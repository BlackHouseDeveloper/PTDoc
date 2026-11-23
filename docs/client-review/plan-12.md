# Plan PR 12: Complete patient chart editing and patient-entry actions

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R008, R024, R028
- Original plan status: Ready for Development
- Branch: `codex/plan-12-patient-chart`
- Base: `main`
- Pull request [#167](https://github.com/BlackHouseDeveloper/PTDoc/pull/167)
- [Authoritative plan brief](source/implementation-plan.md#pr-12-complete-patient-chart-editing-and-patient-entry-actions)
- Review base `6d44db826ddc52930e2b3e1d0243b251495140cf` (November 18, 2026).

## Current evidence and changes

R024 adds chart editing for phone, both address lines, city, state/province, postal code and emergency contact name/phone. The editor sends changed values and the loaded patient version. An empty string clears a supported optional field. Omitted values retain their stored representation, including nulls and compound names. Invalid input and stale saves keep the form open with the entered values.

R028 opens the existing Add Patient modal under the selected clinic's write policy. R008 places keyboard focus on Daily Treatment Note on each activation. The chart also initializes the viewer's time zone and applies it to timeline entries.

Main already contained the patient timestamp concurrency token in the runtime model and all three snapshots. The incremental change uses those definitions. Other chart requests that lose a patient write race receive HTTP 409, and their writes roll back. Patient edits advance the timestamp even if the clock repeats or moves backward. A sync-only acknowledgement preserves the version a chart user loaded. PostgreSQL responses use the precision the database stores.

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

- [x] **AC1:** Given an authorized user editing phone, address and emergency contact, when saving and reloading, then all changes persist and unchanged fields remain intact.
- [x] **AC2:** Given invalid required data or a version conflict, when saving, then a clear error preserves the user’s edits.
- [x] **AC3:** Given the patient page, when Add Patient is selected, then the existing creation workflow opens and saves a clinic-scoped patient.
- [x] **AC4:** Given Start New Note, when activated, then the chooser is in view and keyboard focus reaches its choices.

### Rollout and Regression Risks

Partial updates may clear omitted fields; creation from a chart must not accidentally edit the current patient.

## Verification performed

### Historical verification, October 2026

The following results belong to the original Plan 12 patch on its earlier base.

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

## November 2026 review against current main

The local-only tip `53036c3d` and published tip `513f5a93` had equivalent Plan 12 code. Their patches differed only in changelog placement. Both histories were preserved locally. A single Plan 12 patch was replayed onto main after #166. The changelog kept main's existing entries, and the chart test setup retained both viewer-time-zone interop and focus interop. The incremental diff leaves intake navigation, language, consent, encounter routing, outcome definitions, capability policies and clinic-membership enforcement as merged.

| Review source | Classification | Evidence and disposition |
| --- | --- | --- |
| [Patient write conflicts](https://github.com/BlackHouseDeveloper/PTDoc/pull/167#discussion_r4376299883) | Valid | The generic error handler previously returned 500 for patient races during document and communication writes. It now maps a concurrency exception whose conflicting entries include a patient to 409. Separate HTTP regressions commit a competing update after the chart operation loads the patient and verify the winning phone and the absence of a partial child record. |
| [PostgreSQL version precision](https://github.com/BlackHouseDeveloper/PTDoc/pull/167#discussion_r4376299942) | Valid | POST and PUT return the tracked patient. The production interceptor now stamps a representable patient version. The PostgreSQL regression exercises creation, sync acknowledgement, two updates using the preceding response version, and rejection of a stale version under a fixed sub-microsecond clock. |
| [Review summary](https://github.com/BlackHouseDeveloper/PTDoc/pull/167#pullrequestreview-5650725836) | Duplicate | The summary links the same two inline findings. GitHub returned one review, two threads with one comment each, zero conversation comments and no further pages. Neither the published review body nor the rendered PR conversation showed a Previously missed section. The authenticated Copilot session log exposed only two startup events. The browser session link returned 404. |

### Model and migration evidence

All three disposable EF scaffolds succeeded with the pinned tool. SQLite reported no pending model changes and generated empty Up/Down methods. PostgreSQL reported four existing index additions on patient-provider relationships, insurance authorizations, note templates and clinical notes. SQL Server reported the same four additions plus three filtered-index replacements on provider entries, insurance policies and note templates. A separate Release build of the exact main commit produced the same three exit codes (SQLite 0, SQL Server 1, PostgreSQL 1) and byte-identical generated migration operations for every provider. The runtime model, provider snapshots and migration trees also match the review base. None of the generated operations addresses patient timestamp concurrency. The diagnostic migrations were generated outside the worktree.

### Verification on the rebased implementation

| Check | Result |
| --- | --- |
| Release test-project build, including API, Web and shared UI | Passed with zero warnings and errors. Debug build for local browser hosts also passed with zero warnings and errors. |
| Focused Release API, component, timeline, entry-action, tenancy, RBAC and end-to-end tests | 377 passed, zero failed or skipped. |
| Full Release CoreCi gate | 2,743 passed, zero failed or skipped. |
| Formatting and source checks | Core, Application, Infrastructure, API, UI and Tests passed the owning-project formatter. The tracked secret-policy scan and Git whitespace checks passed. |
| Isolated Chromium | 3 passed, zero skipped. Each width (390, 768 and 1440 px) exercised all eight fields, explicit clearing, invalid DOB retention, a competing save and preserved winner, a long contact value, clinic-owned creation, viewer-local time at UTC+05:45, and repeated Enter/Tab focus. |
| SQLite provider and observability | 4 passed. The 2 PostgreSQL-specific tests skipped in SQLite mode and both ran against PostgreSQL. |
| PostgreSQL 16.10 | The CI-style EF migration completed on a private disposable cluster. All 3 provider tests passed, including patient POST → PUT → PUT with returned versions, a sync acknowledgement, stale-write rejection and the provider's upgrade/downgrade and CRUD regression. |
| SQL Server | Design-time checks and diagnostic scaffolding ran locally. Live migration and persistence coverage uses the SQL Server Actions job. |

AC1–AC4 passed the synthetic API/component/browser checks above. Native installation, screen-reader testing and client acceptance were outside this verification pass.

### Reproduce the checks

Run from the repository root with SDK 8.0.417. The browser harness takes a local seeded PIN through its environment and starts disposable Development hosts. Supplying a nonexistent source database path creates a fresh synthetic database. Browser coverage includes a competing API write, the stored winner, keyboard traversal and a fractional UTC offset in the viewer's time zone.

```sh
dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build -c Release --filter 'FullyQualifiedName~PatientDemographics|FullyQualifiedName~PageScopedAppointmentUsageTests|FullyQualifiedName~PatientsPageTests|FullyQualifiedName~AuthorizationCoverageTests|FullyQualifiedName~TimelineTimestampFormatterTests|FullyQualifiedName~AddPatientModalTests|Category=Tenancy|Category=RBAC|Category=EndToEnd' --verbosity normal -- xUnit.ParallelizeTestCollections=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build -c Release --filter 'Category=CoreCi' --verbosity normal --logger 'trx;LogFileName=test-results.trx' -- xUnit.ParallelizeTestCollections=false
DB_PROVIDER=sqlite dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build -c Release --filter 'Category=DatabaseProvider|Category=Observability'
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore -c Debug
(cd tests/PTDoc.Web.UiQa && npm run test:branch-live-unblock -- --grep 'Plan 12 patient chart')
git diff --check origin/main
```

For formatting, run `dotnet format <project> --verify-no-changes --verbosity diagnostic` for Core, Application, Infrastructure, API, UI and Tests. For each provider use `EF_PROVIDER=<provider> dotnet tool run dotnet-ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.<Provider> -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext --no-build --configuration Release`. The server providers require their design-time connection settings. The database workflow specifies the live migration and smoke-test commands. [Provider workflow](../../.github/workflows/ci-db.yml)

## Remaining acceptance work

Native runtime installation, assistive-technology testing and client acceptance remain open. PR #169 targets this foundation. Its insurance-editor changes require a separate rebase and verification when #167 is integrated.
