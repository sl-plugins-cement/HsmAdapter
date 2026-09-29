using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace HsmAdapter;

public enum NoticeRegion { Top, Center, Bottom }
public enum NoticeResult { Visible, Queued, Unavailable, Unsupported, DoesNotFit }

/// <summary>Game-thread, player-local placement policy shared by all scopes.</summary>
internal static class NoticeCoordinator
{
    private sealed class Item
    {
        internal HintScope Scope = null!;
        internal ReferenceHub Hub = null!;
        internal string Key = "";
        internal NoticeRegion Region;
        internal int Priority;
        internal long Order;
        internal float Height, Expires;
        internal IReadOnlyList<TextRow>? Rows;
        internal string? RichText;
        internal int FontSize;
        internal float LineHeight;
        internal bool Visible;
        internal bool Dirty;
        internal float Y;
    }
    private sealed class Reservation
    {
        internal HintScope Scope = null!;
        internal ReferenceHub Hub = null!;
        internal string Key = "";
        internal ScreenRect Rect = null!;
        internal float Expires;
    }
    private static readonly List<Item> Items = new();
    private static readonly List<Reservation> Reservations = new();
    private static long _nextOrder;
    private static readonly ScreenRect[] Bounds = {
        new ScreenRect(510, 260, 900, 172),
        new ScreenRect(510, 452, 900, 260),
        new ScreenRect(510, 732, 900, 218)
    };
    internal static ScreenRect RegionBounds(NoticeRegion region) => Bounds[(int)region];
    internal static NoticeResult? State(HintScope scope, ReferenceHub hub, string key)
    {
        var item = Items.FirstOrDefault(i => ReferenceEquals(i.Scope, scope) && ReferenceEquals(i.Hub, hub) && i.Key == key);
        return item == null ? (NoticeResult?)null : item.Visible ? NoticeResult.Visible : NoticeResult.Queued;
    }

    internal static NoticeResult Show(HintScope scope, Player player, string key, NoticeRegion region,
        IReadOnlyList<TextRow>? rows, string? richText, float height, int priority, float duration,
        int fontSize, float lineHeight, ITextBackend backend)
    {
        var hub = player.ReferenceHub;
        var existing = Items.FirstOrDefault(i => ReferenceEquals(i.Scope, scope) && ReferenceEquals(i.Hub, hub) && i.Key == key);
        var item = existing ?? new Item { Scope = scope, Hub = hub, Key = key, Order = ++_nextOrder };
        // A queued notice owns its key. An existing visible notice retains its handle on refresh.
        if (existing == null) scope.RemoveRendered(hub, key);
        Reservations.RemoveAll(r => ReferenceEquals(r.Scope, scope) && ReferenceEquals(r.Hub, hub) && r.Key == key);
        item.Region = region; item.Rows = rows; item.RichText = richText; item.Height = height;
        item.Priority = priority; item.Expires = duration == 0 ? float.PositiveInfinity : Time.realtimeSinceStartup + duration;
        item.FontSize = fontSize; item.LineHeight = lineHeight; item.Dirty = true;
        if (existing == null) Items.Add(item);
        Reflow(hub, backend);
        return item.Visible ? NoticeResult.Visible : NoticeResult.Queued;
    }

    internal static void Reserve(HintScope scope, ReferenceHub hub, string key, ScreenRect rect, float duration)
    {
        Items.RemoveAll(i => ReferenceEquals(i.Scope, scope) && ReferenceEquals(i.Hub, hub) && i.Key == key);
        Reservations.RemoveAll(r => ReferenceEquals(r.Scope, scope) && ReferenceEquals(r.Hub, hub) && r.Key == key);
        Reservations.Add(new Reservation { Scope = scope, Hub = hub, Key = key, Rect = rect,
            Expires = duration == 0 ? float.PositiveInfinity : Time.realtimeSinceStartup + duration });
        Reflow(hub);
    }

    internal static void ReserveOnly(HintScope scope, ReferenceHub hub, string key, ScreenRect rect, float duration)
    {
        scope.RemoveRendered(hub, key);
        Reserve(scope, hub, key, rect, duration);
    }

