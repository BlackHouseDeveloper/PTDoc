# Plan PR 04: Validate role configuration and visit types against the existing Settings stack

**Implemented (bounded scope)** — only the changes and checks listed below are claimed.

- Requirements: R004, R037
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-04-settings-verification`
- Base: `codex/settings-connected-ui`
- Expected GitHub number: #158; actual: [#159](https://github.com/BlackHouseDeveloper/PTDoc/pull/159)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-04)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Verified existing #154 at 89cb3fc041f047512ed3083beb278dfc3573ee35 with synthetic Admin data. PTA role selection loaded; a reversible permitted billing visibility change survived reload and was restored. A custom 35-minute clinic visit type survived reload and a fresh login, and appeared in New Appointment. Two residual defects reproduced: Duration displayed blank because only 30/45/60/90 options existed, and Cancel/submit were outside the scrollable viewport at 390px. This incremental follow-up renders nonstandard current durations (including historical values) and bounds the dialog with internal scrolling. After compiling, the custom value displayed correctly at 390/768/1440px, keyboard Tab reached the visible Schedule action, and the synthetic saved appointment retained its visit-type ID and a 35-minute interval. Existing Settings contracts and permissions are unchanged.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

No new permission or clinical policy is introduced. Owner remains read-only under the existing role administration contract; existing custom and historical appointment data retain their meaning.

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
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~AppointmentComponentsTests&#124;FullyQualifiedName~AppointmentsPageTests&#124;FullyQualifiedName~SettingsAdministrationEndpointIntegrationTests&#124;FullyQualifiedName~SettingsAdministrationTests&#124;FullyQualifiedName~RolesPermissionsSettingsTests&#124;FullyQualifiedName~SchedulingVisitTypesSettingsTests' --verbosity minimal | PASS 209, 0 failed/skipped; includes custom 5/35/120-minute display/submission and historical 37-minute preservation. |
| Synthetic Chromium Settings persistence and New Appointment verification at 390/768/1440px, then inspect local synthetic appointment interval | PASS custom type/duration saved; 35-minute persisted interval; keyboard-reachable visible actions, no horizontal overflow. |

## Remaining acceptance work

The reproduced residual defects are fixed and verified. Before deployment, merge/reconcile existing #154 and this dependent #159, then run the integrated release gate. Full Owner/PT/PTA browser sessions, client acceptance and MAUI publishing were not run; server denied-role/tenant/locked-admin cases are covered by the targeted suite.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
