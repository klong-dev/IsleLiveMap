# Tracking: published 2.4.0 versus acceptance build

## Confirmed release mismatch

Published tag: `a74e3130ea666c3bedb95ce168e0107075865aae`.
Acceptance binary: `artifacts/tracking-lab-host-history` (see its live session binary manifest).
The release tag omitted working-tree tracking changes used in acceptance.

- Release merger rejects locations older than two seconds, regardless of continued actor presence.
- Release IPC model does not consume `HasVerifiedPosition`.
- Acceptance Host supports bounded stale retention and session-scoped marker history.
- Release provider snapshots replace marker history; acceptance Host preserves history within the same session/endpoint.
- PRO, GACHA, ORIGIN and SDVN all use `OpenOverlaySessionAsync` and `TakeProPlayerSourceAsync`. Special server buttons do not inherently bypass the Agent.
- Production Agent 0.3.80 download/signature verification by release ProClient was recorded in `artifacts/pro-080-production-check/verification.json`. This does not prove every user's running process has updated.

## Controlled merger replay

Input: frozen `live-080-reentry-20260924/host-ipc-compare.jsonl`.
SHA-256: `D997F88D3E5D2B8ECFB7D321EFD04B7C21C84B334A662735A63DA902074BFDC9`.
11,561 frames; 396,580 entity observations. Replay uses actual release source and actual acceptance DLL, not an imitation of the old condition.

| Host | Marker-frame observations | Stale display | Unique identities |
| --- | ---: | ---: | ---: |
| Published 2.4.0 | 66,074 | 0 | 423 |
| Acceptance binary | 150,608 | 84,534 | 423 |
| Final relaxed working tree | 150,608 | 84,534 | 423 |

An intermediate replay reported 250,322 observations. That version allowed an overly broad history fallback; after requiring matching coordinates, fresh presence and deduplication, the final replay is 150,608. Do not use the intermediate number as the final improvement. The 15-second initial admission rule is unit-tested but adds no observations over acceptance on this particular capture. Final result: `artifacts/publish-tracking-comparison/relaxed-host-rebuilt.json`.

No replayed marker coordinate differs from the corresponding IPC entity coordinate. This is not proof that the Agent coordinate matches the game. These are repeated entity-frame observations, not additional players or an independent recall measurement. The replay isolates the merger and does not prove live renderer behavior, decoder correctness or all-session lifecycle correctness.

## Mitigation

Retain the acceptance Host changes. Add a 15-second initial position admission window for identified entities with presence at most six seconds old. Display locations older than two seconds as stale; do not refresh location timestamps. Keep identity, finite-coordinate, species/provisional and session checks. Existing longer retention still requires verified position or previously admitted matching position plus bounded presence. Do not extend Agent TTL or bypass entitlement.

Internal preview only: `artifacts/tracking-relaxed-preview/IsleLiveMap.exe`.
The initial diagnostic/fix request did not authorize publication. After checking the preview, the owner explicitly approved publishing 2.4.1 on 2026-09-24. A Host update is required to deliver these changes; publishing Agent alone cannot change the 2.4.0 merger.

## Release 2.4.1 verification

- Release worktree starts at published tag 2.4.0; team capacity, modal layout and SDVN commits remain in ancestry.
- Changed runtime sources match the preview working tree byte-for-byte, except the application version bump.
- Clean release-worktree test: Host 392, ProClient 36, App 171 passed (599 total).
- Production Agent stays at 0.3.80; no new Agent or backend deployment is part of this Host release.
- Owner approved the preview; a new complete autonomous three-session live acceptance was not performed for this hotfix. Frozen IPC merger replay is not independent game ground truth.
