# Plan PR 04: Validate role configuration and visit types against the existing Settings stack

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R004, R037
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-04-settings-verification`
- Base: `main`
- Expected GitHub number: #158; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-04)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

GitHub #153 is merged at 6276027. #154 is open, ready for review, and targets main; head 89cb3fc041f047512ed3083beb278dfc3573ee35. Its changed files include RolesPermissionsSettings, SchedulingVisitTypesSettings, SettingsAdministrationApiClient, Appointments, NewAppointmentModal and corresponding tests. The inspected role component loads server administration contracts and includes explicit Owner read-only behavior. This existing implementation is the feature owner; the original preview-only baseline is no longer sufficient evidence to create replacement code. PR-body test claims are not treated as independently run results. The exact #154 head was checked out detached without merging. A fresh Debug test build succeeded and 12 focused RolesPermissionsSettings/SchedulingVisitTypesSettings tests passed (0 failed/skipped). This is independently executed component evidence; authenticated browser persistence, role-picker and navigation acceptance remain open.

## Dependencies, blockers and owners

- QA/engineering: verify the exact #154 candidate in a synthetic authenticated browser, then retain a separate user merge checkpoint before main/deployment acceptance. Testing is already authorized; no replacement Settings implementation is needed.
- January/security owner: use the approved role capability matrix; do not infer additional privileges from the meeting role-picker complaint.

## Decisions and options

Engineering owner: supply integrated stack commit and deployment evidence. Validation preparation can proceed, but completion depends on that build.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** All post-stack configuration criteria.

## Development specification

### User Problem

Admins cannot configure roles, and the client needs configurable appointment visit types.

### Confirmed Requirement

Troubleshoot role configuration and verify the promised visit-type Settings workflow. This is a verification/follow-up slice of existing work, not a second Settings implementation.

### Expected Behavior

After the existing stack is integrated, authorized configuration persists across reload and appointment forms use saved clinic visit types; unsupported actions have a clear explanation.

### Scope

Reconcile meeting cases with #153/#154 acceptance evidence; file a separate delta only for a demonstrated residual gap.

### Out of Scope

New role privileges, organization ownership policy, unrelated kiosk/auto-check-in expansion, or duplicating the stack.

### Implementation Guidance

**Technical Recommendation:** compare approved role capability definitions with API enforcement and Settings presentation. Test allowed and denied operations, stale versions and appointment compatibility. Use existing stack migrations; do not invent replacement settings tables.

### Data and Migration Considerations

Role assignments, visit-type records and existing appointments may be affected by the stack. Verify old visit-type references continue to render and that role edits do not strand the last administrator.

### Security and Privacy Considerations

**Technical Recommendation:** server permissions and clinic scope must match UI; selecting a role must not itself grant that role. Record non-PHI configuration audits.

### Acceptance Criteria

- [ ] **AC1:** Given an authorized clinic administrator after stack integration, when selecting a configurable role, then its saved settings load and permitted changes survive a second session.
- [ ] **AC2:** Given an unauthorized role or different clinic, when sending the same mutation directly to the API, then it is denied.
- [ ] **AC3:** Given a new visit type saved in clinic A, when creating an appointment in A, then it is selectable; clinic B does not inherit it.
- [ ] **AC4:** Given an appointment with a historical/deactivated type, when opened, then its stored meaning is preserved.

### Rollout and Regression Risks

Merge/rebase conflicts and privilege drift are the main risks. Validate stack release parity before calling the meeting defects resolved.

## Verification performed

| Command/check | Result |
| --- | --- |
| git switch --detach 89cb3fc041f047512ed3083beb278dfc3573ee35; dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~RolesPermissionsSettingsTests&#124;FullyQualifiedName~SchedulingVisitTypesSettingsTests&#124;FullyQualifiedName~SettingsAdministrationApiClientTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: exact #154 candidate; 12 role/visit-type component tests, 0 failed/skipped. No matching SettingsAdministrationApiClientTests class exists; no client-test coverage is claimed by that filter term. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 4 | PASS: requirement IDs, specification/acceptance sections, local links, no private source paths and git diff --check. |

## Remaining acceptance work

Run synthetic Admin/Owner/PT/PTA browser scenarios against #154: save/reload roles and visit types, conflict recovery, authorized visibility, role picker and appointment consumption. Existing Owner read-only policy remains in force. User separately decides when to merge #154; no merge was performed.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
