# ORIGIN live findings — 2026-09-30

## Evidence

Read closed historical origin-observe-20260930/origin.jsonl (SHA256 0A5D4FCB2399661EEF5DF6C0B3875535725CB71295644DEDB587F3A381E05604).
1118 request starts, minimum wall-clock interval 19.9988158 seconds (timing jitter). 84 successful Voice results from 01:11:05 to 01:42:25 GMT+7 used stam/maxStam keys; old parser only read stamina/maxStamina. Complete-baseline check therefore rejected every API snapshot and fusion remained inbound-only. This was not a login failure. Numeric stam/maxStam were not in the old diagnostic allowlist, so their exact recorded values are unavailable.

Generic failed responses later repeated on Voice without re-discovery. They cannot be called confirmed no-dino: the retained allowlisted payload only contains success. Separate fusion bug marked retained stats Live hours after last inbound update (~01:36), even at 07:22. At investigation time no game/map processes were running; logs are not a current live test.

## Fixes

- Accept stam/maxStam as aliases while preserving long-name support. Log numeric aliases (no credentials or arbitrary payload values).
- After three failed health results, re-enter sequential discovery with the same 20-second cadence. Failed commands alone do not erase baseline or prove death.
- Track actual API request/accepted inbound presence independently of field retention. After 45 seconds without either source, retain values but mark fusion Stale; ticker/Prime-only refresh cannot renew it.
- UI distinguishes waiting for ORIGIN baseline from combined-source mode and stale state.

## Verification and limitations

Observed-schema regression uses synthetic stamina numeric values explicitly because only key names were captured. It verifies the ORIGIN session now supplies a complete baseline accepted by fusion. Repeated-Voice-failure test runs through timed fallback to Main; stale-state test retains max but rejects fake liveness. Build artifacts/origin-schema-fixed-20260930 succeeds without warnings/errors.

Full server acceptance remains pending: need an online ORIGIN game + authenticated session to demonstrate API-sourced maxima and inbound-sourced currents in the actual rendered frame after the fix. No live success or publication claimed.
