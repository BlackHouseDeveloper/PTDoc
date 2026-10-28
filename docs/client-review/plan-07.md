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

## Current PR 162 audit — branch `ccb2442`

This audit supersedes the historical review pass below. Both retrievals in this pass found head `ccb244288d9033ff6e42c9300f550cc4af8dc00c` and base `9779d2797c8197c41e2f8d44f1a9da98d2efd912`; the checkout matched the PR head and was clean before this work. The head includes `7e4e77a` (the first review fixes and corrected SQLite reorder fixture) and `ccb2442` (the merge of current main). GitHub reports **MERGEABLE**. Changes from this pass are local and uncommitted.

### Retrieval completeness and classification

Retrieved thread-aware feedback and reviews with the GitHub connector, and independently verified full GraphQL bodies, minimized state, totals and pagination twice. There are **2 submitted reviews, 10 inline comments in 10 threads, and 0 conversation comments**. All top-level and nested connections have `hasNextPage: false`. Eight threads are open/current; two are resolved/outdated. **Zero comments are minimized/suppressed, and neither review contains a suppressed or Previously missed section.** The fourteen finding references in the two review summaries repeat the inline comments and are duplicates, not fourteen additional findings.

After deduplication there are nine distinct concerns: **six valid/actionable**, **three outdated/already addressed**, and the original broad policy comment is a **duplicate** of the remaining specific policy-source concerns. There are **no invalid or ambiguous retrieved findings**. “Outdated” below is a code assessment; GitHub still marks the compound-name thread open/current. The separate TRICARE decision remains ambiguous and is described below.

