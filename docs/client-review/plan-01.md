# Plan PR 01: Verify and repair active-use and multi-tab sessions

**Implemented** — only the changes and checks listed below are claimed.

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

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
