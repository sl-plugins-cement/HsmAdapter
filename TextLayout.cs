using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HsmAdapter;

public enum TextCase { Preserve, Upper, Lower }
public enum VerticalAnchor { Top, Middle, Bottom }

public sealed class TextSpan
{
    public TextSpan(string text, string color = "#FFFFFF", bool bold = false, TextCase casing = TextCase.Preserve)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        if (!Regex.IsMatch(color ?? "", "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"))
            throw new ArgumentException("Use #RRGGBB or #RRGGBBAA.", nameof(color));
        if (!Enum.IsDefined(typeof(TextCase), casing)) throw new ArgumentOutOfRangeException(nameof(casing));
        Color = color!; Bold = bold; Casing = casing;
    }
    public string Text { get; }
    public string Color { get; }
    public bool Bold { get; }
    public TextCase Casing { get; }
}

public sealed class TextRow
{
    public TextRow(int fontSize, params TextSpan[] spans)
    {
        if (fontSize < 6 || fontSize > 96) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (spans == null || spans.Length == 0 || spans.Any(s => s == null)) throw new ArgumentException("Supply text spans.", nameof(spans));
        FontSize = fontSize; Spans = Array.AsReadOnly((TextSpan[])spans.Clone());
    }
    public int FontSize { get; }
    public IReadOnlyList<TextSpan> Spans { get; }
}

public sealed class TextLayout
{
    public TextLayout(float x, float y, IEnumerable<TextRow> rows, VerticalAnchor anchor = VerticalAnchor.Top, float maxWidth = 900, float rowGap = 8)
    {
        ValidateNumber(x, nameof(x)); ValidateNumber(y, nameof(y));
        ValidateNumber(maxWidth, nameof(maxWidth)); ValidateNumber(rowGap, nameof(rowGap));
        if (maxWidth < 130 || maxWidth > 1900) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (rowGap < 0 || rowGap > 1080) throw new ArgumentOutOfRangeException(nameof(rowGap));
        if (!Enum.IsDefined(typeof(VerticalAnchor), anchor)) throw new ArgumentOutOfRangeException(nameof(anchor));
        var copy = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        if (copy.Length == 0 || copy.Any(r => r == null)) throw new ArgumentException("Supply rows.", nameof(rows));
        X = x; Y = y; Rows = Array.AsReadOnly(copy); Anchor = anchor; MaxWidth = maxWidth; RowGap = rowGap;
    }
    public float X { get; }
    public float Y { get; }
    public IReadOnlyList<TextRow> Rows { get; }
    public VerticalAnchor Anchor { get; }
    public float MaxWidth { get; }
    public float RowGap { get; }
    internal static void ValidateNumber(float value, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name);
    }
}

internal sealed class RenderedRow
{
    internal string Text = "";
    internal int Size;
    internal float X, Y, Width;
    internal VerticalAnchor Anchor;
    internal HsmHorizontalAlignment Alignment = HsmHorizontalAlignment.Center;
    internal HsmSyncSpeed SyncSpeed;
    internal float LineHeight;
    internal bool Hide;
    internal Func<string>? AutoText;
    internal bool Dynamic;
    internal bool SameAs(RenderedRow other) => Text == other.Text && Size == other.Size &&
        X == other.X && Y == other.Y && Anchor == other.Anchor && Alignment == other.Alignment &&
        SyncSpeed == other.SyncSpeed && LineHeight == other.LineHeight && Hide == other.Hide &&
        Dynamic == other.Dynamic && ReferenceEquals(AutoText, other.AutoText);
}

internal static class LayoutRenderer
{
    internal sealed class UnsafeTextException : ArgumentException { }
    internal static List<RenderedRow> Render(TextLayout layout)
    {
        var result = BuildRows(layout.Rows, layout.MaxWidth, true);
        float height = result.Sum(r => r.Size) + layout.RowGap * (result.Count - 1);
        float y = layout.Y - (layout.Anchor == VerticalAnchor.Bottom ? height : layout.Anchor == VerticalAnchor.Middle ? height / 2 : 0);
        if (y < 0 || y + height > 1080) throw new ArgumentException("Layout exceeds the 0–1080 vertical canvas.");
        foreach (var row in result)
        {
            // HSM serializes coordinates using the current culture. Whole units avoid decimal-comma tags.
            row.X = (float)Math.Floor(Math.Max(-800, Math.Min(1100 - row.Width, layout.X)));
            row.Y = (float)Math.Floor(y); row.Anchor = VerticalAnchor.Top;
            y += row.Size + layout.RowGap;
        }
        return result;
    }

    internal static List<RenderedRow> BuildRows(IReadOnlyList<TextRow> rows, float maxWidth, bool wrap)
    {
        var result = new List<RenderedRow>();
        foreach (var row in rows)
        {
            var text = new StringBuilder();
            var run = new StringBuilder();
            TextSpan? runSpan = null;
            string runCase = "";
            float width = 0;
            void EndRun()
            {
                if (run.Length == 0) return;
                text.Append("<color=").Append(runSpan!.Color).Append('>');
                if (runSpan.Bold) text.Append("<b>");
                text.Append('<').Append(runCase).Append('>').Append(run).Append("</").Append(runCase).Append('>');
                if (runSpan.Bold) text.Append("</b>");
                text.Append("</color>");
                run.Clear();
            }
            void Flush()
            {
                EndRun();
                result.Add(new RenderedRow { Text = text.ToString(), Size = row.FontSize, Width = width });
                text.Clear(); width = 0;
                if (result.Count > 32) throw new ArgumentException("A layout may contain at most 32 rendered rows.");
            }
            foreach (var span in row.Spans)
            {
                // HSM rewrites these even inside noparse. Keep the caller's text unchanged on rejection.
                if (span.Text.IndexOfAny(new[] { '<', '>', '{', '}', '\\' }) >= 0 ||
                    span.Text.Any(c => char.IsControl(c) && c != '\n'))
                    throw new UnsafeTextException();
                string content = span.Casing == TextCase.Upper ? span.Text.ToUpperInvariant() :
                    span.Casing == TextCase.Lower ? span.Text.ToLowerInvariant() : span.Text;
                var elements = StringInfo.GetTextElementEnumerator(content);
                while (elements.MoveNext())
                {
                    string element = elements.GetTextElement();
                    if (element == "\n") { Flush(); continue; }
                    // A safety budget, not a claimed font measurement. It includes room for bold and fallback glyphs.
                    float advance = row.FontSize * 1.35f * element.Length;
                    if (advance > maxWidth) throw new ArgumentException("Text element exceeds row width.");
                    if (width + advance > maxWidth && width > 0)
                    {
                        if (!wrap) throw new ArgumentException("Text exceeds row width.");
                        Flush();
                    }
                    // Explicit case tags override the client's native small-caps style and HSM's default metric mode.
                    string casing = char.IsLower(element, 0) ? "lowercase" : "uppercase";
                    if (runSpan != span || runCase != casing) EndRun();
                    runSpan = span; runCase = casing; run.Append(element);
                    width += advance;
                }
            }
            Flush();
        }
        return result;
    }
}
