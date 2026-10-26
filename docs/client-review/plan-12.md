# Plan PR 12: Complete patient chart editing and patient-entry actions

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R008, R024, R028
- Original plan status: Ready for Development
- Branch: `codex/plan-12-patient-chart`
- Base: `main`
- Expected GitHub number: #166; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-12)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The prior chart editor only exposed full name, DOB and email. The update API already accepted contact fields but had no client version check. The chart now maps all existing contact/address fields and sends the loaded LastModifiedUtc as an optional ExpectedLastModifiedUtc token. The API validates provided values, preserves omitted fields, returns 409 for stale versions, and uses the existing timestamp column as an EF concurrency token for races after the initial check. Input parameters are not mutated. Add Patient links to /patients?action=add, reusing its existing modal and clinic-scoped API. Native focus/scroll reaches the first note button. No new clinical identity or authorization fields.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Place Add Patient on the chart as a link to the existing patient-list creation flow; list behavior stays the source of truth. Intake propagation remains Plan 07’s independently implemented scope.

## Development specification

### User Problem

Existing patient fields cannot all be corrected from the chart, and primary entry actions are hard to reach.

### Confirmed Requirement

Edit existing basic patient information including address, phone and emergency contact; expose Add Patient from the patient page; scroll/focus the note-type choices after Start New Note.

### Expected Behavior

Authorized users can update all already-supported basic fields, create a patient from the relevant chart/list context and immediately reach note-type choices.

### Scope

Connect existing fields and actions with validation, save/reload and meaningful focus.

### Out of Scope

New clinical identity categories, sex/gender decisions, or new authorization fields.

### Implementation Guidance

**Technical Recommendation:** reuse existing update DTO/version handling and AddPatientModal. Keep [Parameter] inputs immutable and use shared UI tokens. Route a newly created patient through existing ownership and duplicate-validation behavior.

### Data and Migration Considerations

No new schema assumed. Preserve omitted fields on patch/update and handle concurrent edits without silently overwriting another user.

### Security and Privacy Considerations

**Technical Recommendation:** enforce clinic-scoped update/create permission at the API; contact fields and DOB must not enter UI telemetry.

### Acceptance Criteria

- [ ] **AC1:** Given an authorized user editing phone, address and emergency contact, when saving and reloading, then all changes persist and unchanged fields remain intact.
- [ ] **AC2:** Given invalid required data or a version conflict, when saving, then a clear error preserves the user’s edits.
- [ ] **AC3:** Given the patient page, when Add Patient is selected, then the existing creation workflow opens and saves a clinic-scoped patient.
- [ ] **AC4:** Given Start New Note, when activated, then the chooser is in view and keyboard focus reaches its choices.

### Rollout and Regression Risks

Partial updates may clear omitted fields; creation from a chart must not accidentally edit the current patient.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~PatientDemographics&#124;FullyQualifiedName~PageScopedAppointmentUsageTests&#124;FullyQualifiedName~PatientsPageTests&#124;FullyQualifiedName~AuthorizationCoverageTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 42 tests, 0 failed, 0 skipped. |
| python3 /private/tmp/ptdoc-client-implementation/browser12.py; local Playwright conflict and creation checks | PASS: contact save/reload, invalid DOB retention, actual 409 retention, unchanged stored phone, clinic-scoped patient creation, and note-choice focus at 390/768/1440px. |
| EF_PROVIDER=sqlite dotnet tool run dotnet-ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext | PASS: no pending model changes. SQLite scaffold confirmed no schema operations; empty scaffold files removed. |
| dotnet format whitespace src/PTDoc.Application/PTDoc.Application.csproj --no-restore --include src/PTDoc.Application/DTOs/PatientDtos.cs --verify-no-changes --verbosity quiet | PASS. |
| dotnet format whitespace src/PTDoc.Api/PTDoc.Api.csproj --no-restore --include src/PTDoc.Api/Patients/PatientEndpoints.cs --verify-no-changes --verbosity quiet | PASS. |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Components/Patients/Profile/Models/PatientProfileVm.cs --verify-no-changes --verbosity quiet | PASS. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs tests/PTDoc.Tests/UI/Patients/PatientDemographicsEditorTests.cs tests/PTDoc.Tests/UI/Pages/PageScopedAppointmentUsageTests.cs --verify-no-changes --verbosity quiet | PASS. |
| python3 workflow.py check 12; git diff --check | PASS: document sections, requirement IDs, links and whitespace. |

## Remaining acceptance work

AC1–AC4 passed in targeted component/API and local browser checks. Native platform installs and client acceptance remain unverified. SQL Server/PostgreSQL design-time scaffolding is limited by the existing IntegrationConnection snapshot helper collision; no schema change is needed for this metadata-only feature.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
