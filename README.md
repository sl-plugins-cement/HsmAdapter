# HsmAdapter

Shared LabAPI plugin for structured text layouts backed by HintServiceMeow. Install exactly one
`HsmAdapter.dll` and one supported `HintServiceMeow.dll` in the port's LabAPI plugin folder.
Consumers reference this project from source (`ProjectReference`); deploy its matching DLL with
the consumer. The adapter also discovers an enabled HSM 5.5 EXILED plugin through the enabled
Exiled Loader when that assembly provides the same public HSM core API. No HSM source changes,
global parser replacement or Harmony patches are required.

Build with `dotnet build HsmAdapter.csproj -c Release`. Override `SCP_SL_MANAGED` for another
dedicated-server installation. The backend checks HSM's public API and enabled-plugin state at
use time. `Hints.IsReady` reports availability; `Show` returns false while unavailable and can be
retried. It does not enqueue missed hints or display a vanilla fallback. Restart the server when
replacing assemblies. The supported baseline is LabAPI 1.1 / HSM 5.5.

## Consumer API

Call all methods on the game thread. Acquire one scope when enabling a consumer and dispose it
when disabling. A scope owns only its hints; identical owner names or keys in other scopes do
not collide. Public API changes are additive within the major version.

```csharp
HintScope hud = HsmAdapter.Hints.Acquire("MyPlugin");
ScreenTextResult result = hud.ShowScreen(player, "status", new ScreenTextLayout(
    new ScreenRect(660, 840, 600, 100),
    new[] { new TextRow(24, new TextSpan("出生保护 ", "#63D9FF"),
        new TextSpan("12秒", "#FFD479", bold: true)) },
    verticalAlignment: VerticalAnchor.Middle), duration: 1.5f);
// Handle a non-Shown result using the product's chosen fallback.
hud.Remove(player, "status");
hud.Clear(player); // this consumer's hints for this player
hud.Dispose();
```

`duration: 0` persists until removal, disconnect, round reset or scope disposal. A repeated key
replaces its rows and expiry, including timed-to-persistent replacement. `Clear()` removes all
hints in that scope. Disabling the adapter clears adapter-owned hints; consumers can reuse their
scopes after it is enabled again.

## Shared notice regions

Use `ShowNotice(player, key, region, rows, height, priority, duration)` for portable
priority-managed alerts. `NoticeRegion.Top`, `Center`, and `Bottom` occupy centered reference-canvas
rectangles `(510,260,900,172)`, `(510,452,900,260)`, and `(510,732,900,218)`. The 0–260
native broadcast band stays outside managed placement; the regions have 20-unit gaps. Within a region,
higher priorities appear first; equal priorities retain their original insertion order on refresh.
Accepted notices stack downward with a 12-unit gap. A notice that cannot fit waits for space.
`NoticeResult.Visible` and `Queued` both mean accepted; `Unavailable`, `Unsupported`, and
`DoesNotFit` leave the existing key and expiry unchanged. `GetNoticeState(player, key)` returns
the current visible/queued state for a scope-owned notice, or `null` after removal or expiry.
Placement is coordinated across scopes for each player independently.

`duration` starts when the key is submitted or refreshed, including while queued. An expired
queued notice never appears later. `Remove`, `Clear`, disconnect, round reset, and scope disposal
release its placement. A successful call to another display API with the same key replaces the
notice; a rejected call preserves it.

For existing configurable HSM markup, `ShowHsmNotice(player, key, region, richText, height,
priority, duration, fontSize, lineHeight)` uses the same queue and placement policy while keeping
HSM's rich-text parser. The caller declares the rendered height; markup can exceed that declaration,
so verify complex templates on a client. This compatibility path requires an HSM-capable backend.

`ShowHsmReserved(player, key, layout, reservation, duration)` displays a legacy `HsmHintLayout`
and reserves its `ScreenRect` in the shared reference canvas under the same key. The reservation
blocks overlapping notices until that visual is replaced, removed, expired, or its scope is cleared.
Use it for legacy panels whose screen footprint must coexist with region notices. The reservation
is a placement declaration, not clipping of HSM rich text.

For a visual owned outside the adapter, `ReserveScreen(player, key, reservation, duration)`
declares only its footprint. Use the same duration as the external visual and call `Remove` if it
ends early. For native broadcasts that queue on the client, send time may precede visible time;
the fixed native band above protects managed notices independently of that uncertain lifetime.
Reservations protect only the caller-declared rectangle and deadline. Refreshing the same key
renews the deadline; the reservation also clears on player
disconnect, round reset, or scope disposal. A successful adapter display call using that key
replaces the reservation.

