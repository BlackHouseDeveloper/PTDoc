# Plan PR 01: Verify and repair active-use and multi-tab sessions

**Latest assessment (2026-10-30): retained-circuit reconnect defect identified and fixed locally; .NET/browser verification pending.**
The final assessment below develops the earlier summary-only concern into a
concrete identity-transition failure. The nine earlier findings remain fixed at
`81f565c`. Passing verification for that commit does not verify these new changes.

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


## PR 156 follow-up review audit — 2026-10-29

### Scope and complete feedback inventory

Verified clean local HEAD and remote PR head both at
`e645d6f752c814b5aaba03713a2f0afe673ec661`, with base
`ee9a0066d11b135841e7da33643d53ab954a4a76`. Retrieved current thread-aware feedback,
full paginated GraphQL conversation/reviews/threads, raw REST review bodies, and
CodeQL alert 19. There are **0 conversation comments, 6 review submissions,
8 threads, and 11 inline comments including 3 prior author replies**. Four threads
are open/current and four are resolved/outdated. All collapsed review sections
were inspected: **no suppressed findings or Previously missed sections/items are
present**. The summary bodies contain **10 duplicate references** to the seven
Copilot findings (three original references, four new references, and three
resolved-since-last-review references). These are not additional defects. The
three author replies report prior fixes rather than raising new findings.

The latest user request supersedes the earlier publication authorization:
**no commit, push, review reply, resolution, PR-level response or review request
is authorized for this audit**. None has been performed.

### Classification and evidence

| Finding | Classification | Current code, call-site, contract and regression evidence |
| --- | --- | --- |
| [4411837564 — delayed logout clears replacement sign-in](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837564) | **Valid; fixed locally, .NET verification pending** | `Program.cs` awaited `RevokePreviousSessionAsync` and then deleted the shared identity cookie with `SignOutAsync`. Replacement login in `WebAuthenticationStepFlow.CompleteWebSignInAsync` can finish while that await is pending. Local logout now issues a sign-in-specific denial marker through `WebSessionActivity.MarkPreviousLocalSessionEnded`; `ValidateCookieAsync` rejects only that original sign-in. A marker for a legacy ticket uses a token digest, never the bearer token. API logout still revokes first and failed revocation still returns 503. `DelayedLogout_CannotClearReplacementSignIn` applies B's cookie before A's delayed response, proves B remains authenticated and can renew, proves A's cookie fails real cookie authentication, and verifies only A's token was revoked; three cases cover current, previous-version and legacy tickets. Existing delayed-renewal, revocation-failure, idle-expired and cross-site tests remain applicable. |
| [4411837629 — pooled auth state propagates 401 across users](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837629) | **Valid; fixed locally, .NET verification pending** | `Program.cs` attached `ApiAccessTokenForwardingHandler` to the pooled named `ServerAPI` handler. Its injected `WebCircuitSessionState`, authentication provider and user service belonged to the factory handler scope, unlike the already scoped UI client. Named callers include Web development communications diagnostics and MFA recovery-code regeneration in `WebAuthenticationStepFlow`. `WebScopedHttpClientFactory` now wraps factory-owned transports with a fresh authentication handler resolved in the caller's request/circuit scope. Named options are retained, other client names receive no auth wrapper, and disposing a wrapper does not dispose the pooled transport. `NamedClients_IsolateRejectedUsers_WhileSharingTransport` uses production DI/factory registration in two scopes, exercises both HttpContext and circuit identity sources, proves A's 401 blocks only A, proves B's diagnostics/recovery calls carry B's token and succeed, and checks the single shared transport survives A's client/scope disposal. `AuthApiClient_RemainsUsableForRecoveryInEndedScope` checks that the separately named auth client remains usable without inherited bearer state. |
| [4411837680 — previous-version cookie starts incompatible monitor](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837680) | **Valid; fixed locally, .NET verification pending** | The monitor required a local identity and session ID, while `RenewAsync` additionally requires `ptdoc_web_activity_ticket=1`. Commit `4f45f05` introduced that marker after the earlier session-ID format, so old still-valid tickets could trigger 401 on first input. The monitor now uses the same `UsesActivityTicket` predicate as renewal. `Monitor_StartsOnlyForCurrentActivityTickets` covers missing/incorrect/current markers and verifies old tickets do not import the monitor or end the circuit. Existing `PreviousVersionCookie_KeepsNormalExpiryWithoutAutomaticSliding` and `LegacyCookie_DoesNotGainNewActivityRenewalBeforeNextSignIn` cover server rollout behavior. |
| [4411837718 — backward wall clock blocks renewal](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837718) | **Valid; fixed locally, JavaScript verification passed** | `sessionActivity.js` throttled using `Date.now() - lastAttempt`, which can remain negative after a clock correction. It now uses `performance.now()` with `lastAttempt=-Infinity`, preserving immediate first input and the one-minute throttle. The new regression starts at monotonic zero, moves wall time back one hour, checks suppression at 59,999 ms and renewal at 60,000 ms. All 10 JavaScript tests pass, including existing callback retry, throttling, in-flight and lifecycle checks. |
| [4381845004 — stale renewal overwrites identity](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845004) | **Outdated; already fixed, no additional renewal change** | `4f45f05` (rebased form of `e6db945`) removed identity reissuance from `RenewAsync`. Current renewal writes only the protected per-sign-in activity cookie; local automatic sliding is disabled and validation failure emits no identity deletion. `DelayedRenewal_CannotRestoreLogoutOrOverwriteNewSignIn` covers both interleavings and response-cookie application. The new logout finding is a distinct response path, not a duplicate of this fixed renewal path. |
| [4381845075 — ended circuits retain API access](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845075) | **Outdated; original circuit/revocation defect already fixed** | `4f45f05` made the monitor end shared circuit state, made the forwarding handler check it before and after authentication lookup, and added server revocation on logout/replacement. `AuthService.LogoutAsync` revokes by opaque token hash; `ValidateSessionAsync` excludes revoked sessions. `AuthenticationChange_RevokesOldCircuitBeforeAnotherProtectedReadOrSave`, `EndedCircuit_BlocksProtectedOperationsWithoutClearingNewCookieOrDraft`, `CircuitEndedDuringAuthenticationLookup_DoesNotSendProtectedRequest`, and `LogoutAsync_RejectsOldOpaqueSessionWithoutRevokingReplacementSession` cover the contract. The new pooled named-client finding addresses a distinct DI scope boundary; it does not require another change to these guard checks. |
| [4381845135 — rejected callback permanently stops recovery](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845135) | **Outdated; already fixed, no additional callback change** | `4f45f05` moved `stopped=true` after successful `invokeMethodAsync`. Both 401 and 409 callback-rejection tests still pass and prove later trusted input retries and successful delivery stops further calls. |
| [4381841551 — CodeQL bypass](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381841551) | **Outdated; already fixed** | `c90d7c1` (rebased form of `e4364ad`) made cross-site renewal denial explicit with `Results.Unauthorized()`. Current `RenewAsync` rejects it before API validation or cookie issuance. `CrossSiteActivity_IsRejectedWithoutRenewingCookie` asserts 401, zero API calls and no Set-Cookie. Alert 19's latest instance is `fixed`; the thread remains resolved/outdated. No suppression was added. |
| Review overview links (10 references) | **Duplicate** | Each links directly to one of the seven Copilot inline findings above. No distinct body-only finding or additional requested change exists. |

