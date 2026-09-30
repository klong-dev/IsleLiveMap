# ORIGIN + inbound field fusion (internal preview)

Branch codex/origin-inbound-stats-fusion, based on published 2.4.5. No release/deploy performed.

## Launcher

Restores GACHA, ORIGIN 5X, SDVN and “Hoặc các server được hỗ trợ riêng:”. DinoRP remains absent. Saved credentials/handlers and normal update/Npcap/Pro gates remain. Free/Pro main buttons stay MỞ MAP / MỞ MAP PRO.

## Health cadence and commands

OriginStatsSession serializes health discovery and active polling with at least 20 seconds between starts. OriginStatsClient additionally gates new health command submission across Main/Voice in the same client, including direct command calls. Pending commands reuse their id; Retry-After extends cooldown; no parallel server probing. Prime remains a separate command lane. Cooldown is not coordinated with an independently open website or another app process.

## Field state

OriginInboundStatsFusion is owned by each ORIGIN overlay session. First API baseline requires all four finite current/max pairs. Afterwards only supplied fields replace state. Inbound can provide current before API baseline; API fills missing maxima. Request-start timestamps conservatively order API responses against inbound; HTTP completion time never wins by itself. Equal timestamps favor inbound. Zero current is a valid update. Missing fields are not zeros.

Field timestamps/source names appear in render diagnostics. UI/team use merged ExactVitals so progress bars can combine current from inbound and retained max from API. No stored packet timestamp is renewed. Prime/growth/species come only from ORIGIN metadata, not inferred from incoming numeric fields or nearby Pro markers. IslePilot fallback logic remains untouched for standard map.

## Lifecycle

Clear state on observed inbound actor/flow/endpoint changes, explicit provider no-dino/reset, provider session/server generation or species change/growth rollback. API response started before an observed boundary cannot repopulate old fields. Request for a new baseline must start after the boundary. Repeated Prime/snapshot refresh does not reapply stats. Network errors retain baseline; authentication expiry is shown and does not imply death. No cross-process disk fusion cache.

Limits: API does not expose a proven stable actor id or a game endpoint mapping. First association is scoped to the user-selected ORIGIN session and currently observed inbound flow, not cryptographic/structural proof that both refer to the same dino. Unobserved death/respawn with reused identity cannot be guaranteed; cannot identify a different physical server solely from display name. Live ORIGIN API/game validation is still required.

## Verification

Tests cover delayed API vs newer inbound, full-baseline requirement, independent partial updates, zero, actor/flow/server boundaries, Prime-only republish and API fault retention. Session integration confirms API max + inbound current reach the merger/UI snapshot with Prime preserved. Health tests use real 20-second cadence; HTTP fake verifies account cooldown and Retry-After across both server lanes. Compiled UI rendered/reviewed at 820/960/1200 widths and Pro. Preview build has zero warnings/errors.

Latest full solution: 721 tests pass (498 core, 186 App, 37 ProClient). Separate compiled Home capture test passes. Runtime UI Automation confirms three server buttons, original label, MỞ MAP, and no DinoRP button.

Artifact: artifacts/origin-fusion-preview-v2/IsleLiveMap.exe, running PID 40372 at handoff. Game process was absent at validation; no live ORIGIN requests or actual pause-growth acceptance claimed.

Binary SHA256:
- App: 91AD13F2980AA08209696AEB99EE0CFD7B401E46BA341B36E25BBE994C763F45
- LocalTelemetry: 661874DA61CD549F636C4E362D7725E81EA78C0B264C79EF642F69BAA0F46C56
- Origin: 0F14949E504D8AA3854A335633BE862CDFC2A886F258C32DAAF41B1DD36409E3

## Follow-up regression fixes (2026-09-30)

Added regressions cover a full response establishing a new provider scope, first baseline after no-dino, paused growth without max updates, late API max replacing only a protocol default, and field timestamp preservation. New provider scope seeds immediately, API max is authoritative over the protocol default, and inbound current remains authoritative when newer. Health cadence remains 20 seconds with no parallel discovery.
