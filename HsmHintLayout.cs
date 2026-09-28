using System;

namespace HsmAdapter;

/// <summary>HSM-specific hint properties. Coordinates and rich text retain HSM semantics.</summary>
public sealed class HsmHintLayout
{
    public HsmHintLayout(string richText, float x, float y, int fontSize = 24,
        VerticalAnchor anchor = VerticalAnchor.Top, HsmHorizontalAlignment alignment = HsmHorizontalAlignment.Center,
        HsmSyncSpeed syncSpeed = HsmSyncSpeed.Default, bool fastUpdate = true,
        float lineHeight = 0, bool hide = false, bool forceUpdate = true,
        bool forceMembershipUpdate = true, bool fastMembershipUpdate = true)
    {
        RichText = richText ?? throw new ArgumentNullException(nameof(richText));
        TextLayout.ValidateNumber(x, nameof(x)); TextLayout.ValidateNumber(y, nameof(y));
        TextLayout.ValidateNumber(lineHeight, nameof(lineHeight));
        if (fontSize < 1 || fontSize > 120) throw new ArgumentOutOfRangeException(nameof(fontSize));
        if (lineHeight < 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        if (!Enum.IsDefined(typeof(VerticalAnchor), anchor)) throw new ArgumentOutOfRangeException(nameof(anchor));
        if (!Enum.IsDefined(typeof(HsmHorizontalAlignment), alignment)) throw new ArgumentOutOfRangeException(nameof(alignment));
        if (!Enum.IsDefined(typeof(HsmSyncSpeed), syncSpeed)) throw new ArgumentOutOfRangeException(nameof(syncSpeed));
        X = x; Y = y; FontSize = fontSize; Anchor = anchor; Alignment = alignment;
        SyncSpeed = syncSpeed; FastUpdate = fastUpdate; LineHeight = lineHeight; Hide = hide;
        ForceUpdate = forceUpdate; ForceMembershipUpdate = forceMembershipUpdate;
        FastMembershipUpdate = fastMembershipUpdate;
    }

    public string RichText { get; }
    public float X { get; }
    public float Y { get; }
    public int FontSize { get; }
    public VerticalAnchor Anchor { get; }
    public HsmHorizontalAlignment Alignment { get; }
    public HsmSyncSpeed SyncSpeed { get; }
    public bool FastUpdate { get; }
    public float LineHeight { get; }
    public bool Hide { get; }
    public bool ForceUpdate { get; }
    public bool ForceMembershipUpdate { get; }
    public bool FastMembershipUpdate { get; }
}

public enum HsmHorizontalAlignment { Center, Left, Right }

/// <summary>Default leaves HSM's value untouched. Named values map to HSM enum names.</summary>
public enum HsmSyncSpeed { Default, UnSync, Slowest, Slow, Normal, Fast, Fastest }
