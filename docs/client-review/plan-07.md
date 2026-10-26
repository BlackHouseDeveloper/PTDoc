# Plan PR 07: Prefill intake and preserve patient and policy data across entry flows

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R020, R031
- Original plan status: Ready for Development
- Branch: `codex/plan-07-intake-prefill`
- Base: `main`
- Expected GitHub number: #161; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-07)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Both service-based EnsureDraft and direct API creation now read saved profile values. The patient-create API used by Add Patient and appointment patient entry is exercised end to end with two policies and a fresh intake reload. Existing editable drafts are never changed on GET; a patient can confirm filling blank fields or keep their answers. Nonblank answers, explicit blanks at new creation, unknown direct-API JSON extension fields, source policy IDs, and locked/submitted evidence are preserved. Ambiguous active policies and archived normalized history do not fall back to stale legacy policies. Clinical history, consent decisions and authorization placement are excluded. The focused suite passed 302 tests, including eight added service/component/API cases plus the JSON preservation regression. No EF entity changed; no database migration is required.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Product/engineering: confirm any fields deliberately excluded from prefill; ordinary saved demographics and policies are approved scope. Clinical history reuse is excluded.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** AC1 and AC5 for deliberately excluded or ambiguous legacy fields.

## Development specification

### User Problem

Patients repeat known details and information entered during creation does not reliably reach the chart or intake.

### Confirmed Requirement

Carry applicable Add Patient/Add Appointment demographics and primary/secondary policy information into the patient profile and created/sent intake, including previously sent editable intakes that lack known information.

### Expected Behavior

New, resumed and previously sent editable intakes expose applicable known patient/policy information while preserving patient answers and clinician corrections.

### Scope

Trace each creation/delivery path and map existing demographics and policy fields; validate initial save, delivery, resume and reload.

### Out of Scope

ROM-to-MMT mapping, automatic copying of old clinical answers, subscriber eligibility decisions or authorization placement.

### Implementation Guidance

**Technical Recommendation:** reuse IntakeDeliveryService, IntakeDraftCanonicalizer, IntakeDomainProjection and NoteWorkspacePayloadMapper. Seed new drafts once. For existing sent editable drafts, fill only provably untouched fields; when legacy blank provenance is unknown, offer known profile values for explicit confirmation instead of silently overwriting an intentional blank. Preserve answers, concurrency versions and submitted/locked records.

### Data and Migration Considerations

Preserve policy identity, primary/secondary designation and provenance. No new patient master schema is assumed. Distinguish never-answered from intentionally cleared values before backfilling editable drafts; unknown legacy blanks require confirmation. Locked intakes remain immutable.

### Security and Privacy Considerations

**Technical Recommendation:** obtain patient and policies using the intake’s clinic and authorized patient link; never accept a client-provided patient ID as proof of access.

### Acceptance Criteria

- [ ] **AC1:** Given a patient with saved address, phone and two policies, when a new authorized intake is created or sent, then applicable fields match those saved values and policy order.
- [ ] **AC2:** Given an intake with edited answers, when reopened, then initialization does not overwrite those answers.
- [ ] **AC3:** Given Add Patient or appointment entry succeeds, when the profile and a new intake open, then the entered patient data is retained after reload.
- [ ] **AC4:** Given a patient in another clinic, when an intake seed request references that patient, then access is denied.
- [ ] **AC5:** Given a previously sent editable intake missing known demographics or policy data, when reopened, then untouched fields receive the known information or ambiguous legacy blanks offer it for confirmation without replacing existing answers.

### Rollout and Regression Risks

Accidental stale overwrite or policy duplication. Roll out with mapping comparison and reject ambiguous writes.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~Intake' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 302 passed, 0 failed, 0 skipped; final Debug build succeeded. |
| dotnet format whitespace src/PTDoc.Application/PTDoc.Application.csproj --no-restore --include src/PTDoc.Application/DTOs/IntakeDtos.cs src/PTDoc.Application/Services/IntakeDraftPersistence.cs src/PTDoc.Application/Services/IntakeResponseDraft.cs src/PTDoc.Application/Intake/IntakeProfilePrefill.cs --verify-no-changes --verbosity minimal | PASS: changed C# files match repository whitespace formatting. |
| dotnet format whitespace src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --include src/PTDoc.Infrastructure/Services/IntakeService.cs src/PTDoc.Infrastructure/Services/IntakeProfilePrefillReader.cs --verify-no-changes --verbosity minimal | PASS: changed C# files match repository whitespace formatting. |
| dotnet format whitespace src/PTDoc.Api/PTDoc.Api.csproj --no-restore --include src/PTDoc.Api/Intake/IntakeAccessEndpoints.cs src/PTDoc.Api/Intake/IntakeEndpoints.cs --verify-no-changes --verbosity minimal | PASS: changed C# files match repository whitespace formatting. |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Components/Intake/Models/IntakeWizardState.cs src/PTDoc.UI/Services/IntakeApiService.cs --verify-no-changes --verbosity minimal | PASS: changed C# files match repository whitespace formatting. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Intake/IntakeServiceTests.cs tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs tests/PTDoc.Tests/Integration/IntakeProfilePrefillIntegrationTests.cs --verify-no-changes --verbosity minimal | PASS: changed C# files match repository whitespace formatting. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 7 | PASS: requirement IDs, required sections, local links, private-path and git diff whitespace checks. |

## Remaining acceptance work

Optional release acceptance: exercise a synthetic OTP-delivered link in a browser and confirm the existing-theme prompt at mobile and desktop widths. No real delivery was sent. Product may later identify additional deliberately excluded profile fields; clinical history and disputed authorization fields remain excluded.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