    internal static void Remove(HintScope scope, ReferenceHub hub, string key)
    {
        bool changed = Items.RemoveAll(i => ReferenceEquals(i.Scope, scope) && ReferenceEquals(i.Hub, hub) && i.Key == key) > 0;
        changed |= Reservations.RemoveAll(r => ReferenceEquals(r.Scope, scope) && ReferenceEquals(r.Hub, hub) && r.Key == key) > 0;
        if (changed) Reflow(hub);
    }
    internal static void RemovePlayer(HintScope scope, ReferenceHub hub, bool reflow = true)
    {
        Items.RemoveAll(i => ReferenceEquals(i.Scope, scope) && ReferenceEquals(i.Hub, hub));
        Reservations.RemoveAll(r => ReferenceEquals(r.Scope, scope) && ReferenceEquals(r.Hub, hub));
        if (reflow) Reflow(hub);
    }
    internal static void RemoveScope(HintScope scope, bool reflow = true)
    {
        var hubs = Items.Where(i => ReferenceEquals(i.Scope, scope)).Select(i => i.Hub)
            .Concat(Reservations.Where(r => ReferenceEquals(r.Scope, scope)).Select(r => r.Hub)).Distinct().ToArray();
        Items.RemoveAll(i => ReferenceEquals(i.Scope, scope));
        Reservations.RemoveAll(r => ReferenceEquals(r.Scope, scope));
        if (reflow) foreach (var hub in hubs) Reflow(hub);
    }
    internal static void Forget(ReferenceHub hub)
    {
        Items.RemoveAll(i => ReferenceEquals(i.Hub, hub));
        Reservations.RemoveAll(r => ReferenceEquals(r.Hub, hub));
    }
    internal static void Sweep()
    {
        float now = Time.realtimeSinceStartup;
        var expired = Items.Where(i => i.Expires <= now).ToArray();
        foreach (var item in expired) if (item.Visible) SafeRemove(item);
        var hubs = expired.Select(i => i.Hub).Distinct().ToArray();
        Items.RemoveAll(i => i.Expires <= now);
        var expiredReservations = Reservations.Where(r => r.Expires <= now).ToArray();
        Reservations.RemoveAll(r => r.Expires <= now);
        foreach (var hub in hubs.Concat(expiredReservations.Select(r => r.Hub)).Distinct()) Reflow(hub);
    }
    internal static void ClearAll()
    {
        Items.Clear(); Reservations.Clear();
    }

    private static bool Intersects(ScreenRect a, ScreenRect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static void SafeRemove(Item item)
    {
        try { item.Scope.RemoveRendered(item.Hub, item.Key); }
        catch (Exception ex) { LabApi.Features.Console.Logger.Error("[HsmAdapter] Notice cleanup failed: " + ex); }
    }

    private static void Reflow(ReferenceHub hub, ITextBackend? backend = null)
    {
        backend ??= TextBackends.Screen;
        if (backend == null) return;
        foreach (NoticeRegion region in Enum.GetValues(typeof(NoticeRegion)))
        {
            var box = RegionBounds(region);
            float cursor = box.Y;
            foreach (var item in Items.Where(i => ReferenceEquals(i.Hub, hub) && i.Region == region)
                         .OrderByDescending(i => i.Priority).ThenBy(i => i.Order))
            {
                float y = cursor;
                while (true)
                {
                    var candidate = new ScreenRect(box.X, y, box.Width, item.Height);
                    var conflict = Reservations.FirstOrDefault(r => ReferenceEquals(r.Hub, hub) && Intersects(candidate, r.Rect));
                    if (conflict == null) break;
                    y = conflict.Rect.Y + conflict.Rect.Height + 12;
                }
                if (y + item.Height > box.Y + box.Height)
                {
                    if (item.Visible) SafeRemove(item);
                    item.Visible = false;
                    continue;
                }
                if (!item.Visible || item.Y != y || item.Dirty)
                {
                    var frame = Prepare(item, backend, y);
                    if (frame == null)
                    {
                        if (item.Visible) SafeRemove(item);
                        item.Visible = false;
                        continue;
                    }
                    try
                    {
                        item.Scope.PutNotice(hub, item.Key, backend, frame);
                        item.Visible = true; item.Y = y; item.Dirty = false;
                    }
                    catch (Exception ex)
                    {
                        item.Visible = false; item.Dirty = true;
                        LabApi.Features.Console.Logger.Error("[HsmAdapter] Notice render failed: " + ex);
                        continue;
                    }
                }
                cursor = y + item.Height + 12;
            }
        }
    }

    internal static ITextFrame? Prepare(IReadOnlyList<TextRow>? rows, string? richText, int fontSize,
        float lineHeight, ITextBackend backend, ScreenRect rect)
    {
        if (richText != null)
            return (backend as IRichNoticeBackend)?.PrepareRichNotice(richText, fontSize, lineHeight, rect);
        var layout = new ScreenTextLayout(rect, rows!, verticalAlignment: VerticalAnchor.Middle);
        return backend.Prepare(layout, out var frame) == ScreenTextResult.Shown ? frame : null;
    }
    private static ITextFrame? Prepare(Item item, ITextBackend backend, float y) =>
        Prepare(item.Rows, item.RichText, item.FontSize, item.LineHeight, backend,
            new ScreenRect(RegionBounds(item.Region).X, y, RegionBounds(item.Region).Width, item.Height));
}
