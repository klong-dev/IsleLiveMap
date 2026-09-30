# Server login reset fix

## Real-profile follow-up (2026-09-30)

User explicitly authorized deleting all real app server sessions. Reproduced failure: absent SDVN parent folder raised DirectoryNotFoundException from File.Delete; shared WebView2 retained cookies after ClearBrowsingDataAsync, verified by our IOException. Missing credential parent now counts as already clean; locked/permission failures still propagate as per-target errors. After clearing DOM storage/cookies, CookieManager.DeleteAllCookies is explicitly applied with up to four bounded asynchronous verification attempts. No exception detail containing credentials is shown (type/HRESULT only).

Tested actual Settings button in fixed preview PID 50032: success modal and inline status observed; all IslePilot/voice/Gacha/SDVN credential files absent. Cookie verification returned empty for existing app profiles. Reopened ORIGIN normally; observed SourceTitleLabel KẾT NỐI ORIGIN X5 and LoginStatusLabel 'Chưa thấy phiên đăng nhập. Hoàn tất đăng nhập rồi bấm KIỂM TRA PHIÊN.' No silent overlay launch. ORIGIN login left open for user. Other non-existing tenant profiles counted clean rather than created. Pro files untouched, no server-side revoke, no Steam/game logout. Deleted local credentials are not backed up; logging in again restores access.

Real WebView2 isolated regression now includes session ORIGIN, Steam SSO and persistent IslePilot cookies and passed. Preview path artifacts/login-reset-fixed/IsleLiveMap.exe, build zero warnings/errors; not published. Game ORIGIN fusion live acceptance remains separate from this logout task.

Root causes: Settings deleted only islepilot-overlay.credential; ORIGIN reuses app WebView2 cookies. SetStatus wrote to the Home status reference (null on Settings), so completion was invisible.

Implemented async reset for app-owned shared WebView2, GachaWebView2 and each SDVN tenant profile, plus IslePilot/voice/Gacha/SDVN credential files. Cookies and DOM storage cleared through awaited WebView2 Profile API, cookies verified empty after operation. No recursive profile deletion, no Chrome/Edge/Steam game profile access. Shared browser Steam SSO is cleared; Pro license/token files and entitlement are not changed.

Settings now has persistent inline result + success/partial-failure modal. Target failures report names but never credential values. Launch and reset are mutually gated while clearing. Navigation back to Settings preserves the result.

Validation: 728 full-solution tests pass (498 core, 193 App, 37 ProClient); isolated real WebView2 test separately executed with ISLE_LOGOUT_BROWSER_CHECK=1 and passed. It creates a fixture ORIGIN cookie, clears profile, reopens controller, confirms OriginSessionCookieReader returns null, and tests idempotent reset. Real user credentials were not cleared by the test. Temp fixture-only browser profile is left under temp for WebView2 cleanup; no production profile used.

Build artifacts/origin-fusion-login-reset succeeds with zero warnings/errors. Preview PID 48896 opened to Settings; runtime UI verifies ClearServerLoginButton and LoginResetStatus. No publish. Manual acceptance: click clear, read completion, then choose ORIGIN; no cookie should remain for silent launch. This clears local app sessions, not a server-side revoke or another already-running app instance's in-memory session.
