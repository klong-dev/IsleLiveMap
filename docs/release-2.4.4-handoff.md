# 2.4.4 client release scope

## Included other-session client commits

- cdf0d32 / f7d5e52: room capacity request and modal layout.
- 064ba13: IslePilot login persistence and stats/GPS separation.
- 17b564f: private last-known and manually confirmed-death markers, opt-in personal sync.
- 4ffc92e: team publish independent of UI, stale/empty update protection and reconnect policy.

The desktop thread “The Isle” reported relay e439460 deployed and verified on 2026-09-29. This release does not deploy backend or change the Pro Agent distribution. Existing 2.4.3 Host tracking/ProClient parity was checked against the release tag.

## Stats and launcher

- Four inbound stats enabled by default; explicit replacement env=0 permits diagnostic rollback.
- Raw packet regressions cover current-tail misclassification, UDP side traffic not resetting game stats, and MaxHunger publication for 1482-bit layout.
- Separate delta fields retained without fabricated timestamps; cold-start unknowns remain unknown; bounded same-scope max cache.
- Known-wrong inbound growth hidden in overlay/Home; species completion deferred, not inferred from nearby markers.
- All dedicated-server buttons temporarily absent; credentials/handlers preserved. Primary Free/Pro labels explicitly say all-server experimental.
- Normal launcher path opens inbound without IslePilot login; update, Npcap and Pro checks remain.
- Team HP/hunger/water use the same retained current/max as the overlay.

## Validation

Full solution 691 tests: core 469, App 185, ProClient 37. Compiled UI capture test passes, Free widths 820/960/1200 and Pro reviewed. Packaging reruns all tests. User accepted current stats with growth/species deferred. No claim that every server protocol or actor schema is supported.

Release sources are isolated from the dirty research workspace in codex/release-2.4.4. No raw captures, credentials, server clone or diagnostic dumps are packaged/pushed.
