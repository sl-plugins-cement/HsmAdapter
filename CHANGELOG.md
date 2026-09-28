# Changelog

## 1.2.0

- Add explicit HSM compatibility layouts for fixed and dynamic hints, including alignment,
  line height, sync speed, optional AutoText callbacks, and an optional width measurement.
- Clear owned hints on player departure and preserve ownership across callback and same-key refresh.
- Let portable text reject HSM-unsafe content through its result without changing the existing key.

## 1.1.0

- Add portable screen rectangles, explicit alignment/overflow intent, renderer capabilities and
  placement results. Rejected requests preserve existing content and expiry.
- Separate scope lifetime from renderer handles while preserving the existing HSM APIs and
  assembly identity for consumers built against 1.0.

## 1.0.0

- Add scoped hint ownership, explicit case spans, independently positioned rows, conservative
  wrapping and a caller-owned raw path over the existing HSM backend.
- Keep refresh and expiry on the game thread, update anchors on replacement, and remove only
  hints owned by the disposing scope.
- Renew unchanged content without a composed HSM resend, and emit whole-unit structured
  coordinates to avoid locale-dependent decimal separators.
