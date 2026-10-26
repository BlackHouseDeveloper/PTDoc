# Plan PR 14: Consolidate insurance policy and authorization editing

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R044, R045, R046
- Original plan status: Ready for Development
- Branch: `codex/plan-14-insurance-editor`
- Base: `codex/plan-12-patient-chart`
- Expected GitHub number: #168; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-14)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented R044–R046. The chart exposes one insurance entry. Policy limits and plan-year controls sit together, with one adjuster field set. One save coordinates policy and edited child records; archived records remain read-only, and existing nonarchived historical editing rules remain unchanged. EF concurrency failures roll back policy, child and audit changes together. Tenant and child ownership are enforced server-side.

The normalized editor omits untouched children and preserves their versions/utilization. Saved legacy details remain in a collapsed compatibility section with one legacy save, extension JSON preservation, stable history-row correction, and an explicit conflict/discard flow. New patients do not see a second empty editor. Unknown historical row identity is rejected rather than guessed. Normalized saves invalidate stale patient-level payer editors through Plan 12's version token.

All providers receive only nullable TotalVisitLimit / VisitLimitType columns. SQL Server/Postgres snapshots previously re-registered already generated integration metadata; scaffolding now materializes that existing metadata without recreating its existing indexes. New migration target models retain the Plan 12 patient concurrency token. SQLite downgrade uses native DROP COLUMN as supported by the tested bundled provider.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

User confirmed that Authorization Required should be removed from intake/chart entry (Plan 15). This editor does not introduce that toggle. Existing historical edit/archive permissions remain the default; any broader clinical correction policy requires a separate decision.

## Development specification

### User Problem

Duplicate entry points and scattered forms make policy and authorization information difficult to maintain consistently.

### Confirmed Requirement

Remove the duplicate Insurance & Authorization entry; place visit limit and calendar/plan-year fields with policy entry; remove duplicate adjuster fields; combine related authorization/PCP/referral details and history with one save.

### Expected Behavior

Users edit each policy and its related authorization/referral information in a coherent workflow and receive one clear save result.

### Scope

UI consolidation and an atomic or explicitly coordinated save of already-supported fields; editable history subject to existing record rules.

### Out of Scope

Changing benefit rules, conflating historical and active authorizations, or resolving the disputed Add Patient field by assumption.

### Implementation Guidance

**Technical Recommendation:** retain distinct domain records behind the unified UI. Reuse existing service contracts; if one-save requires orchestration, implement it server-side in the appropriate Application/Infrastructure boundary with concurrency validation.

### Data and Migration Considerations

Preserve primary/secondary policy links, historical validity dates, utilization and referral provenance. No destructive table merge is implied. Migrate only if contract gaps are demonstrated.

### Security and Privacy Considerations

**Technical Recommendation:** API permission and clinic ownership apply to every child record; reject forged policy/authorization IDs and audit substantive edits.

### Acceptance Criteria

- [ ] **AC1:** Given a patient chart, when accessing insurance, then a single intended chart entry opens the consolidated workflow.
- [ ] **AC2:** Given a policy with visit limit and plan-year information, when saved once and reloaded, then all intended changes persist without duplicate adjuster fields.
- [ ] **AC3:** Given multiple authorizations and referrals, when editing one historical entry, then unrelated records and utilization remain unchanged.
- [ ] **AC4:** Given a validation/concurrency failure in one section, when saving, then the UI does not report a fully successful save or silently lose edits.

### Rollout and Regression Risks

Consolidation may accidentally combine separate clinical/business meanings. Compare before/after synthetic records.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Insurance&#124;FullyQualifiedName~PatientInfoRouteTests&#124;FullyQualifiedName~PatientClinicalInfoCard&#124;FullyQualifiedName~PatientDemographics&#124;FullyQualifiedName~AuthorizationCoverageTests' --verbosity minimal -m:1 | PASS: 91 tests, 0 failed/skipped; includes SQLite migration up/down/up and transaction race rollback, API permission and tenancy cases. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~PatientInsurancePoliciesPanelTests' --verbosity minimal -m:1 | PASS: 3 tests after moving save errors beside the action. |
| EF_PROVIDER=<sqlite&#124;sqlserver&#124;postgres> dotnet tool run dotnet-ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.<Sqlite&#124;SqlServer&#124;Postgres> -s src/PTDoc.Api --context PTDoc.Infrastructure.Data.ApplicationDbContext | PASS all three providers; inert local design-time connection placeholders for SQL Server/Postgres. No live provider connection claimed. |
| dotnet format whitespace <changed-project.csproj> --no-restore --verify-no-changes --include <changed C# files> | PASS changed Core/Application/Infrastructure/API/UI/Tests C# files; final test whitespace corrected and rechecked. |
| Playwright CLI synthetic local Web/API, browser one-save/reload, historical correction, real concurrent child and patient 409, explicit discard; document widths 390/768/1440 | PASS: retained inputs, no partial persistence, untouched record identity/utilization/version preserved; one chart insurance entry. Initial browser harness timing/selector assertions were corrected after fresh snapshots. |
| python3 workflow.py check 14; git diff --check; incremental diff against codex/plan-12-patient-chart | PASS requirements/sections/local links/whitespace; incremental scope excludes the Plan 12 foundation. |

## Remaining acceptance work

Client review and native MAUI publish/install remain open. Run these migrations against disposable SQL Server/Postgres provider databases before release; those live provider executions were not available in this session. No clinical/benefit-rule change is claimed.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
