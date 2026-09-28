using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace HsmAdapter;

/// <summary>Owns only the hints created through this lease. Call from the game thread.</summary>
public sealed class HintScope : IDisposable
{
    private readonly string _group;
    private readonly Dictionary<(ReferenceHub Hub, string Key), Entry> _entries = new();
    private bool _disposed;
    internal HintScope(string owner, string? groupName = null) => _group =
        (groupName ?? "HsmAdapter." + owner) + "." + Guid.NewGuid().ToString("N");

    public bool Show(Player player, string key, TextLayout layout, float duration = 0)
        => Put(player, key, LayoutRenderer.Render(layout ?? throw new ArgumentNullException(nameof(layout))), duration);

    /// <summary>Shows portable screen-space text. A rejected request leaves the existing key unchanged.</summary>
    public ScreenTextResult ShowScreen(Player player, string key, ScreenTextLayout layout, float duration = 0)
    {
        Validate(player, key, duration);
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        if (!Hints.Enabled || player.IsDestroyed) return ScreenTextResult.Unavailable;
        var backend = TextBackends.Screen;
        if (backend == null) return ScreenTextResult.Unavailable;
        var result = backend.Prepare(layout, out var frame);
        if (result != ScreenTextResult.Shown) return result;
        Put(player, key, backend, frame!, duration);
        return ScreenTextResult.Shown;
    }

    /// <summary>Passes caller-owned rich text to HSM without layout repair or case normalization.</summary>
    public bool ShowRaw(Player player, string key, string richText, float x, float y, int fontSize = 24,
        VerticalAnchor anchor = VerticalAnchor.Top, float duration = 0)
        => ShowHsm(player, key, new HsmHintLayout(richText, x, y, fontSize, anchor), duration);

    /// <summary>Shows HSM rich text with HSM geometry and parser behavior.</summary>
    public bool ShowHsm(Player player, string key, HsmHintLayout layout, float duration = 0)
        => ShowHsmCore(player, key, layout, null, duration);

    /// <summary>Installs a persistent HSM AutoText callback; the delegate remains owned by this key.</summary>
    public bool ShowHsmAutoText(Player player, string key, Func<string> text, HsmHintLayout layout, float duration = 0)
        => ShowHsmCore(player, key, layout, text ?? throw new ArgumentNullException(nameof(text)), duration);