**Totals: 8 unique findings — 4 valid/actionable, 4 outdated, 0 invalid,
0 ambiguous; 10 duplicate summary references.** No suppressed or Previously
missed finding was found. No unresolved product decision is needed for these
fixes. The four new threads remain open pending user verification and explicit
publication authorization.

The scoped-handler approach follows the framework's documented distinction
between application scopes and pooled handler scopes:
[Microsoft IHttpClientFactory guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory#message-handler-scopes-in-ihttpclientfactory).

### Changes, limits and verification

Added `src/PTDoc.Web/Services/WebScopedHttpClientFactory.cs` in the existing Web
composition/services layer and its factory-level tests under
`tests/PTDoc.Tests/Web/`. No Core/Application dependency, API contract, database
migration or package change is required. Logout marker cookies are denial-only;
API revocation remains authoritative even if the browser removes the marker.
Markers are retained through the original session deadline. External-provider
logout keeps its existing flow. Web idle/absolute deadlines and clinic API
policies are unchanged. Updated architecture/security docs describe the behavior.

Checks run for this audit:

- `node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs`: **10 passed**.
- `git diff --check`: **passed**.
- `python3 .github/scripts/scan_secret_policy.py`: **passed**, 9 tracked configuration files.

No .NET build, test or format command was run, per AGENTS.md. The user's previous
passing builds/tests apply to the earlier code, not these new changes. Run from
the repository root:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests|FullyQualifiedName~WebScopedHttpClientFactoryTests|FullyQualifiedName~WebSessionActivityMonitorTests|FullyQualifiedName~WebLoginEndpointIntegrationTests|FullyQualifiedName~WebLogoutEndpointIntegrationTests|FullyQualifiedName~ApiAccessTokenForwardingHandlerTests|FullyQualifiedName~WebUserServiceTests|FullyQualifiedName~AuthServiceTests' --verbosity normal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --verbosity normal
node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs
```

For browser acceptance, repeat the existing synthetic multi-tab active/idle and
explicit recovery scenarios. Include logout in A while B signs in, a rejected API
session alongside a valid independent browser profile, and a previous-version
cookie upgrade. Hosted integrated-release, Entra-provider, screen-reader and
native-platform acceptance remain separate.

After these checks pass and the user authorizes GitHub writes, threads
**4411837564 (`PRRT_kwDOQt-faM6xiDrs`), 4411837629 (`PRRT_kwDOQt-faM6xiDsP`),
4411837680 (`PRRT_kwDOQt-faM6xiDsw`) and 4411837718 (`PRRT_kwDOQt-faM6xiDtE`)**
will be eligible for evidence-backed replies and resolution. The four historical
threads are already resolved and need no additional action.


## PR 156 CodeQL test-probe review audit

### Inventory and scope

Local HEAD and fetched remote PR head are both
`d102f0dd25d76791041c3444f66f16f1cb7a4305`; base is
`ee9a0066d11b135841e7da33643d53ab954a4a76`. The worktree was clean at the start.
Retrieved full paginated GraphQL conversation/review/thread data, cross-checked
REST review bodies and connector thread/timeline results, and inspected CodeQL
alerts 19 and 20 directly. Inventory: **0 conversation comments, 12 reviews,
10 inline threads and 17 inline comments (including 7 earlier author replies)**.
Eight threads are resolved; two are open. GitHub marks two resolved threads as
current rather than outdated, so their disposition below is based on current code,
not the GitHub outdated flag.

All collapsed review sections and complete bodies were inspected. **No suppressed
findings or Previously missed sections/items are present.** The three Copilot
summaries contain **15 duplicate finding references**. The latest Copilot inline
comment duplicates CodeQL alert 20, leaving **9 unique findings: 1 valid/actionable
and 8 outdated/already fixed; 0 invalid and 0 ambiguous**. The seven author replies
record earlier verification and are not new findings.

The current user instruction prohibits commit, push, replies, resolutions,
PR-level responses and new review requests. None was performed in this pass.

### Complete classification

| Comment / finding | Classification | Evidence from current code, tests, contracts and history |
| --- | --- | --- |
| [4412062637 — CodeQL alert 20](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4412062637) | **Valid actionable test-harness/scanner finding; fixed locally, verification pending** | Alert 20 is open and classified `test`; its source is `WebSessionActivityIntegrationTests.cs`'s path condition guarding `AuthenticateAsync`. `BrowserAuthenticationProbe` is private to the test class and registered only by `SessionHost.ConfigureTestServices`; there is no production registration or demonstrated production authorization bypass. The ad-hoc `app.Use` path branch introduced by `d102f0d` has been replaced with `app.Map("/test/browser-authentication", ...)` and a terminal `Run` delegate. Every request entering that branch still calls the real cookie authentication scheme before returning 200 or 401. Other paths use the normal application pipeline. No scanner suppression, production code change or authentication relaxation was added. Existing `DelayedLogout_CannotClearReplacementSignIn` has three ticket-format cases, each asserting B's valid cookie yields 200 and A's signed-out cookie yields 401 through this probe. |
| [4418418883 — explicit test probe route](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4418418883) | **Duplicate of 4412062637** | Names CodeQL 20 and the same path branch, and requests the mapped-route remedy above. One code change handles both threads; no second implementation is required. |
| [4411837564 — delayed logout deletes replacement cookie](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837564) | **Outdated; already fixed, no new change** | `d102f0d` changed local logout in `Program.cs` to revoke A's API token and call `MarkPreviousLocalSessionEnded`, which writes only A's denial marker. `ValidateCookieAsync` rejects that marker. `DelayedLogout_CannotClearReplacementSignIn` applies B's identity cookie before A's delayed response and checks B's identity/renewal, A's rejection, and token-specific revocation across current, previous-version and legacy formats. The probe routing correction preserves those assertions. |
| [4411837629 — pooled authentication state crosses users](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837629) | **Outdated; already fixed, no new change** | `d102f0d` registers scoped `WebScopedHttpClientFactory`; named `ServerAPI` calls from diagnostics and MFA recovery-code regeneration receive auth wrappers from the caller scope. Only transport is shared. `NamedClients_IsolateRejectedUsers_WhileSharingTransport` covers request and circuit principals, A's rejection, B's success, same-scope blocking and transport survival after disposal. `AuthApiClient_RemainsUsableForRecoveryInEndedScope` covers the separate auth client contract. |
| [4411837680 — previous-version monitor startup](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837680) | **Outdated; already fixed, no new change** | `d102f0d` makes `WebSessionActivityMonitor` require `UsesActivityTicket`, matching `RenewAsync`. `Monitor_StartsOnlyForCurrentActivityTickets` covers missing, incorrect and current markers. `PreviousVersionCookie_KeepsNormalExpiryWithoutAutomaticSliding` and the legacy renewal test cover normal old-ticket expiry. |
| [4411837718 — backward clock stops renewal](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837718) | **Outdated; already fixed, no new change** | `d102f0d` uses `performance.now()` and an initially eligible `lastAttempt`. The JavaScript backward-clock regression shifts wall time by an hour and checks the 59,999/60,000 ms throttle boundary. No JavaScript changed in this pass. |
| [4381845004 — stale renewal overwrites identity](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845004) | **Outdated; already fixed, no new change** | `4f45f05` removed identity reissuance from renewal. `RenewAsync` writes only the per-sign-in protected activity cookie; `Program.cs` disables local sliding and rejected cookie validation emits no deletion. `DelayedRenewal_CannotRestoreLogoutOrOverwriteNewSignIn` tests both response-ordering interleavings. |
| [4381845075 — ended circuit retains API access](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845075) | **Outdated; already fixed, no new change** | `4f45f05` connected the monitor to terminal circuit state and checks it before/after token lookup in the forwarding handler. Logout/replacement call the existing API revoke contract. `AuthService.LogoutAsync` revokes the exact opaque token hash; `ValidateSessionAsync` excludes revoked sessions. Existing blocked read/save, termination-during-lookup, and `LogoutAsync_RejectsOldOpaqueSessionWithoutRevokingReplacementSession` tests cover these boundaries; `d102f0d` additionally fixed named-client scope isolation. |
| [4381845135 — callback rejection disables recovery](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845135) | **Outdated; already fixed, no new change** | `4f45f05` sets `stopped=true` only after successful callback delivery. Both 401 and 409 JavaScript callback-retry regressions remain present and were previously verified. |
| [4381841551 — CodeQL alert 19](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381841551) | **Outdated; already fixed, no new change** | `c90d7c1` makes cross-site renewal return explicit `Results.Unauthorized()` before API calls and cookie writes. `CrossSiteActivity_IsRejectedWithoutRenewingCookie` asserts that contract. Alert 19's current most-recent instance is `fixed`. |
| Fifteen review-summary links | **Duplicate references** | Each links to one of the inline findings above. No distinct body-only finding exists. |

### Change and verification handoff

Only the test probe registration, this audit and `docs/CHANGELOG.md` changed.
The existing three delayed-logout cases already exercise the changed probe with
both authenticated and signed-out cookies, so no duplicate test was added for
the route-registration syntax. Their requests to login, logout and activity also
exercise the ordinary pipeline outside the probe branch.

`git diff --check` passed. No .NET build/test/format or CodeQL analysis was run,
consistent with AGENTS.md. Previous user-confirmed passing builds/tests apply to
`d102f0d`, not this new test-route edit. CodeQL alert 20 remains open remotely;
removing the flagged condition is not evidence that a fresh analysis has passed.

Run from the repository root:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal
```

After an explicitly authorized publication, the existing `.github/workflows/codeql.yml`
**Analyze (C#)** job must run on the updated PR and confirm alert 20 is fixed.
It analyzes the non-MAUI build graph including the tests; local .NET success alone
does not close a CodeQL alert. No workflow dispatch or review request was made.

After .NET and fresh CodeQL verification pass, **4412062637
(`PRRT_kwDOQt-faM6ximHf`)** and its duplicate **4418418883
(`PRRT_kwDOQt-faM6xxelU`)** are eligible for evidence-based replies/resolution
with explicit user authorization. The other eight threads are already resolved
and need no new replies. No ambiguous product decision remains; pending work is
verification and publication authorization, not an inferred security-policy change.

## PR 156 post-publication review audit — 2026-10-30

### Inventory and classification

Local HEAD and fetched remote PR head both equal
`81f565cea078106152bd5d9f4751bbd9c1bce124`; PR base remains
`ee9a0066d11b135841e7da33643d53ab954a4a76`. The initial working tree was clean.
Retrieved the complete paginated conversation, reviews and threads using the
review-fetch script, cross-checked the connector timeline and thread metadata,
and read all review bodies, including collapsed sections. Inventory: **0 conversation
comments, 15 reviews, 10 threads, 19 inline comments (9 author replies)**.
All ten threads are resolved; eight have GitHub's outdated flag. Classification
uses current code rather than that flag. No suppressed or **Previously missed**
sections/items were returned. The latest summary contains an unthreaded concern
even though it also says there are no open issues; that concern is included below.

After deduplication: **10 unique findings: 9 outdated/already fixed, 1 ambiguous,
0 verified valid/actionable, 0 invalid**. One inline comment and sixteen review
summary links are duplicate references. Prior author replies are verification
records, not additional findings.

Paths below are repository-relative; test method names identify the assertions
rechecked in this pass. No implementation or regression change is needed for the
already-fixed items.

| Finding | Classification | Current evidence / why no new change |
| --- | --- | --- |
| [4381841551: CodeQL 19](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381841551) | Outdated | `WebSessionActivity.RenewAsync` in `src/PTDoc.Web/Auth/WebSessionActivity.cs` returns explicit Unauthorized for cross-site input before contacting the API or writing cookies. `CrossSiteActivity_IsRejectedWithoutRenewingCookie` in `tests/PTDoc.Tests/Integration/WebSessionActivityIntegrationTests.cs` asserts 401, zero API calls and no Set-Cookie. Introduced by `c90d7c1`; the current alert instance is fixed. |
| [4381845004: stale renewal replaces identity](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845004) | Outdated | `RenewAsync` writes only the sign-in-specific protected activity cookie; local sliding renewal is disabled in `src/PTDoc.Web/Program.cs`, and rejected cookie validation emits no deletion. `DelayedRenewal_CannotRestoreLogoutOrOverwriteNewSignIn` checks both response-ordering cases, unchanged B identity, rejected A, and absence of identity-cookie writes. Fixed by `4f45f05`. |
| [4381845075: ended circuit retains API access](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845075) | Outdated as the concrete original finding | `WebSessionActivityMonitor.SessionEnded` now ends scoped state. `src/PTDoc.Web/Services/ApiAccessTokenForwardingHandler.cs` checks that state before and after token lookup, and API rejection ends monitored state. `RevokePreviousSessionAsync` is called on logout/replacement. `src/PTDoc.Infrastructure/Identity/AuthService.cs` revokes the exact token hash and excludes revoked sessions from validation. `EndedCircuit_BlocksProtectedOperationsWithoutClearingNewCookieOrDraft`, `CircuitEndedDuringAuthenticationLookup_DoesNotSendProtectedRequest`, `AuthenticationChange_RevokesOldCircuitBeforeAnotherProtectedReadOrSave`, and `LogoutAsync_RejectsOldOpaqueSessionWithoutRevokingReplacementSession` cover those contracts. Fixed by `4f45f05`; the new unspecified integration concern is tracked separately below. |
| [4381845135: failed callback disables recovery](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4381845135) | Outdated | `src/PTDoc.Web/wwwroot/js/sessionActivity.js` sets `stopped` only after successful callback delivery. Separate 401 and 409 disconnected-callback regressions in `tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs` verify later trusted input retries. Fixed by `4f45f05`. |
| [4411837564: delayed logout deletes B's cookie](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837564) | Outdated | Local logout calls `MarkPreviousLocalSessionEnded` after confirmed revocation instead of deleting the shared identity cookie. `ValidateCookieAsync` rejects only the marked sign-in. `DelayedLogout_CannotClearReplacementSignIn` delivers B's cookie before A's response and checks B's identity/renewal, A's 401 and token-specific revocation for current, previous-version and legacy formats. Fixed by `d102f0d`; `81f565c` retains these assertions with the mapped test probe. |
| [4411837629: pooled auth state crosses users](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837629) | Outdated | `Program.cs` registers scoped `WebScopedHttpClientFactory`, scoped state, and transient auth wrappers; `CreateClient("ServerAPI")` resolves the wrapper from its caller scope and borrows only pooled transport. The same path supplies injected UI HttpClient, diagnostics and MFA recovery-code calls. `NamedClients_IsolateRejectedUsers_WhileSharingTransport` covers request and circuit principals, A's rejection, B's success, same-scope blocking and disposal isolation. `AuthApiClient_RemainsUsableForRecoveryInEndedScope` checks the recovery client. Fixed by `d102f0d`. |
| [4411837680: previous-version monitor startup](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837680) | Outdated | Monitor startup requires `UsesActivityTicket`, matching renewal eligibility. `Monitor_StartsOnlyForCurrentActivityTickets` covers absent, incorrect and current markers; integration tests `PreviousVersionCookie_KeepsNormalExpiryWithoutAutomaticSliding` and `LegacyCookie_DoesNotGainNewActivityRenewalBeforeNextSignIn` cover old-ticket behavior. Fixed by `d102f0d`. |
| [4411837718: backward clock suppresses renewal](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4411837718) | Outdated | JavaScript uses `performance.now()` and starts with `lastAttempt = -Infinity`. The backward-clock regression shifts wall time back one hour and checks the 59,999/60,000 ms threshold. Fixed by `d102f0d`. |
| [4412062637: CodeQL 20 test probe](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4412062637) | Outdated | `BrowserAuthenticationProbe` now uses `app.Map`/`Run`, still calls real cookie authentication, and is registered only in the nested test host. The three delayed-logout cases check valid B returns 200 and signed-out A returns 401. Fixed by `81f565c`; fresh CodeQL run 41050698530 passed and alert 20 is fixed. |
| [4418418883: explicit test route](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#discussion_r4418418883) | Duplicate | Same probe and requested remedy as CodeQL 20; already handled by `81f565c`. No independent implementation needed. |
| [5711868930: interactive-circuit boundary summary](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#pullrequestreview-5711868930) | Ambiguous | Claims token forwarding and unauthorized/conflict alerts could diverge, but supplies no affected path, event ordering, separate scope, failing assertion or reproduction. Current scope wiring and framework behavior do not establish a defect. Existing tests cover the constituent paths, but are not a composed live-browser test of the actual render roots. See investigation and missing evidence below. |
| Sixteen review-summary links | Duplicate | Each links to a concrete inline item above. The summary-only concern is separately counted, not discarded as a duplicate or hidden behind the resolved-thread count. |

### Circuit-boundary investigation and remaining uncertainty

`src/PTDoc.Web/Components/App.razor:19` mounts the monitor as an InteractiveServer
root beside `AppRoot`; `src/PTDoc.UI/Components/AppRoot.razor:2` also declares
InteractiveServer and wraps routes in cascading authentication. Both use the same
Web server runtime. Separate render roots are not evidence of separate circuits:
the .NET 8 [server bootstrap](https://github.com/dotnet/aspnetcore/blob/v8.0.21/src/Components/Web.JS/src/Boot.Server.Common.ts)
maintains one circuit manager for the server root-component manager, including
root updates. [CircuitFactory](https://github.com/dotnet/aspnetcore/blob/v8.0.21/src/Components/Server/src/Circuits/CircuitFactory.cs)
creates one service scope and passes it to the remote renderer and circuit host.
This is framework-source evidence, not a newly executed browser test.

`Program.cs:93` registers `WebCircuitSessionState` scoped. The scoped factory at
line 113 resolves auth wrappers in that scope, and the injected HttpClient at line
114 uses the same factory. For example, `NoteWorkspaceApiService` takes that
HttpClient and uses it for workspace reads and writes. The monitor subscribes to
the state's Changed event; `SessionEnded` calls End on that injected state.
The forwarding handler blocks terminal state and publishes API 401 termination
through it. Browser activity 409 is translated to SessionChanged by the JavaScript
callback; ordinary API conflict responses are not automatically session changes.
No extra scope is created by these component or client paths. API revocation is
also independently enforced by the server token-validation contract.

`WebSessionActivityMonitorTests` checks both callback reasons, notices, preserved
navigation and state-change notification. `ApiAccessTokenForwardingHandlerTests`
checks blocked reads/writes and API rejection. `WebScopedHttpClientFactoryTests`
uses production host registration but manually creates scopes and test principals.
The JavaScript tests use a mocked callback. These are useful regression boundaries,
but none alone proves the entire browser/SignalR/render-root integration. No
committed Playwright session-boundary test was found in `tests/PTDoc.Web.UiQa/tests`.

To turn the summary into an actionable defect, the missing information is a
concrete failing scenario: route/component, initial identity, whether the trigger
is activity 401/409 or API 401, disconnect/reconnect or sign-in ordering, and the
observed request/alert behavior. A live reproduction should record whether both
roots and the forwarding handler use the same circuit/state instance and whether
the callback completed. Without that evidence, neither a critical bypass nor a
required rendering redesign is established. No product-policy decision is inferred,
and the concern is not marked invalid or resolved.

### Changes, verification and thread disposition

Only this audit and `docs/CHANGELOG.md` changed. No production or test changes were
made: there are no newly verified actionable defects, and adding a speculative
regression for an unspecified scenario would not establish the missing integration
evidence. Existing coverage is identified above. No builds/tests were rerun, per
AGENTS.md; the user-confirmed builds/tests and successful post-push checks apply to
`81f565c`. `git diff --check` and `git diff --cached --check` passed for this pass.

No new build or test is required for these documentation-only changes. To repeat
the relevant regression verification from the repository root:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~WebSessionActivityIntegrationTests|FullyQualifiedName~WebSessionActivityMonitorTests|FullyQualifiedName~WebScopedHttpClientFactoryTests|FullyQualifiedName~ApiAccessTokenForwardingHandlerTests|FullyQualifiedName~AuthServiceTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal
node --test tests/PTDoc.Web.UiQa/tools/session-activity.test.mjs
```

These commands do not replace live-browser evidence for the ambiguous concern.
**No threads become newly eligible for reply or resolution:** all ten are already
resolved and the new summary has no resolvable inline thread. No suppressed item
was individually resolved. No commit, push, comment, resolution, PR-level response
or review request was made during this pass.

## Comprehensive circuit-boundary assessment — 2026-10-30

### Finding and scope

The deeper assessment identifies a **valid, actionable reconnect identity defect**
within the concern raised by review
[5711868930](https://github.com/BlackHouseDeveloper/PTDoc/pull/156#pullrequestreview-5711868930).
This supersedes the preceding ambiguous classification for that concern. It is
source-established, with regression coverage added; it has **not yet been
reproduced or verified by executing the new .NET/browser suites**.
The fresh review still contains ten resolved inline threads and the same summary;
no new suppressed or Previously missed item was returned. The nine concrete older
findings and their duplicate references retain the dispositions above. Overall:
**9 outdated/already fixed, 1 valid/fixed locally pending verification, 0 remaining
ambiguous findings in the retrieved inventory**. The summary's exact intended
scenario is unknown, so this does not claim every possible circuit failure is solved.

The issue is not separate service scopes for sibling InteractiveServer roots.
It is a principal transition inside the **same retained circuit**:

1. A current local sign-in opens a circuit and a component retains an unsaved draft.
2. The circuit disconnects. The browser establishes a new sign-in, possibly for the
   same account, with a different sign-in ID.
3. The .NET 8 [ComponentHub.ConnectCircuit implementation](https://github.com/dotnet/aspnetcore/blob/v8.0.21/src/Components/Server/src/ComponentHub.cs#L172-L192)
   reconnects the existing circuit and calls `SetCircuitUser(Context.User)`.
   Its [CircuitHost](https://github.com/dotnet/aspnetcore/blob/v8.0.21/src/Components/Server/src/Circuits/CircuitHost.cs)
   updates the circuit authentication provider; the existing components survive.
4. Previously, `ApiAccessTokenForwardingHandler` accepted whichever token was now
   available from HttpContext or the circuit provider. `WebCircuitSessionState`
   retained only a terminal reason, not the opening sign-in. The monitor's JavaScript
   still held its original sign-in ID from its first render, and its next activity
   callback could be throttled or disconnected.
5. A component could therefore issue a protected operation with the replacement
   identity before the old activity callback established SessionChanged. This is an
   identity/draft attribution boundary failure, not evidence that API RBAC or
   tenant checks are bypassed. API revocation of A alone does not reject B's valid token.

The same principal-transition boundary also affects UI authorization: `AppRoot`
uses CascadingAuthenticationState and `Routes` uses AuthorizeRouteView with an
anonymous RedirectToLogin branch. Publishing an anonymous reconnect principal
could remove the editor before recovery. A changed-role principal could likewise
replace authorized content. Blocking HTTP alone does not protect that draft.

### Minimal correction and limits

- New Web-only `WebSessionCircuitHandler`, registered through production DI, binds
  the opening sign-in ID before interactive components initialize. It observes
  authentication-state changes and unsubscribes on disposal.
- `WebCircuitSessionState` validates the current principal against that binding.
  A replacement sign-in yields SessionChanged; anonymous or failed authentication
  yields SessionEnded. Existing terminal state remains terminal, preserving the
  first reason and the explicit recovery requirement.
- The forwarding handler independently reads and validates the current circuit
  authentication state before forwarding a protected request. A stale handshake
  HttpContext cannot override that state, and a delayed/missing notification cannot
  allow the next request to adopt B's identity. The existing post-lookup terminal
  check remains in place.
- Both paths use the existing Changed event and monitor; no rendering redesign,
  cookie mutation, shared user state, automatic navigation or draft clearing was added.
  Binding is limited to current local activity tickets. Request-only clients,
  previous-version/OIDC flows and the explicit public intake-access exception keep
  their prior behavior. Already-dispatched requests are not retroactively cancelled;
  API authorization/revocation still governs them.
- `WebCircuitAuthenticationStateProvider` implements the framework's host
  authentication-state contract. Once the local circuit is bound, it validates
  incoming host principal updates before publishing them. Changed/anonymous
  identities terminate the shared state while preserving the original UI snapshot,
  preventing authorization-driven editor removal. That snapshot grants no new API
  access because the forwarding handler rejects terminal state. Normal same-sign-in
  updates and unbound prerender/request updates retain their normal behavior.

### Integration coverage

Twelve new theory cases in `tests/PTDoc.Tests/Web/WebScopedHttpClientFactoryTests.cs`
use the real Web host's service registrations, resolve its registered circuit
handler, invoke its opening lifecycle, and render the actual monitor alongside a
draft component that consumes the production scoped HttpClient. The transport is
a controlled test double; nine cases control authentication notifications with a
test provider, while three exercise the production host authentication provider
and AuthorizeView together. They cover:

| Boundary | Assertions |
| --- | --- |
| Activity callback: SessionEnded / SessionChanged | Sibling component reads and saves plus named-client reads receive 401 without transport; correct recovery action appears; draft and navigation remain; a separate circuit still forwards successfully. |
| Component API 401 | Real forwarding wrapper ends state and updates the sibling monitor; later reads never reach transport. |
| Component API 409 / 503 | Ordinary conflict/transient failure does not end the circuit; subsequent reads still forward. Activity 409 remains a distinct sign-in-change signal. |
| Replacement principal with notification | Recovery appears before a protected operation; retained draft cannot forward the new token. |
| Replacement principal without notification | Outbound guard detects changed identity even when HttpContext still contains A; transport remains untouched. |
| Anonymous reconnect | Sign in recovery, no forwarded request, retained draft. |
| Same-sign-in reconnect | Request forwards normally with the original token and no false recovery alert. |
| Production host authentication provider and AuthorizeView (3 cases) | Replacement/anonymous updates preserve the exact editor instance and draft while blocking writes and displaying recovery; unchanged identity remains authorized and usable. |

These tests intentionally emulate the circuit lifecycle and JS callback; they do
not claim to run SignalR. The complementary new
`tests/PTDoc.Web.UiQa/tests/session-circuit-boundary.spec.ts` supplies **seven actual
browser cases** through the existing disposable localhost harness:

- Activity 401 and 409 drive the unmodified JavaScript callback into the real
  monitor and sibling Settings provider editor, preserving a synthetic draft.
- API POST 401 versus 409/503 drives the actual server-side client and verifies
  the correct recovery behavior.
- A real SignalR disconnect, replacement browser sign-in and `Blazor.reconnect()`
  retain the original page/draft and must raise Reload recovery without relying on
  an activity failure. A new tab's fresh circuit remains usable.
- An anonymous reconnect must similarly retain the actual authorized editor and
  display Sign in without a router redirect; its protected reads/saves stay blocked.
- Unconsumed, exact-path fault rules prove denied reads/saves do not reach the
  API proxy; the rules also prevent a faulty implementation from persisting the
  synthetic provider. Distinct local-validation/error transitions ensure stale
  UI messages cannot satisfy the assertions before the server callback completes.

The browser suite uses the actual Web composition and a copied database, not a
test-only production endpoint. Its reconnect control uses .NET 8's internal
disconnect hook to stop the connection without disposing the retained circuit;
this harness dependency should be revisited when upgrading Blazor. Authentication
credentials come only from the existing out-of-band local fixture environment.

### Verification and handoff

Passed: ten activity JavaScript regressions, two fault-proxy infrastructure tests,
JavaScript harness syntax check, seven-case Playwright discovery, and both git diff
whitespace checks. The fault-proxy tests initially could not bind a loopback socket
inside the sandbox; both passed when rerun with loopback permission. Discovery is
not browser execution. No .NET build/test or full browser run was performed, in
accordance with AGENTS.md; their results are required before claiming the fix verified.

Run from the repository root:

```bash
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~WebScopedHttpClientFactoryTests|FullyQualifiedName~WebSessionActivityMonitorTests|FullyQualifiedName~ApiAccessTokenForwardingHandlerTests|FullyQualifiedName~WebSessionActivityIntegrationTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal
dotnet format src/PTDoc.Web/PTDoc.Web.csproj --verify-no-changes --verbosity minimal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal
```

The existing localhost harness launches **Debug** artifacts with `--no-build`.
Build both hosts in that configuration before running it:

```bash
dotnet build src/PTDoc.Api/PTDoc.Api.csproj --configuration Debug
dotnet build src/PTDoc.Web/PTDoc.Web.csproj --configuration Debug
cd tests/PTDoc.Web.UiQa
PTDOC_UI_QA_REQUIRE_FULL_FIXTURES=false PTDOC_UI_QA_PIN=<local-seeded-pin> npm run test:branch-live-unblock -- --session-boundary --headed
```

Use the existing seeded SQLite source database and installed Playwright Chromium;
set the existing admin credential overrides if the local fixture differs. Do not
use hosted-beta credentials or hosts for this suite. The harness creates its own
loopback hosts/proxy and disposable database copy. Reports remain ignored output.

Architecture/security documentation and CHANGELOG now describe the reconnect
guard. The preceding review-audit edits were preserved as historical evidence.
No commit, push, GitHub reply/resolution or review request was made. The concern
has no inline thread to resolve; after all verification passes, an evidence-backed
summary response would require separate user authorization. Existing resolved
threads need no duplicate replies.
