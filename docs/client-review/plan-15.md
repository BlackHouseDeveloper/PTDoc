# Plan PR 15: Resolve authorization-field placement in patient and policy entry

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R029, R030
- Original plan status: Needs Clarification
- Branch: `codex/plan-15-authorization-placement`
- Base: `main`
- Expected GitHub number: #169; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-15)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

User approved removal of Authorization Required from intake/chart entry on 2026-10-26. R029 independently required removing the initial authorization number. No such requirement control exists in the patient questionnaire itself; the entry surfaces found were AddPatientModal and AdditionalAuthorizationSettingsPanel.

Both initial registration fields and their serialization are removed. A patient created with or without insurance does not acquire a No/false/pending requirement value or an authorization number. Existing form result properties remain compatible for callers, but these fields are no longer collected. The chart no longer asks for requirement status. Its authorization/referral details remain accessible even when the historical flag is absent/Unknown/No, so removing the gate does not hide saved numbers. Unrelated payer saves retain the original absent, empty, Unknown, Yes or No value exactly.

No EF migration, record deletion, permission expansion or benefit-enforcement change. Plan 14 does not introduce a requirement toggle; this independent main-based diff does not consume its new workflow contracts.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Approved placement matrix: Add Patient collects neither Authorization Required nor authorization number; intake/chart entry has no Authorization Required control; existing policy/authorization details remain available under existing permissions. Missing stays missing; no inference to No. Stored historical requirement values and numbers are preserved.

## Development specification

### User Problem

Initial registration asks for authorization details before benefits verification, but later discussion appears to reintroduce Authorization Required.

### Confirmed Requirement

Removing the authorization number from initial Add Patient is explicit. Authorization Required is contradictory: early removal versus later Yes/No discussion in a new-policy context that the developer restated as Add Patient.

### Expected Behavior

An approved field-placement matrix states what is collected at initial patient creation versus verified policy entry, including Unknown versus Yes/No.

### Scope

Decision and then a small field-placement/UI-contract change; preserve stored authorization values.

### Out of Scope

Inventing benefit-verification rules, defaulting unknown to No or deleting existing authorization records.

### Implementation Guidance

**Technical Recommendation:** implement the uncontested number removal as an independent sub-slice if desired; keep the disputed toggle gated until answered. Do not overload missing JSON with a false value.

### Data and Migration Considerations

No data deletion. Preserve existing unknown/yes/no semantics and old numbers in their authoritative records.

### Security and Privacy Considerations

**Technical Recommendation:** retain authorization-edit permissions and provenance of verified versus patient-entered information.

### Acceptance Criteria

- [ ] **AC1:** Given an approved placement matrix, when a new patient is entered, then only approved initial fields appear.
- [ ] **AC2:** Given unknown benefit status, when a patient is created, then the saved state is not silently converted to No.
- [ ] **AC3:** Given an existing authorization number, when entry fields change, then the historical value remains available in its authorized policy/authorization view.

### Rollout and Regression Risks

Wrong defaults can misrepresent coverage. Do not ship a guessed toggle placement.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~AddPatientModalTests&#124;FullyQualifiedName~PatientInfoRouteTests&#124;FullyQualifiedName~InsurancePolicyServiceTests&#124;FullyQualifiedName~PatientsPage&#124;FullyQualifiedName~DashboardWidgetNavigationTests' --verbosity minimal -m:1 | PASS: 72 tests, 0 failures/skips. Existing tests that selected removed controls were updated; validation and persistence assertions retained. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~AddPatientModalTests' --verbosity minimal -m:1 | PASS: 16 tests after final section-copy update. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --include tests/PTDoc.Tests/UI/AddPatientModalTests.cs tests/PTDoc.Tests/UI/Pages/PatientInfoRouteTests.cs tests/PTDoc.Tests/UI/Pages/PatientsPageTests.cs tests/PTDoc.Tests/UI/Dashboard/DashboardWidgetNavigationTests.cs | PASS. |
| Playwright CLI local synthetic patient registration and historical authorization save/reload; SQLite fixture readback | PASS: fields absent, registration succeeds with NULL number and absent requirement/status; original Unknown and historical number preserved after chart save/reload. |
| python3 workflow.py check 15; git diff --check | PASS requirement, section, local-link, sensitive-path and whitespace checks. |

## Remaining acceptance work

Client visual acceptance and native platform publish/install remain open. Integrate alongside Plan 14 without reintroducing the removed toggle or dropping its version-preserving legacy save behavior.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
