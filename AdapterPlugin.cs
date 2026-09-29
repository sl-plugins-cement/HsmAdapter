using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LabApi.Events.Handlers;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;
using MEC;

namespace HsmAdapter;

public static class Hints
{
    private static readonly HashSet<HintScope> Scopes = new();
    internal static bool Enabled;
    internal static int ThreadId;
    public static bool IsReady { get { CheckThread(); return Enabled && HsmBackend.Ready() != null; } }
    /// <summary>Optional HSM rich-text width measurement, in HSM coordinate units.</summary>
    public static float? MeasureHsmWidth(string richText, int fontSize)
    {
        CheckThread();
        if (richText == null) throw new ArgumentNullException(nameof(richText));
        if (fontSize < 6 || fontSize > 96) throw new ArgumentOutOfRangeException(nameof(fontSize));
        return Enabled ? HsmBackend.Ready()?.MeasureWidth(richText, fontSize) : null;
    }
    public static ScreenTextFeatures ScreenFeatures
    {
        get { CheckThread(); return Enabled ? TextBackends.Screen?.Features ?? ScreenTextFeatures.None : ScreenTextFeatures.None; }
    }
    public static HintScope Acquire(string owner, string? groupName = null)
    {
        CheckThread();
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Supply an owner name.", nameof(owner));
        if (groupName != null && string.IsNullOrWhiteSpace(groupName))
            throw new ArgumentException("Supply a group name.", nameof(groupName));
        var scope = new HintScope(owner, groupName); Scopes.Add(scope); return scope;
    }
    internal static void CheckThread()
    {
        if (ThreadId != 0 && Thread.CurrentThread.ManagedThreadId != ThreadId)
            throw new InvalidOperationException("HsmAdapter must be called on the game thread.");
    }
    internal static void Release(HintScope scope) => Scopes.Remove(scope);
    internal static void ClearAll()
    {
        NoticeCoordinator.ClearAll();
        foreach (var scope in Scopes.ToArray()) scope.Clear();
    }
    internal static void PlayerLeft(PlayerLeftEventArgs ev)
    {
        var hub = ev.Player?.ReferenceHub;
        if (hub == null) return;
        // HSM may already have destructed the display in its own Left handler.
        foreach (var scope in Scopes.ToArray()) scope.ForgetDisconnected(hub);
        NoticeCoordinator.Forget(hub);
    }
    internal static IEnumerator<float> Sweep()
    {
        while (Enabled)
        {
            foreach (var scope in Scopes.ToArray())
            {
                try { scope.Sweep(); }
                catch (Exception ex) { Logger.Error("[HsmAdapter] Hint cleanup failed: " + ex); }
            }
            try { NoticeCoordinator.Sweep(); }
            catch (Exception ex) { Logger.Error("[HsmAdapter] Notice sweep failed: " + ex); }
            yield return Timing.WaitForSeconds(0.1f);
        }
    }
}

public sealed class AdapterPlugin : Plugin
{
    private CoroutineHandle _sweep;
    public override string Name => "HsmAdapter";
    public override string Author => "sl-plugins-cement";
    public override string Description => "Owned, structured hint layouts backed by HintServiceMeow";
    public override Version Version => new(1, 3, 1);
    public override Version RequiredApiVersion => new(1, 1, 0);
    public override LoadPriority Priority => LoadPriority.High;
    public override void Enable()
    {
        Hints.ThreadId = Thread.CurrentThread.ManagedThreadId;
        Hints.Enabled = true;
        ServerEvents.WaitingForPlayers += Hints.ClearAll;
        PlayerEvents.Left += Hints.PlayerLeft;
        _sweep = Timing.RunCoroutine(Hints.Sweep());
        Logger.Info("[HsmAdapter] API 1 enabled; HSM readiness is checked when displaying hints.");
    }
    public override void Disable()
    {
        Hints.Enabled = false;
        ServerEvents.WaitingForPlayers -= Hints.ClearAll;
        PlayerEvents.Left -= Hints.PlayerLeft;
        Timing.KillCoroutines(_sweep);
        Hints.ClearAll();
    }
}
