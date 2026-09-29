# IslePilot primary / inbound background

Implemented on codex/islepilot-primary-inbound-fallback, based on published 2.4.4.
User approved release 2.4.5 on codex/release-2.4.5 with button labels MỞ MAP / MỞ MAP PRO.
Backend and Agent distribution remain unchanged.

## Behavior

- Normal launcher uses saved IslePilot login again. Missing login opens the login dialog; Free and Pro may explicitly choose inbound instead. Cancel still cancels launch.
- Inbound accumulator stays enabled in the background independently of provider priority.
- Healthy IslePilot four-stat data wins as one provider snapshot, preserving website species/growth/Prime/nutrition. This does not field-mix partially populated healthy provider snapshots with potentially different inbound identities.
- Stale/reconnecting/offline/unsupported/auth-expired or stat-empty IslePilot switches to available inbound four-stat data. Recovery returns to provider automatically when the running provider session publishes valid data. Expired credentials require re-login; no auth bypass.
- Prime remains provider-owned. Same-identity retained Prime is marked stale during outage; unsupported/offline identity or explicit endpoint mismatch cannot carry old Prime/species forward. Unknown endpoint association remains an existing limitation, not new structural proof.
- Prime omission in a partial /me response preserves the quest list; explicit [] clears it. Respawn and identity changes clear previous-dino quests. Separate provider stat/Prime timestamps prevent unrelated map/GPS/Prime updates renewing stats freshness.
- UI retains stale quest rows with reconnect label and suppresses completion toasts from stale snapshots. Stats fallback never synthesizes Prime or uses known-invalid inbound growth.
- Other provider lanes and Pro tracking are unchanged; separate server buttons remain hidden as in 2.4.4.

## Verification

- Full solution: 481 core + 186 UI + 37 ProClient = 704 tests pass.
- Prime compiled-window test additionally executed in its own WPF process (ISLE_PRIME_UI_CHECK=1), passed. Default suite entry returns early to avoid multiple Application instances in one test process.
- Stream integration: provider exception leaves inbound active and retains Prime; merger tests cover outage, expiry, recovery, unsupported server, explicit wrong endpoint, silence and identity reset.
- Release build to artifacts/islepilot-fallback-final succeeds with zero warnings/errors.
- Preview PID 88868 opened through normal basic-map button and reached ISLEPILOT CONNECTING without a repeated login modal. No game process present at runtime inspection; no claim of live server Prime/failover acceptance.

## Remaining validation

Run with game online on an IslePilot-supported server to compare live quests, stats, loss/recovery and species. Credential expiry requires user login. Generic provider faults mark fallback but do not implement a new provider session factory/restart. User approved publication despite this live-validation limitation; release notes disclose it.
