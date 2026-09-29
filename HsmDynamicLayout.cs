using System;

namespace HsmAdapter;

/// <summary>HSM DynamicHint target coordinates and caller-owned rich text.</summary>
public sealed class HsmDynamicLayout
{
    public HsmDynamicLayout(string richText, float targetX, float targetY, int fontSize = 24,
        HsmSyncSpeed syncSpeed = HsmSyncSpeed.Default, bool fastUpdate = true,
        float lineHeight = 0, bool hide = false, bool forceUpdate = true,
        bool forceMembershipUpdate = true, bool fastMembershipUpdate = true)
    {
        RichText = richText ?? throw new ArgumentNullException(nameof(richText));
        TextLayout.ValidateNumber(targetX, nameof(targetX)); TextLayout.ValidateNumber(targetY, nameof(targetY));
        TextLayout.ValidateNumber(lineHeight, nameof(lineHeight));
        if (fontSize < 1 || fontSize > 120) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (lineHeight < 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        if (!Enum.IsDefined(typeof(HsmSyncSpeed), syncSpeed)) throw new ArgumentOutOfRangeException(nameof(syncSpeed));
        TargetX = targetX; TargetY = targetY; FontSize = fontSize; SyncSpeed = syncSpeed;
        FastUpdate = fastUpdate; LineHeight = lineHeight; Hide = hide;
        ForceUpdate = forceUpdate; ForceMembershipUpdate = forceMembershipUpdate;
        FastMembershipUpdate = fastMembershipUpdate;
    }

    public string RichText { get; }
    public float TargetX { get; }
    public float TargetY { get; }
    public int FontSize { get; }
    public HsmSyncSpeed SyncSpeed { get; }
    public bool FastUpdate { get; }
    public float LineHeight { get; }
    public bool Hide { get; }
    public bool ForceUpdate { get; }
    public bool ForceMembershipUpdate { get; }
    public bool FastMembershipUpdate { get; }
}
