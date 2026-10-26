# Plan PR 01: Verify and repair active-use and multi-tab sessions

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R003, R005, R048, R061
- Original plan status: Verification Required
- Branch: `codex/plan-01-session-verification`
- Base: `main`
- Expected GitHub number: #155; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-01)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Main now includes the Settings service/authentication changes from merged #153. Web still configures a 15-minute sliding cookie and an eight-hour cookie MaxAge; MaxAge alone is not proof of an absolute server deadline. The API validates current security state and revocation. The focused synthetic authentication test run passed 49 tests, including cookie issuance/step-up, session validation and bearer security. This does not reproduce or rule out the reported active Blazor-circuit / second-tab failure. #154 still changes Web authentication UI and is unmerged. No authentication code was changed.

## Dependencies, blockers and owners

- Engineering/QA: run a controlled browser scenario on the integrated #154 build covering active editing across the idle interval, a second tab, explicit logout and API revocation. Existing unit/integration tests are not a substitute for this circuit/browser evidence.
- Product/security owner: confirm the existing anti-piggyback invariant and allowed idle/absolute policy; no timeout weakening is proposed.

## Decisions and options

Engineering and security owner: identify the existing anti-piggyback invariant and activity-renewal contract before coding; reproduction can start now. Its preservation controls AC2–AC3.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** AC1–AC3 (activity/session invariant).

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
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~WebLoginEndpointIntegrationTests&#124;FullyQualifiedName~AuthServiceTests&#124;FullyQualifiedName~LogoutSessionClassifierTests&#124;FullyQualifiedName~LocalBearerSecurityTests' --verbosity minimal | Passed: 49; failed: 0; skipped: 0. Main 6276027; synthetic fixtures; no production session used. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 1; git diff --check | Passed: requirement IDs, required sections, local links, sensitive local paths and whitespace checked. The script is a task-local document checker; no .NET behavior is asserted by it. |

## Remaining acceptance work

AC1–AC4 need browser/circuit evidence. Run two tabs in one synthetic user profile plus a separate second-user profile, preserve active draft text, capture credential-free reason codes, and compare Web/API source/release identifiers. The PR remains draft; no runtime defect is claimed fixed.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