    /// <summary>Shows HSM DynamicHint using its target coordinates and overlap behavior.</summary>
    public bool ShowHsmDynamic(Player player, string key, HsmDynamicLayout layout, float duration = 0)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        return Put(player, key, new List<RenderedRow> { new RenderedRow {
            Text = layout.RichText, X = layout.TargetX, Y = layout.TargetY, Size = layout.FontSize,
            SyncSpeed = layout.SyncSpeed, LineHeight = layout.LineHeight, Hide = layout.Hide, Dynamic = true
        } }, duration, layout.FastUpdate, layout.ForceUpdate,
            layout.ForceMembershipUpdate, layout.FastMembershipUpdate);
    }

    private bool ShowHsmCore(Player player, string key, HsmHintLayout layout, Func<string>? autoText, float duration)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        return Put(player, key, new List<RenderedRow> { new RenderedRow {
            Text = layout.RichText, X = layout.X, Y = layout.Y, Size = layout.FontSize,
            Anchor = layout.Anchor, Alignment = layout.Alignment, SyncSpeed = layout.SyncSpeed,
            LineHeight = layout.LineHeight, Hide = layout.Hide, AutoText = autoText
        } }, duration, layout.FastUpdate, layout.ForceUpdate,
            layout.ForceMembershipUpdate, layout.FastMembershipUpdate);
    }

    private bool Put(Player player, string key, List<RenderedRow> rows, float duration,
        bool fastUpdate = true, bool forceUpdate = true,
        bool forceMembershipUpdate = true, bool fastMembershipUpdate = true)
    {
        Validate(player, key, duration);
        if (!Hints.Enabled || player.IsDestroyed) return false;
        var backend = HsmBackend.Ready();
        if (backend == null) return false;
        Put(player, key, backend, new HsmTextFrame(rows, fastUpdate, forceUpdate,
            forceMembershipUpdate, fastMembershipUpdate), duration);
        return true;
    }
    private void Validate(Player player, string key, float duration)
    {
        Check();
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Supply a stable hint key.", nameof(key));
        TextLayout.ValidateNumber(duration, nameof(duration));
        if (duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
    }
    private void Put(Player player, string key, ITextBackend backend, ITextFrame frame, float duration)
    {
        var index = (player.ReferenceHub, key);
        _entries.TryGetValue(index, out var entry);
        if (entry != null && !ReferenceEquals(entry.Backend, backend))
        {
            Remove(player.ReferenceHub, key);
            entry = null;
        }
        if (entry == null)
        {
            entry = new Entry(backend, backend.CreateHandle(player.ReferenceHub, _group, key));
            // Own the handle before mutation so partially added UI is included in cleanup.
            _entries.Add(index, entry);
        }
        try
        {
            entry.Handle.Update(frame);
            // Even unchanged content renews the one deadline on this entry.
            entry.Expires = duration == 0 ? float.PositiveInfinity : Time.realtimeSinceStartup + duration;
        }
        catch
        {
            Remove(player.ReferenceHub, key);
            throw;
        }
    }

    public void Remove(Player player, string key)
    {
        Check();
        if (player == null) throw new ArgumentNullException(nameof(player));
        Remove(player.ReferenceHub, key);
    }
    public void Remove(ReferenceHub hub, string key)
    {
        Check();
        if (ReferenceEquals(hub, null)) throw new ArgumentNullException(nameof(hub));
        RemoveEntry(hub, key);
    }
    private void RemoveEntry(ReferenceHub hub, string key)
    {
        if (!_entries.TryGetValue((hub, key), out var entry)) return;
        try { if (hub == null) entry.Handle.Forget(); else entry.Handle.Remove(true); }
        finally { _entries.Remove((hub!, key)); }
    }
    public void Clear(Player player)
    {
        Check();
        if (player == null) throw new ArgumentNullException(nameof(player));
        Clear(player.ReferenceHub);
    }
    public void Clear(ReferenceHub hub)
    {
        Check();
        if (ReferenceEquals(hub, null)) throw new ArgumentNullException(nameof(hub));
        foreach (var key in _entries.Keys.Where(k => ReferenceEquals(k.Hub, hub)).ToArray())
            TryRemove(key.Hub, key.Key);
    }
    /// <summary>Releases a departed player's entries after HSM may have destructed its display.</summary>
    public void ForgetDisconnected(ReferenceHub? hub)
    {
        Check();
        if (ReferenceEquals(hub, null)) return;
        foreach (var key in _entries.Keys.Where(k => ReferenceEquals(k.Hub, hub)).ToArray())
        {
            if (!_entries.TryGetValue(key, out var entry)) continue;
            entry.Handle.Forget();
            _entries.Remove(key);
        }
    }
    public void Clear()
    {
        Check();
        foreach (var key in _entries.Keys.ToArray()) TryRemove(key.Hub, key.Key);
    }
    private void TryRemove(ReferenceHub hub, string key)
    {
        try { RemoveEntry(hub, key); }
        catch (Exception ex) { LabApi.Features.Console.Logger.Error("[HsmAdapter] Hint cleanup failed: " + ex); }
    }
    internal void Sweep()
    {
        foreach (var pair in _entries.ToArray())
            if (pair.Key.Hub == null) ForgetDisconnected(pair.Key.Hub);
            else if (pair.Value.Expires <= Time.realtimeSinceStartup) RemoveEntry(pair.Key.Hub, pair.Key.Key);
    }
    public void Dispose()
    {
        if (_disposed) return;
        Clear(); _disposed = true; Hints.Release(this);
    }
    private void Check()
    {
        Hints.CheckThread();
        if (_disposed) throw new ObjectDisposedException(nameof(HintScope));
    }
    private sealed class Entry
    {
        internal Entry(ITextBackend backend, ITextHandle handle) { Backend = backend; Handle = handle; }
        internal readonly ITextBackend Backend;
        internal readonly ITextHandle Handle;
        internal float Expires;
    }
}
