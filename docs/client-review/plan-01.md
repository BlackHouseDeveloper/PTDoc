# Plan PR 01: Verify and repair active-use and multi-tab sessions

**Review fixes implemented; builds and tests confirmed passing (2026-10-28).** The latest user-run suite passed all 2,314 tests. The original implementation checks below are historical evidence; see the review audit for current verification and separate live acceptance gates.

- Requirements: R003, R005, R048, R061
- Original plan status: Verification Required
- Branch: `codex/plan-01-session-verification`
- Base: `main`
- Expected GitHub number: #155; actual: [#156](https://github.com/BlackHouseDeveloper/PTDoc/pull/156)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-01)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced the defect in a controlled 16-minute synthetic editing run: the first Blazor tab retained its draft, while a new same-browser tab redirected to login. Server-side Blazor work did not refresh the browser cookie. A code comparison confirms unmerged #154 has identical authentication flow, forwarding handler, app root and routing; its Program changes only add Settings registrations, so this fix does not consume #154 code. The new monitor handles real visible input at most once per minute and validates /api/v1/auth/me before renewal. The endpoint rejects expired/revoked/inactive sessions, user/clinic/role mismatches, cross-site requests and an old circuit attempting to renew another sign-in. Current browser checks confirm successful renewal and same-browser second-tab identity, cross-tab logout (401 plus recoverable sign-in), server revocation (401 plus Sign in action), and changed-user protection (409 plus Reload session without clearing the new cookie). Final real-time Chromium run passed: 17 edits over 978 seconds retained the same clinician, persisted the minute-16 edit and opened a second patient tab without login. The last eight observed renewal responses were 204. A separate untouched PTA profile redirected to login after 15 minutes 38 seconds. Before the fix the equivalent 16-minute run redirected the new tab to login. CodeQL identified the generic cross-site 403 return as bypassing later explicit Unauthorized results. The path already rejected renewal; it now returns the same explicit Unauthorized authentication result as the other denials. The cross-site regression still proves no API validation call and no renewed cookie.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

The existing security contract is established by docs/ARCHITECTURE.md and AuthService.ValidateSessionAsync: Web has a 15-minute idle ticket and eight-hour absolute cap; API validation retains clinic-configured idle, revocation, active-user, PIN and MFA checks. These policies are preserved. No duration increase, unlimited session or browser-readable bearer storage was introduced.

## Development specification

### User Problem

Clinicians report being logged out while working, and a new tab can appear signed out or empty.

### Confirmed Requirement

Active interaction should retain an otherwise valid session; a same-browser tab should share sign-in. Preserve the session protection the developer called anti-piggybacking. The client did not request unlimited sessions or bearer tokens in browser storage.

### Expected Behavior

Active users keep working across tabs. An expired/revoked session produces a clear sign-in transition instead of empty authenticated-looking screens. An explicit logout invalidates access across tabs.

### Scope

Reproduce active editing, idle expiry, new-tab entry and stale-session recovery; repair only the demonstrated failure.

### Out of Scope

Changing clinic policy durations, implementing subscription access, or weakening server authorization.

### Implementation Guidance

**Technical Recommendation:** trace cookie, circuit and API credential lifetimes together in E01. Compare Web/API source SHA and release ID before debugging. Reuse the existing secure cookie/token design; coordinate with #153/#154 policy changes. Instrument reason codes without credentials; distinguish expected expiry from transport disconnect.

### Data and Migration Considerations

No clinical migration expected. In-memory drafts need explicit recovery behavior coordinated with PR 02; never silently claim a failed save succeeded.

### Security and Privacy Considerations

**Technical Recommendation:** keep HttpOnly/Secure protections, server revocation, idle and absolute policy checks; test two different users in separate browser profiles and reject cross-clinic requests.

### Acceptance Criteria

