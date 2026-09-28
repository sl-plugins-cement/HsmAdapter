using System;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using LabApi.Loader;
using LabApi.Features.Console;

namespace HsmAdapter;

internal sealed class HsmBackend : ITextBackend
{
    public ScreenTextFeatures Features => ScreenTextFeatures.CenterAlignment | ScreenTextFeatures.Wrap;

    public ITextHandle CreateHandle(ReferenceHub hub, string group, string key) => new HsmTextHandle(this, hub, group, key);

    public ScreenTextResult Prepare(ScreenTextLayout layout, out ITextFrame? frame)
    {
        frame = null;
        var box = layout.Bounds;
        if (box.X < 0 || box.Y < 0 || (double)box.X + box.Width > 1920 || (double)box.Y + box.Height > 1080)
            return ScreenTextResult.OutOfBounds;
        if (layout.Alignment != TextAlignment.Center || layout.Overflow == TextOverflow.Clip || layout.Overflow == TextOverflow.Ellipsis)
            return ScreenTextResult.Unsupported;
        System.Collections.Generic.List<RenderedRow> rows;
        try { rows = LayoutRenderer.BuildRows(layout.Rows, box.Width, layout.Overflow == TextOverflow.Wrap); }
        catch (LayoutRenderer.UnsafeTextException) { return ScreenTextResult.Unsupported; }
        catch (ArgumentException) { return ScreenTextResult.DoesNotFit; }
        float height = rows.Sum(r => r.Size) + layout.RowGap * (rows.Count - 1);
        if (height > box.Height) return ScreenTextResult.DoesNotFit;
        float y = box.Y + (layout.VerticalAlignment == VerticalAnchor.Bottom ? box.Height - height :
            layout.VerticalAlignment == VerticalAnchor.Middle ? (box.Height - height) / 2 : 0);
        // Native 1920x1080 observations: row center = 960 + HSM caret / 2.
        float x = (float)Math.Floor((box.X + box.Width / 2 - 960) * 2);
        if (x < -800 || rows.Any(r => x + r.Width > 1100)) return ScreenTextResult.OutOfBounds;
        foreach (var row in rows)
        {
            row.X = x; row.Y = (float)Math.Floor(y); row.Anchor = VerticalAnchor.Top;
            y += row.Size + layout.RowGap;
        }
        frame = new HsmTextFrame(rows);
        return ScreenTextResult.Shown;
    }

    private readonly Type _hint;
    private readonly Assembly _assembly;
    private readonly Type? _dynamicHint;
    private readonly MethodInfo _get, _add, _remove, _update;
    private readonly FieldInfo? _isDestructed;
    private readonly PropertyInfo _text, _x, _y, _size, _anchor, _id, _alignment;
    private readonly PropertyInfo? _lineHeight, _hide, _syncSpeed, _autoText;
    private readonly PropertyInfo? _targetX, _targetY;
    private readonly object[] _anchors;
    private readonly object?[] _alignments;
    private readonly object? _coordinateTools;
    private readonly MethodInfo? _measureWidth;
    private static HsmBackend? _instance;
    private static DateTime _nextAttempt;

    private HsmBackend(Assembly assembly)
    {
        _assembly = assembly;
        Type Need(string name) => assembly.GetType("HintServiceMeow.Core." + name, true)!;
        var display = Need("Utilities.PlayerDisplay");
        _isDestructed = display.GetField("isDestructed", BindingFlags.NonPublic | BindingFlags.Instance);
        _hint = Need("Models.Hints.Hint");
        _dynamicHint = assembly.GetType("HintServiceMeow.Core.Models.Hints.DynamicHint");
        _targetX = _dynamicHint?.GetProperty("TargetX");
        _targetY = _dynamicHint?.GetProperty("TargetY");
        var abstractHint = Need("Models.Hints.AbstractHint");
        _get = display.GetMethod("Get", new[] { typeof(ReferenceHub) }) ?? throw new MissingMethodException("PlayerDisplay.Get(ReferenceHub)");
        _add = display.GetMethod("AddHint", new[] { abstractHint, typeof(string) }) ?? throw new MissingMethodException("AddHint");
        _remove = display.GetMethod("RemoveHint", new[] { abstractHint, typeof(string) }) ?? throw new MissingMethodException("RemoveHint");
        _update = display.GetMethod("ForceUpdate", new[] { typeof(bool) }) ?? throw new MissingMethodException("ForceUpdate");
        PropertyInfo Property(string name, Type? type = null)
        {
            var property = _hint.GetProperty(name);
            if (property?.CanWrite != true || (type != null && property.PropertyType != type)) throw new MissingMemberException(name);
            return property;
        }
        _text = Property("Text", typeof(string)); _x = Property("XCoordinate", typeof(float));
        _y = Property("YCoordinate", typeof(float)); _size = Property("FontSize", typeof(int));
        _id = Property("Id", typeof(string)); _anchor = Property("YCoordinateAlign");
        _alignment = Property("Alignment");
        _lineHeight = abstractHint.GetProperty("LineHeight");
        _hide = abstractHint.GetProperty("Hide");
        _syncSpeed = abstractHint.GetProperty("SyncSpeed");
        _autoText = abstractHint.GetProperty("AutoText");
        _anchors = new[] { "Top", "Middle", "Bottom" }.Select(n => Enum.Parse(_anchor.PropertyType, n)).ToArray();
        object? Alignment(string name)
        {
            try { return Enum.Parse(_alignment.PropertyType, name); }
            catch { return null; }
        }
        _alignments = new[] { Alignment("Center") ?? throw new MissingMemberException("HSM Center alignment"),
            Alignment("Left"), Alignment("Right") };
        try
        {
            var toolsType = assembly.GetType("HintServiceMeow.Core.Utilities.Tools.CoordinateTools");
            var constructor = toolsType?.GetConstructors().FirstOrDefault();
            _coordinateTools = constructor == null ? null : constructor.Invoke(
                constructor.GetParameters().Select(_ => (object?)null).ToArray());
            _measureWidth = toolsType?.GetMethods().FirstOrDefault(m => m.Name == "GetTextWidth" &&
                m.GetParameters().Length >= 2 && m.GetParameters()[0].ParameterType == typeof(string) &&
                m.GetParameters()[1].ParameterType == typeof(int));
        }
        catch { _coordinateTools = null; _measureWidth = null; }
    }

