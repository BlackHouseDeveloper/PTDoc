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

## PR 162 review verification and local remediation

Reviewed published head `5a2bc16ac7a138f84c05e42ef117eafd4e09ec4c` against base `6276027251609df64ffbbfa883f66e2d83dd57e4`. The local checkout initially matched that head and had no uncommitted changes. Fixes below are local and uncommitted. The earlier 302-test result above belongs to the published head, **not** these follow-up changes.

### Complete feedback inventory and deduplication

Retrieved the review submissions, merged comment timeline, and thread-aware review feedback through the GitHub connector, then independently retrieved GitHub GraphQL connection totals, pagination, full bodies, and minimized status. There is one submitted review, four inline comments in four unresolved/current threads, and zero conversation comments. Every connection, including each review/thread comment connection, reports `hasNextPage: false`. None of the comments is minimized. No outdated/resolved thread, suppressed-comment section, or **Previously missed** finding exists in the returned full payload. The four review-overview bullets link to these same comments and are **duplicates**, requiring no separate implementation or thread action.

There are **four unique valid/actionable findings**, zero outdated findings, zero invalid findings, and zero ambiguous findings among the retrieved review comments. The additional compatibility decision below is distinct from that inventory.

| Finding / thread | Classification and evidence at the reviewed head | Local correction and regression evidence |
| --- | --- | --- |
| [Policy identity/freshness, including secondary coverage](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624649) — `PRRT_kwDOQt-faM6wMs1y` | **Valid.** `IntakeProfilePrefill.ApplyTo` stored source IDs and one aggregate timestamp, but `IntakeDomainProjection.UpsertPolicyAsync` selected the active row by patient/priority and never read those source fields. `IntakeService.SubmitAsync` and `IntakeEndpoints.SubmitIntake` call that projection after updating patient/intake state. Authorizations remain attached to the selected policy ID, so replacement/reordering can mix old policy data with existing authorizations. The standalone access endpoint also writes legacy payer fields and must reject stale sources. The priority-based writer already existed in the base commit; new automatic prefill makes unchanged submissions reach this failure, so this is not outdated. | Shared `HasCurrentPolicySourcesAsync` checks patient, clinic, unique active priority, ID and freshness before service/API/standalone submission writes. New snapshots retain exact per-policy timestamps; legacy snapshots use their existing combined timestamp without refreshing it for demographic-only acceptance. Projection additionally selects the accepted ID and checks freshness. `IntakeServiceTests.ProfilePrefill_RejectsStalePolicyBeforeMutatingSubmission` covers primary/secondary replacement, reorder, edits (including an edit older than the aggregate profile timestamp), archive/inactive/ambiguity, foreign identity/clinic, and missing/legacy timestamps. `IntakeProfilePrefillIntegrationTests.Submit_ValidatesPolicySourcesBeforeAnyWrites` covers HTTP 409, unchanged patient/intake/authorization evidence, and valid standalone submission. |
| [Compound first names](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624708) — `PRRT_kwDOQt-faM6wMs2d` | **Valid.** `IntakeProfilePrefillReader.ReadAsync` joined structured names, while both `IntakeService.SubmitAsync` and `IntakeEndpoints.ApplySubmittedIntakePatientFields` split at the first space even when the submitted name was unchanged. The latter helper also serves standalone submission. The split predates this PR, but automatic prefill newly exposes saved compound names to it. | Both name writers skip rewriting an exact unchanged trimmed full name. Explicit edits retain existing parsing. `ProfilePrefill_SubmitPreservesCompoundNameAndPolicyIdentity` covers unchanged and edited service submission; `CreatedPatient_ProfileAndBothIntakeCreationRoutesRetainSavedValues` now submits/reloads through both creation routes; the successful standalone case also checks `Mary Jane` / `Smith`. |
| [Self Pay/policy-only prefill availability](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624750) — `PRRT_kwDOQt-faM6wMs24` | **Valid.** `InsurancePolicyService.ValidatePolicy` permits Self Pay without a carrier. `HasValuesForBlankFields` only checked carrier/member for available primary data, while `ApplyTo` also supported payer type, group and adjuster fields. The wizard conditions its confirmation on that availability method. | Availability now includes every supported primary field and the secondary group number, while retaining the existing whole-policy blank check. `ProfilePrefill_OffersEverySupportedPolicyField_WithoutMixingExistingPolicies` covers the omitted fields. `IntakeWizardPageTests.ProfilePrefill_SelfPayOnlyOfferSurvivesWizardSaveWithIndependentPolicyVersions` checks actual confirmation and save, retaining a separately accepted secondary version. Existing apply/dismiss and locked-record tests remain unchanged. |
| [Legacy providerType alias](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624784) — `PRRT_kwDOQt-faM6wMs3N` | **Valid.** `PatientInfoPage.ProviderTypeOptions` and payer serialization use `providerType`, including `workers_comp`, `motor_vehicle` and `self_pay`. The reader only hydrated member-number aliases. `InsurancePayerCard` expects display labels and uses those labels to show adjuster fields. | Case-insensitive alias lookup translates the seven values supported by the intake selector, retaining canonical `payerType` precedence. `ProfilePrefill_LegacyProviderTypeUsesIntakeLabels` covers each label, whitespace/case, adjuster values, and canonical precedence. Existing `InsurancePayerCard_UpdatesSecondaryInsuranceAndAdjusterFields` verifies that the Workers' Compensation label exposes working adjuster controls. |