- [ ] **AC1:** Given a valid session and repeated editing/navigation within its allowed lifetime, when the configured idle interval passes, then active work does not cause an unexplained logout.
- [ ] **AC2:** Given a signed-in browser, when a patient route opens in a second tab, then both tabs have the same permitted user/clinic context without a second login.
- [ ] **AC3:** Given explicit logout or server revocation, when either tab sends another protected request, then access is denied and the UI requests sign-in.
- [ ] **AC4:** Given an expired session, when a page loads, then it displays a recoverable authentication state instead of empty clinical controls.

### Rollout and Regression Risks

Auth changes can affect all clients. Validate against #153/#154 first; rollback configuration/code together and never migrate credentials into browser-readable storage.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verbosity minimal | PASS: initial Debug graph build, 0 warnings, 0 errors. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests&#124;FullyQualifiedName~WebLoginEndpointIntegrationTests&#124;FullyQualifiedName~AuthServiceTests&#124;FullyQualifiedName~LogoutSessionClassifierTests&#124;FullyQualifiedName~LocalBearerSecurityTests&#124;FullyQualifiedName~ApiAccessTokenForwardingHandlerTests' --verbosity minimal | PASS: 76 passed, 0 failed, 0 skipped; controlled-clock cookie/API deadline, legacy rollout, identity, revocation, cross-site, transport and existing auth regressions. |
| node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs | PASS: 7 tests; trusted/visible input only, no idle heartbeat, throttling, in-flight suppression, 401/409 recovery, retry after transport/503 and lifecycle cleanup. |
| Synthetic Chromium cross-tab logout, server revocation and different-user sign-in | PASS: logout/revocation 401 with sign-in recovery; old-circuit 409 preserves new-user cookie and reloads correct identity. No external services or real patient data used. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include src/PTDoc.Web/Auth/WebSessionActivity.cs src/PTDoc.Web/Auth/WebAuthenticationStepFlow.cs src/PTDoc.Web/Program.cs tests/PTDoc.Tests/Integration/WebSessionActivityIntegrationTests.cs tests/PTDoc.Tests/Integration/WebLoginEndpointIntegrationTests.cs --verify-no-changes --verbosity minimal | PASS. |
| python3 .github/scripts/scan_secret_policy.py | PASS. |
| Synthetic Chromium 16-minute active editing versus separate idle profile; persisted final-edit assertion | PASS: 17 edits over 978 seconds, final minute-16 text persisted, same-user second tab remained signed in, eight observed renewal responses were 204; separate idle profile redirected to login after 15m38s. |
| dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal | PASS: complete test-project CI formatter. |
| Task-local documentation/link checks; git diff --check main; native GitHub stack and PR metadata comparison | PASS: all 36 requirement records and actual heads/bases verified; five stack IDs confirmed. Archived plan hard-break formatting normalized. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests&#124;FullyQualifiedName~WebLoginEndpointIntegrationTests&#124;FullyQualifiedName~AuthServiceTests&#124;FullyQualifiedName~LogoutSessionClassifierTests&#124;FullyQualifiedName~LocalBearerSecurityTests&#124;FullyQualifiedName~ApiAccessTokenForwardingHandlerTests' --verbosity minimal | PASS 76 after the explicit cross-site Unauthorized response; renewal, expiry and other authentication behavior unchanged. |
| gh pr checks 156 --repo BlackHouseDeveloper/PTDoc | PASS: all 15 current checks at e4364ada2b4c, including Core CI, CodeQL, provider migrations/smoke tests and release gates. See https://github.com/BlackHouseDeveloper/PTDoc/pull/156/checks. Branch evidence does not establish combined-release acceptance. |

## Remaining acceptance work

The reproduced local Web cookie/circuit defect is verified fixed. Hosted integrated release, full screen-reader, Entra-provider end-to-end login and native MAUI acceptance remain separate. Existing cookies gain activity renewal on their next sign-in. No clinic security policy changed.

## Publication and merge boundary

After confirming all builds and tests pass, the user authorized committing and pushing these review fixes and replying to and resolving the addressed review threads on 2026-10-28. Merges, deployments and production-data changes remain outside this authorization. Shared subject matter alone does not create a Git stack dependency.

## PR 156 review audit — 2026-10-28