| Comment / thread | Classification and evidence | Change or reason no additional change is required |
| --- | --- | --- |
| [4375624649 — original policy identity/freshness](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624649), `PRRT_kwDOQt-faM6wMs1y` (open) | **Duplicate / overlapping umbrella.** `7e4e77a` added scoped, active, unique-priority, ID and version checks in `IntakeDomainProjection.HasCurrentPolicySourcesAsync`, and `UpsertPolicyAsync` targets the accepted ID. The exact replacement/reorder scenario with retained IDs is already covered by `ProfilePrefill_RejectsStalePolicyBeforeMutatingSubmission` and `Submit_ValidatesPolicySourcesBeforeAnyWrites`. Remaining ways to lose or incorrectly validate that source are specifically described by 4396538992, 4396539015 and 4396539038. | Count and implement those specific defects once. Keep the original primary/secondary replacement/reorder and authorization-preservation coverage. The corrected SQLite fixture still stages reordering through Tertiary within a transaction. |
| [4375624708 — compound first names](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624708), `PRRT_kwDOQt-faM6wMs2d` (open) | **Outdated.** Both `IntakeService.SubmitAsync` and the shared API `ApplySubmittedIntakePatientFields` compare the submitted full name to the current joined name before splitting. `7e4e77a` therefore preserves `Mary Jane` / `Smith` for an unchanged submission. | No further name-splitting change. Existing `ProfilePrefill_SubmitPreservesCompoundNameAndPolicyIdentity` covers unchanged and explicitly edited names; `CreatedPatient_ProfileAndBothIntakeCreationRoutesRetainSavedValues` and the successful standalone case cover the API writers. Later clinician name corrections are a different defect, addressed by 4396538958. |
| [4375624750 — Self Pay/policy-only availability](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624750), `PRRT_kwDOQt-faM6wMs24` (resolved/outdated) | **Outdated.** `InsurancePolicyService.ValidatePolicy` allows Self Pay without carrier/member values. `HasValuesForBlankFields` now includes payer type, group and all adjuster fields, matching `ApplyTo`. | No additional availability fix. Existing `ProfilePrefill_OffersEverySupportedPolicyField_WithoutMixingExistingPolicies` and the updated wizard `ProfilePrefill_SelfPayOnlyOfferSurvivesWizardSaveWithIndependentPolicyVersions` cover the contract through explicit server acceptance. |
| [4375624784 — legacy reader providerType alias](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4375624784), `PRRT_kwDOQt-faM6wMs3N` (resolved/outdated) | **Outdated for the reported reader path.** `IntakeProfilePrefillReader` already maps all seven supported legacy labels and preserves canonical `payerType` precedence. Existing `ProfilePrefill_LegacyProviderTypeUsesIntakeLabels` covers aliases, case/whitespace and precedence. | No separate reader repair. The mapping is reused at the patient-write boundary for 4396539070; this is a different path because normalized records take precedence over legacy JSON. TRICARE is not assigned an invented category. |
| [4396538958 — stale demographics](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396538958), `PRRT_kwDOQt-faM6w-CT7` (open) | **Valid; implemented locally.** Before this pass both patient writers copied prefilled demographics unconditionally; the aggregate profile timestamp was only consulted for policies. An untouched old phone/name/address could overwrite a subsequent chart correction. | New intakes capture server-owned field values in `PrefilledDemographics`. `IntakeProfileUpdates.ForPatientWrite` compares current chart values to that snapshot and preserves fields changed in the chart, while retaining the patient's original answers in the submitted intake. Legacy prefilled drafts without a field snapshot conservatively preserve chart demographics. Both the service and the shared authenticated/standalone API writer use this projection. Tests: `ProfilePrefill_ServiceSaveAndSubmitPreserveSourceAndNewerDemographics` and `Submit_PreservesDemographicsCorrectedAfterPrefill` (both routes, all prefilled demographic fields, including corrections with an older timestamp). |
| [4396538992 — legacy source followed by a normalized policy](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396538992), `PRRT_kwDOQt-faM6w-CUV` (open) | **Valid; implemented locally.** The old early return treated two null policy IDs as proof that no source check was needed. A subsequently created normalized row was then selected by priority. | Creation records source absence as well as presence. For a prefilled draft, a null accepted ID now requires no current active normalized candidate at that priority. Both primary and secondary are checked; new rows cause reconciliation before submission. Tests: `ProfilePrefill_LegacySourceRejectsNewNormalizedPolicy` for both priorities, plus the existing no-write/authorization-preservation conflict matrix. |
| [4396539015 — metadata removed or forged by PUT](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396539015), `PRRT_kwDOQt-faM6w-CUk` (open) | **Valid; implemented locally.** Both PUT endpoints and `IntakeService.SaveDraftAsync` replaced response JSON with client data. `SyncEngine` also replaced the same JSON, so fixing only HTTP would leave another bypass. | `IntakeProfileProvenance.Preserve` removes incoming protected fields case-insensitively and restores the stored source snapshot, IDs, exact versions and omission choices. Unknown answer extensions remain. Both PUT routes and service save/submit call `PrepareAsync`; sync updates preserve metadata and discard reconciliation requests, while new offline records discard unverifiable client provenance and require reconciliation before writing existing policies. Creation captures the authorized server source. Tests: `Save_PreservesServerSourceWhenClientDropsOrForgesMetadata` (both routes, omission/null/forgery/alternate casing), the service demographic test (metadata dropped on save and direct submit), and `ReceiveClientPushAsync_AppliesIntakeFormPayload_WhenUnlocked` (create/update). |
| [4396539038 — aggregate timestamp fallback](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396539038), `PRRT_kwDOQt-faM6w-CUz` (open) | **Valid; implemented locally.** `IsPolicyVersionCurrent` accepted `policy.LastModifiedUtc <= ProfilePrefillSourceModifiedUtc` without an exact version. `SyncEngine` assigns incoming policy `LastModifiedUtc`, so a delayed 09:30 correction passes a 10:00 aggregate timestamp even though the prefill used 09:00 values. | Removed the aggregate fallback. A non-null accepted policy ID requires its exact timestamp; missing exact versions now require explicit reconciliation. `ProfilePrefill_MissingExactVersionRejectsDelayedCorrectionBelowAggregate` covers both priorities. The existing stale-source matrix now seeds missing versions in stored legacy JSON rather than attempting to modify protected metadata from the client. |
| [4396539070 — providerType ignored on patient save](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396539070), `PRRT_kwDOQt-faM6w-CVJ` (open) | **Valid; implemented locally.** `PatientInfoPage` writes `providerType`; `UpsertPatientRecordAsync` hydrated only member aliases before creating/updating normalized policies. The reader then correctly preferred that incorrectly categorized normalized row, bypassing its legacy alias mapping. | Reuse `NormalizeProviderType` in `HydrateLegacyPayerAliases` before normalized projection, retaining canonical payer precedence. `PatientInfoProviderType_NormalizesOnCreateAndUpdateBeforePrefill` submits the actual UI-shaped payload for all seven supported types, verifies normalized create and correction on update, checks intake prefill, and verifies canonical precedence. |
| [4396539095 — no conflict recovery](https://github.com/BlackHouseDeveloper/PTDoc/pull/162#discussion_r4396539095), `PRRT_kwDOQt-faM6w-CVR` (open) | **Valid; implemented locally.** A populated policy group could not use blank-field prefill, and editing its visible fields retained the stale source ID/version. There was no explicit way to stop projecting an archived policy. | The wizard now exposes refresh, use-current and omit actions for each policy. `IntakeProfileUpdate` carries an explicit action and the fingerprint of the offered profile; the server re-reads the authorized patient profile and rejects a changed/invalid offer. Acceptance copies current values and sources; omission clears that intake policy and suppresses its chart/policy projection. Switching primary identity clears policy-specific authorization answers instead of moving them to a replacement; existing chart authorizations remain attached to their original IDs. Other answers survive. Ambiguous active policies cannot be accepted as a single policy. Tests: `StalePolicy_CanBeExplicitlyReconciledAndResubmitted` (both routes/priorities, accept replacement/omit archived), `ProfilePrefill_ReconciliationRejectsStaleOfferAndPreservesOtherAnswers`, wizard apply/dismiss/Self Pay and recovery tests, and the API-service request serialization assertion. |

Implementation references: [prefill contract](../../src/PTDoc.Application/Intake/IntakeProfilePrefill.cs), [protected metadata](../../src/PTDoc.Application/Intake/IntakeProfileProvenance.cs), [explicit action contract](../../src/PTDoc.Application/Intake/IntakeProfileUpdate.cs), [server update/projection](../../src/PTDoc.Infrastructure/Services/IntakeProfileUpdates.cs), [source validation and normalized writes](../../src/PTDoc.Infrastructure/Services/IntakeDomainProjection.cs), [profile reader](../../src/PTDoc.Infrastructure/Services/IntakeProfilePrefillReader.cs), [service](../../src/PTDoc.Infrastructure/Services/IntakeService.cs), [authenticated endpoints](../../src/PTDoc.Api/Intake/IntakeEndpoints.cs), [standalone endpoints](../../src/PTDoc.Api/Intake/IntakeAccessEndpoints.cs), [sync writer](../../src/PTDoc.Infrastructure/Sync/SyncEngine.cs), and [wizard](../../src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor).

Regression references: [service tests](../../tests/PTDoc.Tests/Intake/IntakeServiceTests.cs), [HTTP/SQLite tests](../../tests/PTDoc.Tests/Integration/IntakeProfilePrefillIntegrationTests.cs), [wizard tests](../../tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs), [client transport test](../../tests/PTDoc.Tests/Intake/IntakeApiServiceTests.cs), and [sync protocol tests](../../tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs). No schema migration or bulk patient-data rewrite is required.

### Ambiguous decisions and verification limits

No retrieved finding remains unimplemented or needs a domain decision. **TRICARE remains a separate ambiguous compatibility extension:** `PatientInfoPage` offers `tricare`, but neither the intake payer selector nor `InsurancePayerType` defines it. Product/domain owners must choose first-class support or an existing category. The implementation does not guess a mapping.

Builds, tests, format gates and browser QA were **not run by the agent**, per `AGENTS.md`. `git diff --check` passed. Static review also confirmed that standalone access validates the current patient contact; the new demographic test obtains its OTP after the contact correction so it exercises submission instead of an expected authorization rejection.

The subsequent user-run `dotnet test --no-build` executed the **Debug** assembly and reported **2,416 passed, 4 failed, 0 skipped (2,420 total)**. Every failure was `StalePolicy_CanBeExplicitlyReconciledAndResubmitted` with `standalone: true`, covering both priorities and accept/omit choices. All four stopped at the refresh GET with HTTP 405, before reconciliation: the test reused `/api/v1/intake/access/{intakeId}`, which supports PUT but has no GET mapping. The production `IntakeApiService.GetLatestByPatientIdAsync` already uses `/api/v1/intake/access/patient/{patientId}/latest`. The test now uses the registered patient-scoped latest route for each access mode and asserts HTTP 200, intake/patient identity and a non-null profile offer before continuing. Existing reconciliation, resubmission and authorization-preservation assertions remain. This run reported no failures in the earlier SQLite reorder fixtures. The corrected refresh test still needs a rebuilt rerun; the remaining Release, format and browser verification is not established by this Debug result.

Rebuild and run the eight-case reconciliation matrix first (intentionally omit `--no-build`):

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug --filter 'FullyQualifiedName~StalePolicy_CanBeExplicitlyReconciledAndResubmitted' --verbosity normal -m:1 -p:BuildInParallel=false
```

### Exact verification commands

Run from the repository root. The test project builds the changed Core/Application/Infrastructure/API/Web/shared-UI graph. Run the focused tests first, then the CI categories and full suite. Release matches `.github/workflows/ci-core.yml`; Debug retains the user's local configuration.

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Debug --no-build --filter 'FullyQualifiedName~Intake|FullyQualifiedName~InsurancePolicyServiceTests|FullyQualifiedName~SyncClientProtocolTests' --verbosity normal
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release --no-build --filter 'Category=CoreCi' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release --no-build --filter 'Category=OfflineSync' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release --no-build --verbosity normal
```

Formatting gates (including shared UI, which changed):

```bash
dotnet format src/PTDoc.Core/PTDoc.Core.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Application/PTDoc.Application.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.UI/PTDoc.UI.csproj --no-restore --verify-no-changes --verbosity minimal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --verbosity minimal
```

Manual acceptance: with a synthetic patient, start an intake; correct chart demographics and replace a policy; confirm submission conflicts without losing answers; refresh and accept current insurance; resubmit and confirm the chart keeps its corrected demographics and the old authorization stays attached to its original policy. Repeat with an archived secondary policy and omission, including a standalone link. The new controls reuse existing theme classes.

### Threads eligible after successful verification and explicit authorization

All **eight currently open threads** become candidates for a reply and resolution after the changes are verified and published with authorization: `PRRT_kwDOQt-faM6wMs1y`, `PRRT_kwDOQt-faM6wMs2d`, `PRRT_kwDOQt-faM6w-CT7`, `PRRT_kwDOQt-faM6w-CUV`, `PRRT_kwDOQt-faM6w-CUk`, `PRRT_kwDOQt-faM6w-CUz`, `PRRT_kwDOQt-faM6w-CVJ`, and `PRRT_kwDOQt-faM6w-CVR`. Explain the overlap for the original policy thread and the existing fix for the compound-name thread. The Self Pay and legacy-reader threads are already resolved and require no further resolution. Summary references are duplicates and require no separate response. No GitHub reply, resolution, PR-level response, review request, commit or push was performed in this pass.

## Historical PR 162 review verification and local remediation

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