Supporting source: [prefill mapping](../../src/PTDoc.Application/Intake/IntakeProfilePrefill.cs), [profile reader](../../src/PTDoc.Infrastructure/Services/IntakeProfilePrefillReader.cs), [projection and source validator](../../src/PTDoc.Infrastructure/Services/IntakeDomainProjection.cs), [service submission](../../src/PTDoc.Infrastructure/Services/IntakeService.cs), [authenticated API](../../src/PTDoc.Api/Intake/IntakeEndpoints.cs), [standalone API](../../src/PTDoc.Api/Intake/IntakeAccessEndpoints.cs), [wizard](../../src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor), [policy validation](../../src/PTDoc.Infrastructure/Services/InsurancePolicyService.cs), [legacy producer](../../src/PTDoc.UI/Pages/PatientInfo/PatientInfoPage.razor), and [payer selector](../../src/PTDoc.UI/Components/Intake/Cards/InsurancePayerCard.razor).

Regression files: [service tests](../../tests/PTDoc.Tests/Intake/IntakeServiceTests.cs), [API round-trip/conflict tests](../../tests/PTDoc.Tests/Integration/IntakeProfilePrefillIntegrationTests.cs), [wizard tests](../../tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs), and existing [payer component tests](../../tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs). New cases are marked `CoreCi`, matching the existing test classes. They have not been executed during this session.

### Separate unresolved compatibility decision

**Ambiguous extension, not an additional retrieved review finding:** Patient Info offers `providerType = tricare`, while `InsurancePayerCard` has no TRICARE option and `InsurancePayerType` has no TRICARE member. A product/domain decision is needed on whether to add first-class support or map to an existing payer category. No category was guessed and no schema/enum expansion was made. Existing raw legacy payer JSON remains intact. This does not block the seven supported alias fixes, but the limitation should be disclosed when replying to the alias thread.

### Verification to run from the repository root

User-provided runs reported 353/355 focused tests passing and 1893/1895 CoreCi tests passing. The subsequent attached output reported 2318/2320 passing and identified both failures as `Submit_ValidatesPolicySourcesBeforeAnyWrites` with `change: "reordered"` (authenticated primary and standalone secondary cases). Both threw SQLite Error 19 on `PatientInsurancePolicies.PatientId, PatientInsurancePolicies.CoveragePriority` while saving the fixture, before the submission request. The fixture now swaps priorities in three saves through the unused tertiary slot inside one transaction, preserving policy IDs, timestamps and authorization links. Additional assertions verify the reordered primary/secondary member numbers remain unchanged after HTTP 409. The production fixes are unchanged; these corrected tests still need to be rerun.

First rebuild and rerun the affected integration class:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug --filter 'FullyQualifiedName~IntakeProfilePrefillIntegrationTests' --verbosity normal -m:1 -p:BuildInParallel=false
```

`git diff --check` passes. Per `AGENTS.md`, no build, test, or dotnet formatting command was run automatically. The test project references API, Web, shared UI, Application, Infrastructure and Core, so the following build covers the changed application graph:

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug --no-build --filter 'FullyQualifiedName~Intake|FullyQualifiedName~InsurancePolicyServiceTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug --no-build --filter 'Category=CoreCi' --verbosity normal
```

Run the changed-project formatting gates as well:

```bash
dotnet format src/PTDoc.Application/PTDoc.Application.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.UI/PTDoc.UI.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity minimal
```

After these checks pass and the user explicitly authorizes publication and GitHub discussion changes, all four listed threads are candidates for a reply describing the fix and verification, followed by resolution. Disclose the TRICARE scope decision in the alias reply. No commit, push, comment reply, thread resolution, PR-level response, or review request was performed.

## Remaining release acceptance work

Optional release acceptance: exercise a synthetic OTP-delivered link in a browser and confirm the existing-theme prompt at mobile and desktop widths. No real delivery was sent. Product may later identify additional deliberately excluded profile fields; clinical history and disputed authorization fields remain excluded.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
