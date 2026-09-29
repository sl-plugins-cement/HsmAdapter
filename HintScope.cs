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

    /// <summary>Places structured text in a shared priority region for this player.</summary>
    public NoticeResult ShowNotice(Player player, string key, NoticeRegion region, IEnumerable<TextRow> rows,
        float height = 100, int priority = 0, float duration = 0)
    {
        Validate(player, key, duration);
        var copy = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        if (copy.Length == 0 || copy.Any(r => r == null)) throw new ArgumentException("Supply rows.", nameof(rows));
        return ShowNoticeCore(player, key, region, copy, null, height, priority, duration, 24, 0);
    }

    /// <summary>Places HSM rich text in a shared priority region; height is caller-declared.</summary>
    public NoticeResult ShowHsmNotice(Player player, string key, NoticeRegion region, string richText,
        float height = 100, int priority = 0, float duration = 0, int fontSize = 24, float lineHeight = 0)
    {
        Validate(player, key, duration);
        if (richText == null) throw new ArgumentNullException(nameof(richText));
        if (fontSize < 1 || fontSize > 120) throw new ArgumentOutOfRangeException(nameof(fontSize));
        TextLayout.ValidateNumber(lineHeight, nameof(lineHeight));
        if (lineHeight < 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        return ShowNoticeCore(player, key, region, null, richText, height, priority, duration, fontSize, lineHeight);
    }

    private NoticeResult ShowNoticeCore(Player player, string key, NoticeRegion region, IReadOnlyList<TextRow>? rows,
        string? richText, float height, int priority, float duration, int fontSize, float lineHeight)
    {
        if (!Enum.IsDefined(typeof(NoticeRegion), region)) throw new ArgumentOutOfRangeException(nameof(region));
        TextLayout.ValidateNumber(height, nameof(height));
        var bounds = NoticeCoordinator.RegionBounds(region);
        if (height <= 0 || height > bounds.Height) return NoticeResult.DoesNotFit;
        if (!Hints.Enabled || player.IsDestroyed) return NoticeResult.Unavailable;
        var backend = TextBackends.Screen;
        if (backend == null) return NoticeResult.Unavailable;
        if (richText != null && backend is not IRichNoticeBackend) return NoticeResult.Unsupported;
        var layout = new ScreenRect(bounds.X, bounds.Y, bounds.Width, height);
        if (richText == null)
        {
            var result = backend.Prepare(new ScreenTextLayout(layout, rows!, verticalAlignment: VerticalAnchor.Middle), out _);
            if (result != ScreenTextResult.Shown) return result == ScreenTextResult.Unsupported ? NoticeResult.Unsupported : NoticeResult.DoesNotFit;
        }
        return NoticeCoordinator.Show(this, player, key, region, rows, richText, height, priority,
            duration, fontSize, lineHeight, backend);
    }

    /// <summary>Returns this scope's current notice state for a key, or null if it has no notice.</summary>
    public NoticeResult? GetNoticeState(Player player, string key)
    {
        Check();
        if (player == null) throw new ArgumentNullException(nameof(player));
        if (key == null) throw new ArgumentNullException(nameof(key));
        return NoticeCoordinator.State(this, player.ReferenceHub, key);
    }

    /// <summary>Shows legacy HSM rich text while reserving its declared screen rectangle under the same key.</summary>
    public NoticeResult ShowHsmReserved(Player player, string key, HsmHintLayout layout,
        ScreenRect reservation, float duration = 0)
    {
        Validate(player, key, duration);
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        ValidateReservation(reservation);
        if (!ShowHsmCore(player, key, layout, null, duration, preserveCoordinator: true)) return NoticeResult.Unavailable;
        NoticeCoordinator.Reserve(this, player.ReferenceHub, key, reservation, duration);
        return NoticeResult.Visible;
    }

    /// <summary>Reserves a screen rectangle for a caller-owned visual under this scope's key.</summary>
    public NoticeResult ReserveScreen(Player player, string key, ScreenRect reservation, float duration = 0)
    {
        Validate(player, key, duration);
        ValidateReservation(reservation);
        if (!Hints.Enabled || player.IsDestroyed) return NoticeResult.Unavailable;
        NoticeCoordinator.ReserveOnly(this, player.ReferenceHub, key, reservation, duration);
        return NoticeResult.Visible;
    }

    private static void ValidateReservation(ScreenRect reservation)
    {
        if (reservation == null) throw new ArgumentNullException(nameof(reservation));
        if (reservation.X < 0 || reservation.Y < 0 ||
            (double)reservation.X + reservation.Width > 1920 || (double)reservation.Y + reservation.Height > 1080)
            throw new ArgumentOutOfRangeException(nameof(reservation));
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

    private bool ShowHsmCore(Player player, string key, HsmHintLayout layout, Func<string>? autoText, float duration,
        bool preserveCoordinator = false)
    {
        if (layout == null) throw new ArgumentNullException(nameof(layout));
        return Put(player, key, new List<RenderedRow> { new RenderedRow {
            Text = layout.RichText, X = layout.X, Y = layout.Y, Size = layout.FontSize,
            Anchor = layout.Anchor, Alignment = layout.Alignment, SyncSpeed = layout.SyncSpeed,
            LineHeight = layout.LineHeight, Hide = layout.Hide, AutoText = autoText
        } }, duration, layout.FastUpdate, layout.ForceUpdate,
            layout.ForceMembershipUpdate, layout.FastMembershipUpdate, preserveCoordinator);
    }

    private bool Put(Player player, string key, List<RenderedRow> rows, float duration,
        bool fastUpdate = true, bool forceUpdate = true,
        bool forceMembershipUpdate = true, bool fastMembershipUpdate = true,
        bool preserveCoordinator = false)
    {
        Validate(player, key, duration);
        if (!Hints.Enabled || player.IsDestroyed) return false;
        var backend = HsmBackend.Ready();
        if (backend == null) return false;
        Put(player, key, backend, new HsmTextFrame(rows, fastUpdate, forceUpdate,
            forceMembershipUpdate, fastMembershipUpdate), duration, preserveCoordinator);
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
    private void Put(Player player, string key, ITextBackend backend, ITextFrame frame, float duration,
        bool preserveCoordinator = false)
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
            if (!preserveCoordinator) NoticeCoordinator.Remove(this, player.ReferenceHub, key);
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
        if (!_entries.TryGetValue((hub, key), out var entry)) { NoticeCoordinator.Remove(this, hub, key); return; }
        try { if (hub == null) entry.Handle.Forget(); else entry.Handle.Remove(true); }
        finally { _entries.Remove((hub!, key)); NoticeCoordinator.Remove(this, hub, key); }
    }
    internal void RemoveRendered(ReferenceHub hub, string key)
    {
        if (!_entries.TryGetValue((hub, key), out var entry)) return;
        try { if (hub == null) entry.Handle.Forget(); else entry.Handle.Remove(true); }
        finally { _entries.Remove((hub!, key)); }
    }
    internal void PutNotice(ReferenceHub hub, string key, ITextBackend backend, ITextFrame frame)
    {
        var index = (hub, key);
        if (_entries.TryGetValue(index, out var previous) && !ReferenceEquals(previous.Backend, backend)) RemoveRendered(hub, key);
        if (!_entries.TryGetValue(index, out var entry))
        {
            entry = new Entry(backend, backend.CreateHandle(hub, _group, key));
            _entries.Add(index, entry);
        }
        try { entry.Handle.Update(frame); entry.Expires = float.PositiveInfinity; }
        catch { RemoveRendered(hub, key); throw; }
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
            try { RemoveRendered(key.Hub, key.Key); }
            catch (Exception ex) { LabApi.Features.Console.Logger.Error("[HsmAdapter] Hint cleanup failed: " + ex); }
        NoticeCoordinator.RemovePlayer(this, hub);
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
        NoticeCoordinator.RemovePlayer(this, hub, false);
    }
    public void Clear()
    {
        Check();
        foreach (var key in _entries.Keys.ToArray())
            try { RemoveRendered(key.Hub, key.Key); }
            catch (Exception ex) { LabApi.Features.Console.Logger.Error("[HsmAdapter] Hint cleanup failed: " + ex); }
        NoticeCoordinator.RemoveScope(this);
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