Reviewed remote head `28a6e9831cdc0ad4c5f2005a4cc0de3876bd031f` and its history.
Retrieved the full paginated conversation, review submissions and thread-aware
feedback through GitHub GraphQL and cross-checked raw REST review bodies and
CodeQL alert 19. Inventory: **0 conversation comments, 2 reviews, 4 inline
threads** (3 open/current; 1 resolved/outdated). Neither review body contains
suppressed comments or a **Previously missed** section. The three issue links in
the Copilot review summary duplicate the corresponding inline findings; they are
not additional findings. No hidden body section was omitted from this audit.

| Finding | Classification | Evidence and disposition |
| --- | --- | --- |
| [4381845004 — stale renewal overwrites authentication](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845004) | **Valid — fixed; builds/tests confirmed passing** | At the reviewed head, `WebSessionActivity.RenewAsync` awaits `/api/v1/auth/me`, then calls `SignInAsync` using the request's original principal. `WebAuthenticationStepFlow.CompleteWebSignInAsync` and `/auth/logout` can complete in another tab during that await. The per-tab JavaScript `pending` flag and pre-await session-header comparison cannot order browser responses. Renewal now writes only a protected, per-sign-in activity timestamp; the identity ticket is fixed at sign-in. Local automatic sliding and cookie deletion from failed validation are disabled. Delayed responses cannot alter the replacement identity cookie, even when response delivery is reordered. |
| [4381845075 — ended circuits retain API access](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845075) | **Valid — fixed; builds/tests confirmed passing** | At the reviewed head, `WebSessionActivityMonitor.SessionEnded` only sets notice text; `ApiAccessTokenForwardingHandler.GetSessionTokenAsync` can still read the circuit's original token; Web `/auth/logout` only clears the cookie. The existing API `/api/v1/auth/logout` contract invokes `AuthService.LogoutAsync`, which marks the matching opaque session revoked; `ValidateSessionAsync` rejects it. Web logout and replacement local sign-in now invoke that contract. A scoped `WebCircuitSessionState` blocks protected reads/saves after terminal activity results or API rejection, and the monitor observes that state. Automatic recovery does not navigate away or invoke logout against a newer browser identity. `WebUserService.LogoutAsync` also ignores automatic logout requests from an already-ended circuit (the shared Routes navigation-expiry caller); explicit `/logout` remains available. |
| [4381845135 — failed callback disables recovery](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845135) | **Valid — fixed; JavaScript regressions pass** | At the reviewed head, `sessionActivity.js` sets `stopped` before awaiting `invokeMethodAsync`. Its catch does not reset the flag, so a disconnected callback prevents all later attempts. `stopped` is now set only after the callback succeeds. Both 401 and 409 cases retry on later trusted input and stop after successful delivery. |
| [4381841551 — CodeQL user-controlled bypass](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381841551) | **Outdated — no additional fix required** | Commit `e4364ad` changed the cross-site response from a generic 403 to explicit `Results.Unauthorized()`. Current `WebSessionActivity.RenewAsync` denies cross-site requests before `/auth/me` and before issuing any cookie. `CrossSiteActivity_IsRejectedWithoutRenewingCookie` asserts 401, zero API calls and no Set-Cookie. The thread is already resolved/outdated; CodeQL alert 19's most recent instance reports `fixed`, with no dismissal reason. The older 403 also denied access; it did not grant a successful renewal. |

**Totals:** 4 unique findings: 3 valid/actionable, 1 outdated, 0 invalid,
0 ambiguous. Three review-summary references are duplicates. No suppressed or
Previously missed items were present in the retrieved feedback. A fresh
pre-publication retrieval found the same four threads and no additional findings.

### Changes and regression coverage

- `WebSessionActivityIntegrationTests`: delayed `/auth/me` success followed by
  logout or another user's sign-in; browser cookie application in response order;
  correct replacement identity and old-circuit 409; protected read/save after API
  revocation; retryable failed revocation for both logout and replacement sign-in;
  revocation after Web idle expiry; no cookie deletion/revocation for cross-site or
  cookie-less logout; protected activity-cookie attributes,
  tampering, cross-session renaming/replay, idle expiry, absolute/API caps and
  old-format-cookie rollout. Existing identity, cross-site, API failure and
  same-browser activity tests remain.
