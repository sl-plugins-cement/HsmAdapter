using System;
using System.Collections.Generic;
using System.Linq;

namespace HsmAdapter;

public enum TextAlignment { Center, Left, Right }
public enum TextOverflow { Wrap, Reject, Clip, Ellipsis }
public enum ScreenTextResult { Shown, Unavailable, Unsupported, OutOfBounds, DoesNotFit }

[Flags]
public enum ScreenTextFeatures
{
    None = 0, CenterAlignment = 1, LeftAlignment = 2, RightAlignment = 4,
    Wrap = 8, Clip = 16, Ellipsis = 32, FullCanvasPositioning = 64,
}

/// <summary>A rectangle in a 1920 by 1080 reference canvas, with origin at its top left.</summary>
public sealed class ScreenRect
{
    public ScreenRect(float x, float y, float width, float height)
    {
        TextLayout.ValidateNumber(x, nameof(x)); TextLayout.ValidateNumber(y, nameof(y));
        TextLayout.ValidateNumber(width, nameof(width)); TextLayout.ValidateNumber(height, nameof(height));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        X = x; Y = y; Width = width; Height = height;
    }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
}

/// <summary>Backend-independent text intent. Font size and gap use reference-canvas units.
/// Bounds are not a clipping guarantee; HSM wraps using a conservative width budget.</summary>
public sealed class ScreenTextLayout
{
    public ScreenTextLayout(ScreenRect bounds, IEnumerable<TextRow> rows,
        TextAlignment alignment = TextAlignment.Center, VerticalAnchor verticalAlignment = VerticalAnchor.Top,
        TextOverflow overflow = TextOverflow.Wrap, float rowGap = 8)
    {
        Bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));
        var copy = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        if (copy.Length == 0 || copy.Any(r => r == null)) throw new ArgumentException("Supply rows.", nameof(rows));
        if (!Enum.IsDefined(typeof(TextAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(typeof(VerticalAnchor), verticalAlignment)) throw new ArgumentOutOfRangeException(nameof(verticalAlignment));
        if (!Enum.IsDefined(typeof(TextOverflow), overflow)) throw new ArgumentOutOfRangeException(nameof(overflow));
        TextLayout.ValidateNumber(rowGap, nameof(rowGap));
        if (rowGap < 0 || rowGap > 1080) throw new ArgumentOutOfRangeException(nameof(rowGap));
        Rows = Array.AsReadOnly(copy); Alignment = alignment; VerticalAlignment = verticalAlignment;
        Overflow = overflow; RowGap = rowGap;
    }
    public ScreenRect Bounds { get; }
    public IReadOnlyList<TextRow> Rows { get; }
    public TextAlignment Alignment { get; }
    public VerticalAnchor VerticalAlignment { get; }
    public TextOverflow Overflow { get; }
    public float RowGap { get; }
}