    internal static HsmBackend? Ready()
    {
        // A loaded assembly alone is insufficient: a disabled HSM must not receive displays.
        var assembly = EnabledAssembly();
        if (assembly == null) return null;
        if (_instance != null && _instance._assembly == assembly) return _instance;
        if (DateTime.UtcNow < _nextAttempt) return null;
        _nextAttempt = DateTime.UtcNow.AddSeconds(5);
        try { return _instance = new HsmBackend(assembly); }
        catch (Exception ex) { Logger.Warn("[HsmAdapter] Incompatible HSM API: " + ex.GetBaseException().Message); return null; }
    }

    private static Assembly? EnabledAssembly()
    {
        var labPlugin = PluginLoader.EnabledPlugins.FirstOrDefault(p =>
            p.GetType().Assembly.GetName().Name == "HintServiceMeow");
        if (labPlugin != null) return labPlugin.GetType().Assembly;
        if (!PluginLoader.EnabledPlugins.Any(p => p.Name == "Exiled Loader")) return null;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            a.GetName().Name == "HintServiceMeow-Exiled");
        var pluginType = assembly?.GetType("HintServiceMeow.Plugin.Plugin");
        try
        {
            var instance = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var config = instance?.GetType().GetProperty("Config")?.GetValue(instance);
            var enabled = config?.GetType().GetProperty("IsEnabled")?.GetValue(config);
            return enabled is true ? assembly : null;
        }
        catch { return null; }
    }

    internal object Display(ReferenceHub hub) => _get.Invoke(null, new object[] { hub })!;
    internal bool IsDestructed(object display) => _isDestructed?.GetValue(display) is true;
    internal float? MeasureWidth(string richText, int fontSize)
    {
        if (_coordinateTools == null || _measureWidth == null) return null;
        try
        {
            var parameters = _measureWidth.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = richText; args[1] = fontSize;
            for (int i = 2; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue :
                parameters[i].ParameterType.IsValueType ? Activator.CreateInstance(parameters[i].ParameterType) : null;
            return Convert.ToSingle(_measureWidth.Invoke(_coordinateTools, args));
        }
        catch { return null; }
    }
    internal object Create(string id, RenderedRow row)
    {
        if (row.Dynamic && (_dynamicHint == null || _targetX?.CanWrite != true || _targetY?.CanWrite != true))
            throw new MissingMemberException("HSM DynamicHint.TargetX/TargetY");
        var hint = Activator.CreateInstance(row.Dynamic ? _dynamicHint! : _hint)!;
        _id.SetValue(hint, id);
        Set(hint, row);
        return hint;
    }
    internal void Set(object hint, RenderedRow row)
    {
        if (row.Dynamic)
        {
            _targetX!.SetValue(hint, row.X); _targetY!.SetValue(hint, row.Y);
        }
        else
        {
            _x.SetValue(hint, row.X); _y.SetValue(hint, row.Y);
            _anchor.SetValue(hint, _anchors[(int)row.Anchor]);
            _alignment.SetValue(hint, _alignments[(int)row.Alignment] ??
                throw new MissingMemberException("HSM " + row.Alignment + " alignment"));
        }
        _size.SetValue(hint, row.Size);
        if (row.LineHeight != 0)
        {
            if (_lineHeight?.CanWrite != true) throw new MissingMemberException("HSM LineHeight");
            _lineHeight.SetValue(hint, row.LineHeight);
        }
        else if (_lineHeight?.CanWrite == true) _lineHeight.SetValue(hint, 0f);
        if (row.Hide)
        {
            if (_hide?.CanWrite != true) throw new MissingMemberException("HSM Hide");
            _hide.SetValue(hint, true);
        }
        else if (_hide?.CanWrite == true) _hide.SetValue(hint, false);
        if (_syncSpeed?.CanWrite == true)
        {
            _syncSpeed.SetValue(hint, Enum.Parse(_syncSpeed.PropertyType,
                row.SyncSpeed == HsmSyncSpeed.Default ? "Normal" : row.SyncSpeed.ToString()));
        }
        else if (row.SyncSpeed != HsmSyncSpeed.Default) throw new MissingMemberException("HSM SyncSpeed");
        _text.SetValue(hint, row.Text);
        if (row.AutoText != null)
        {
            if (_autoText?.CanWrite != true) throw new MissingMemberException("HSM AutoText");
            var parameterType = _autoText.PropertyType.GetMethod("Invoke")?.GetParameters().Single().ParameterType
                ?? throw new MissingMemberException("HSM AutoText callback signature");
            var parameter = Expression.Parameter(parameterType, "update");
            var body = Expression.Invoke(Expression.Constant(row.AutoText));
            _autoText.SetValue(hint, Expression.Lambda(_autoText.PropertyType, body, parameter).Compile());
        }
    }
    internal void Add(object display, object hint, string group) => _add.Invoke(display, new[] { hint, group });
    internal void Remove(object display, object hint, string group) => _remove.Invoke(display, new[] { hint, group });
    internal void Update(object display, bool fast = true) => _update.Invoke(display, new object[] { fast });
}