For existing HSM layouts, `ShowHsm(player, key, new HsmHintLayout(richText, x, y, fontSize,
anchor, alignment, syncSpeed, fastUpdate, lineHeight, hide, forceUpdate,
forceMembershipUpdate, fastMembershipUpdate), duration)` keeps HSM coordinates,
markup, alignment and parser behavior. `ShowHsmDynamic` takes `HsmDynamicLayout` with target X/Y
for HSM overlap positioning. `ShowHsmAutoText` accepts a `Func<string>` callback that HSM polls
on its own update schedule; the callback must tolerate that scheduling and HSM owns its errors;
remove the key or dispose the scope to release it. These compatibility calls return `false` while
HSM is unavailable. Their rich text, keybind tokens and geometry are HSM-specific and will need
explicit redesign for another renderer. `Hints.MeasureHsmWidth(richText, fontSize)` returns a
nullable HSM measurement for callers that need their existing measured placement; a missing
measurement is not zero width. `Hints.Acquire(owner, groupName)` accepts an optional legacy group
prefix and appends a unique scope suffix so each consumer retains independent ownership.
`forceUpdate` and `forceMembershipUpdate` decide whether to request an extra HSM refresh for
in-place changes and membership changes. If enabled, `fastUpdate` and `fastMembershipUpdate`
choose HSM's fast or normal schedule. All four default to `true`; HSM still schedules its native
updates when the extra force request is disabled.
The `ReferenceHub` overloads of `Remove` and `Clear` support callers that no longer have a LabAPI
player wrapper. Use `ForgetDisconnected(hub)` after a disconnect if HSM may already have destroyed
the display; the adapter also forgets its entries on LabAPI's player-left event.

## Portable screen text

Use `ShowScreen` for new consumers. Its rectangle has a top-left origin on a 1920 by 1080
reference canvas. Font sizes and row gaps use reference-canvas units. Horizontal alignment is
requested relative to the rectangle; vertical alignment anchors the entire block. Physical pixels and
aspect-ratio behavior depend on the client UI scaling; the HSM baseline is verified at 1920x1080.
Do not put HSM caret offsets or rich-text tags into this API.

`Hints.ScreenFeatures` reports currently available renderer features. HSM supports centered
rows, wrapping, overflow rejection and Top/Middle/Bottom vertical alignment. Left/right
alignment, clipping, ellipsis and full-canvas positioning are not supported. `Reject` allows
explicit rows/LF but rejects a row exceeding its width budget. `Wrap` splits long rows at text
elements; vertical overflow is rejected. No policy silently moves the requested block inward.

| Result | Meaning |
| --- | --- |
| `Shown` | Accepted by the renderer; this is not a client delivery acknowledgement. |
| `Unavailable` | Adapter/renderer is disabled, unavailable, or the player is destroyed. |
| `Unsupported` | The selected alignment or overflow policy is unavailable. |
| `OutOfBounds` | The rectangle exceeds the reference canvas or its placement exceeds the renderer's supported band. |
| `DoesNotFit` | Content exceeds the renderer's width/height budget or rendered-row limit. |

A non-`Shown` result leaves existing content **and its expiry** unchanged. Invalid geometry and
layout arguments throw; plain text that HSM cannot safely represent returns `Unsupported` without
changing content. Unexpected renderer failures throw and clean up the affected key. `ShowScreen`, `Show`
and `ShowRaw` share the same key namespace and lifecycle, so switching APIs replaces the entry.

HSM uses a conservative text advance budget, **not measured glyph bounds**. The rectangle is
layout intent, not a clipping region or a promise of exact containment. Line breaks and font
metrics can differ between renderers. In HSM, a row's budget is the sum of `fontSize * 1.35`
per UTF-16 unit; this is deliberately independent of the coordinate conversion. The row center
maps to `2 * (centerX - 960)` HSM caret units. That caret must be at least -800 and its sum
with the row budget must be at most 1100. `ShowScreen` checks this after wrapping. Use its result
for content-dependent placement decisions instead of duplicating those backend rules in callers.

Northwood's published [DisplayKit](https://github.com/northwood-studios/LabAPI/wiki/DisplayKit)
and [style reference](https://github.com/northwood-studios/LabAPI/wiki/DisplayKit-StyleProperties)
provide absolute positioning, box sizing, wrapping and clipping. The internal `ITextBackend` /
`ITextHandle` boundary keeps preparation, rendering and removal behind the shared scope contract.
A native backend can map rectangles to absolute elements on per-player canvases while retaining
consumer ownership, keys and expiry. Native span styling and scaling must be checked against
the supported game version; passing HSM markup through is not the contract. No DisplayKit backend
is included while the installed game references lack that API.

## Existing HSM layout API

Coordinates use HSM's 1080-high canvas. X is its center-relative caret offset, not screen pixels.
Structured coordinates are floored to whole units for culture-independent numeric tags.
Rows have explicit font sizes and gaps, and each rendered row is a separate HSM hint. Top, Middle
and Bottom anchor the whole structured block. Out-of-canvas blocks are rejected before mutation.

Structured spans accept plain text, hex colors, bold and Preserve/Upper/Lower casing. LF starts
a new row. Markup, braces, backslashes and other control characters are rejected at display time
because HSM rewrites them before rendering. Case conversion is invariant; preserve mode explicitly styles
lowercase and uppercase runs. Each span closes its styles.

Rows split at text-element boundaries using a conservative advance budget, not HSM's font-width
estimate. `maxWidth` is 130–1900 units (default 900), and X is clamped to the measured caret band.
This favors safety over tightly fitted typography and may break Latin words. Font sizes are
6–96; layouts have at most 32 rendered rows. Novel fonts/glyphs and edge placement still require
real-client verification; this is not a general TextMeshPro layout engine.

`ShowRaw` retains caller-owned HSM rich text and coordinates. It shares ownership and expiry,
but makes no casing, wrapping, spacing or markup-safety guarantee. Existing providers can remain
in place while individual features migrate.