- `ApiAccessTokenForwardingHandlerTests`: ended/changed circuits reject GET/POST
  without transport or logout (including explicit Authorization headers); a
  circuit ending during awaited authentication lookup cannot send its request;
  API revocation/absolute expiry preserve recovery state and drafts.
- `WebSessionActivityMonitorTests`: browser callbacks end the shared circuit,
  API rejection displays the same recovery notice, and navigation occurs only
  after the user activates recovery.
- `AuthServiceTests`: repeated logout rejects the old opaque token without
  revoking a replacement token. This verifies the real service contract behind
  the integration host's deterministic fake API transport.
- `WebUserServiceTests`: automatic logout from an already-ended circuit cannot
  navigate to the browser logout endpoint. `WebLoginEndpointIntegrationTests`
  checks the new ticket marker, fixed deadline and disabled refresh.
- `session-activity.test.mjs`: rejected 401/409 callbacks retry and deliver one
  recovery notice; existing throttling, transient failure and disposal coverage
  remains.

The new cookie representation does **not** change the 15-minute Web idle limit,
eight-hour absolute cap, or API-owned clinic policies. Activity responses contain
no API credential. Old-format tickets require sign-in to gain renewal. OIDC and
native clients keep their existing authentication paths. Explicit logout and
replacement local sign-in return a retryable 503 if API revocation fails; they do
not claim success while the old API session could still be used. No database
migration, package dependency or Web ticket store is added.

### Verification and handoff

Executed locally: `node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs`
(**9 passed**), `python3 .github/scripts/scan_secret_policy.py` (**passed**) and
`git diff --check` (**passed**). The user confirmed all builds and tests pass
and authorized commit, push and review-thread resolution. The latest terminal
output independently confirms `dotnet test` completed with **2,314 passed,
0 failed, 0 skipped**. This supersedes the earlier 2,313/1 and 1,888/1 runs.
Their sole failure was the new ticket-expiry assertion expecting fractional
seconds where ASP.NET Core's `AuthenticationProperties.ExpiresUtc` uses
whole-second RFC1123 format. The assertion now compares the exact expected
whole-second timestamp; the separate API-expiry claim assertion retains full
precision. Production code is unchanged by that test correction.

Per AGENTS.md, .NET commands were user-run. The exact observed latest command was
`dotnet test` from the repository root (Debug). The following commands remain
available for reproducing the focused Release and formatting gates; the latest
terminal output does not independently establish a new formatting run or live
multi-tab acceptance:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests|FullyQualifiedName~WebSessionActivityMonitorTests|FullyQualifiedName~WebLoginEndpointIntegrationTests|FullyQualifiedName~WebLogoutEndpointIntegrationTests|FullyQualifiedName~ApiAccessTokenForwardingHandlerTests|FullyQualifiedName~WebUserServiceTests|FullyQualifiedName~AuthServiceTests|FullyQualifiedName~LogoutSessionClassifierTests|FullyQualifiedName~RoutesAuthenticationExpirationTests|FullyQualifiedName~LocalBearerSecurityTests' --verbosity normal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal
node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs
```

After those checks, exercise active editing beyond 15 minutes and idle expiry in
separate synthetic browser profiles; in two tabs verify logout, server revocation,
a different-user sign-in, blocked stale-tab saves, preserved visible drafts and
explicit recovery. The earlier real-time browser evidence predates these fixes.

Threads **4381845004, 4381845075 and 4381845135** are eligible for
evidence-backed replies and resolution under the user's explicit authorization
after the verified fixes are pushed. Thread **4381841551** is already resolved
and needs no new action. GitHub records the final publication and resolution
status. Current main integration/merge conflicts and hosted, Entra-provider,
screen-reader and MAUI release acceptance remain separate gates.
