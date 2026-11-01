> Archived planning snapshot, 2026-10-26, before implementation. Runtime/test statements describe that original planning session. Current implementation evidence lives in each plan record and the tracker. Local source paths were removed and Markdown hard-break spacing normalized for publication. Original SHA-256: `0095d0efd59b862be8c82e448222931f29a719ab8603e7c6a8440ba94eedf9dd`.

# PTDoc client review: evidence-grounded implementation plan

Analysis date: **October 26, 2026**. Meeting: **August 21, 2026**. Proposed work only; identifiers PR 01–36 below are planning identifiers, not existing GitHub pull-request numbers.

The pasted master prompt governs this task. The meeting, prior roadmap and PDF contents are evidence to reconcile, not instructions to execute. No repository code, branch, issue, pull request or deployment was changed.

## 1. Executive Summary

The confirmed work centers on reliable sessions and saving, simpler patient intake, useful empty-slot scheduling, coherent insurance/provider workflows, clinically distinct ROM/MMT entry, scored outcome questionnaires and clinician-reviewed note generation. Current source contains substantial foundations, but several visible controls are previews, incomplete integrations or shared paths that do not implement the requested distinctions.

**Coverage:** 95 distinct ledger entries: 94 meeting-derived intents/context/dependency entries and one supplied-asset validation entry (R095). Of these, 84 concern product work, decisions, materials or process obligations; 8 are context only, 2 are possible future ideas, and 1 groups unrelated conversation. These counts do not imply 95 approved product requirements. There are 59 confirmed-requirement entries, including the update-message process commitment.

**Proposed work:** 36 development/decision briefs: **19 Ready for Development**, **7 Needs Clarification**, **5 Blocked by Dependency**, and **5 Verification Required**. Ready means the bounded confirmed slice has enough definition to start; it does not mean tests passed, external integrations are configured, or prerequisites can be skipped. Decision briefs are not authorization to implement a guessed policy.

The highest-risk work is tenant/role ownership, session and save recovery, clinical metric migration, exact instrument scoring, signed-note provenance and fax destinations. No evidenced critical vulnerability or data corruption was established, so no item is assigned Critical merely from hypothetical risk. Architecture/subscription policy, sex/gender semantics, handoff/sign-off rules, patient-alert permissions and authorization behavior remain explicit decision gates.

The two open Settings PRs **[#153](https://github.com/BlackHouseDeveloper/PTDoc/pull/153)** and **[#154](https://github.com/BlackHouseDeveloper/PTDoc/pull/154)** must be reconciled before new role/Settings implementation. The supplied outcome PDFs satisfy the file-delivery request, but not instrument-version/scoring approval; the referenced discharge workflow document remains missing. Fax sandbox access and synthetic AI review examples are acceptance dependencies.

Major source corrections: the final intake rule includes **re-evaluations**; clinician-administered outcomes are an exception to patient-only entry; care-team **email was declined**; review editing may navigate/focus rather than require inline editing; Authorization Required placement remains contradictory; commercial signup was discussed but not approved. Third-party billing complaints do not create integration commitments, while the later explicit PTDoc zero-visit safeguard merits a separate decision.

**Overall confidence:** high for the explicit final workflow requests and inspected source facts; medium for transcript-only labels and unfinished policies; low for current deployed defect reproduction because no application session was exercised. No item is marked Already Addressed, Verified, Not Reproduced or Verified in Current Build without runtime evidence.

## 2. Source and Verification Statement

| Source | Reviewed material | SHA-256 prefix (16 characters) |
| --- | --- | --- |
| S01 | User request (authoritative task instructions) (supplied source file) | 86227d2dcc2ad37b |
| S02 | Complete Gemini meeting document (supplied source file) | 1ada1a6d0862ef7c |
| S03 | Prior 21-item roadmap (comparison, not approval) (supplied source file) | 5d070c5b36c3001a |
| S04 | Patient-measure archive: 12 PDFs (supplied source file) | 567fcbd6fc0169ac |
| S05 | Clinician-measure archive: 4 PDFs (supplied source file) | d65cacaf57801ac5 |

### What was actually inspected

- S02 was read in full: summary, all **45 Aligned entries (P0018–P0062)**, all **58 assigned next steps (P0069–P0126)**, detailed notes and the full transcript through its closing notice. Extraction contains 2,231 nonempty paragraphs. All 13 embedded screenshots were visually inspected. Transcript coverage runs from 00:01:29 to 03:03:07.
- All pages of all 16 PDFs were text-extracted and reviewed. Image-render inspection checked the QuickDASH formula, HOOS wording, FAAM sports rows, NDI layout, Oswestry appendix/body diagram and the blank second page of 5x sit-to-stand. The instrument inventory identifies content that cannot be treated as an approved specification.
- Repository source was inspected at **`0372ca446910322337c9f005f6aeddffcb51d348`**, detached HEAD, clean working tree, in `isolated PTDoc worktree`. Latest local commit: **Add clinic Settings persistence and migrations (#152)**. The parent history includes #151 through #145. This is a local source snapshot, not proof that beta runs this revision.
- Targeted repository documentation: architecture/layer and reference-data conventions, copilot instructions, development guidance, CI branching/deployment/test categories, FSD/TDD role/signature/AI rules, clinicrefdata status, fax integration guidance and the required PR template. SDK pin: **.NET 8.0.417**.
- Read-only GitHub lookup searched open issues/open PRs and reviewed the latest 100 PR search results, plus relevant bodies and current metadata for #153/#154. It found only unrelated open issue #20 and two open PRs at lookup time. This is not an exhaustive search of all historical closed work. PR diffs, review threads and CI logs were not inspected; no PR review or approval is claimed.
- No local Web/API host, beta page, authenticated session, database or runtime diagnostics was exercised. No build, .NET test suite, browser suite, migration, fax transmission or AI provider call ran. No live patient data was used for testing. Reported runtime defects therefore remain unverified.

### Source precedence and locator convention

Explicit later client decisions take precedence over earlier exploratory comments and Gemini’s compressed Aligned wording. Transcript speaker labels are **January Austin** (client/clinical workflow owner) and **Admin Black House Developers** (developer). Developer suggestions are not treated as client approval unless the exchange supports that conclusion. Quoted wording is used sparingly; most evidence is paraphrased.

`P####` is the extraction’s paragraph ordinal, preserving blank-paragraph offsets; it is **not a Word page number**. Timestamps are the meeting’s elapsed timestamps, not application event times. Source aliases in section 6 join repeated Aligned/next-step/detailed-note/transcript statements into one primary intent. A locator can span multiple speakers; the brief lists speaker labels actually present in those referenced paragraphs where available.

Gemini explicitly warns that the transcript is computer generated and editable. Misheard instrument names, speech interruptions, note-type switching and ambiguous pronouns are retained as uncertainty. Screenshots support meeting-observed presentation only; they cannot prove current backend behavior. Patient-like identifiers visible in those screenshots are intentionally omitted from this plan.

### GitHub overlap at inspection

| Existing PR | Verified metadata | Planning consequence |
| --- | --- | --- |
| [#153 — Implement Settings administration services and APIs](https://github.com/BlackHouseDeveloper/PTDoc/pull/153) | Open, not draft; head `497a6c1fc78dd44ce7c64aa1533ea943cf072034`; base `main` at local inspected SHA; GitHub reported mergeable | Coordinate role/Settings work. Its body’s older stack/base description is not used over current metadata. |
| [#154 — Connect Settings administration and authentication UI](https://github.com/BlackHouseDeveloper/PTDoc/pull/154) | Open draft; head `b6ffc8b89e66e1f9da97b947b2a9389ef6719691`; base `codex/settings-services-api` at `4ae59c42288e82dcb7deb49e2feda3a5dfdaf4ed`; GitHub reported not mergeable | Validate/reconcile this stack before new role or Settings fixes; its base snapshot differs from current #153 head. |

PR-body assertions, including test counts, were not independently verified. Merged titles such as #150 (clinical workspace), #140 (responsive work) and #144 (beta gate), and earlier related #118/#104/#91, are overlap clues rather than proof a meeting issue is resolved. Do not reopen broad replacement implementations from titles alone.

### Repository evidence register

“Repository-verified” below means inspected source, not a running build. Each link is pinned to the inspected commit. A source file can contain the proposed UI while its runtime behavior remains **Unable to Verify**.

| Evidence | Finding | Inspected source |
| --- | --- | --- |
| E01 | Session configuration is source evidence, not runtime renewal proof. | [src/PTDoc.Web/Program.cs:577](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Web/Program.cs#L577) |
| E02 | Conflict, retry, save and navigation recovery UI already exists. | [src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor:92](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor#L92) |
| E03 | Timeline converts UTC to local; clinic timezone is a separate modeled value. | [src/PTDoc.UI/Pages/PatientProfile.razor:285](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/PatientProfile.razor#L285); [src/PTDoc.Core/Models/Clinic.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Core/Models/Clinic.cs) |
| E04 | Settings role UI is a PT-only preview; current work overlaps open #153/#154. | [src/PTDoc.UI/Components/Settings/RolesPermissionsSettings.razor:82](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Settings/RolesPermissionsSettings.razor#L82) |
| E05 | Clinic/User.ClinicId exist; no organization aggregate found in inspected Core models. | [src/PTDoc.Core/Models/Clinic.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Core/Models/Clinic.cs); [src/PTDoc.Core/Models/User.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Core/Models/User.cs) |
| E06 | Registration requires an active clinic and returns PendingApproval. | [src/PTDoc.Infrastructure/Services/UserRegistrationService.cs:56](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/UserRegistrationService.cs#L56) |
| E07 | Draft initialization, delivery and projection are separate existing paths; empty default seed and locked-intake guard need attention. | [src/PTDoc.Infrastructure/Services/IntakeService.cs:79](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/IntakeService.cs#L79); [src/PTDoc.Infrastructure/Services/IntakeDeliveryService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/IntakeDeliveryService.cs); [src/PTDoc.Infrastructure/Services/IntakeDraftCanonicalizer.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/IntakeDraftCanonicalizer.cs); [src/PTDoc.Infrastructure/Services/IntakeDomainProjection.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/IntakeDomainProjection.cs); [src/PTDoc.UI/Services/NoteWorkspacePayloadMapper.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Services/NoteWorkspacePayloadMapper.cs); [src/PTDoc.Application/Services/IntakeResponseDraft.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/Services/IntakeResponseDraft.cs) |
| E08 | Wizard and progress component use a fixed five-step flow. | [src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor); [src/PTDoc.UI/Components/Intake/IntakeProgressBar.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/IntakeProgressBar.razor) |
| E09 | Review edit changes a step enum; body-view toggle is an existing component. | [src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor:587](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor#L587); [src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor); [src/PTDoc.UI/Components/Intake/Steps/BodyViewToggle.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Steps/BodyViewToggle.razor) |
| E10 | History/limitation components and runtime reference catalog exist. | [src/PTDoc.UI/Components/Intake/Cards/MedicalHistoryCard.razor:14](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Cards/MedicalHistoryCard.razor#L14); [src/PTDoc.UI/Components/Intake/Cards/FunctionalLimitationsCard.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Cards/FunctionalLimitationsCard.razor); [src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json) |
| E11 | Revocation controls and separated release/contact details exist; canonical consent packet must remain compatible. | [src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor:546](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor#L546); [src/PTDoc.Application/Intake/IntakeConsentPacket.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/Intake/IntakeConsentPacket.cs) |
| E12 | Chart editor is narrower than existing patient update API. | [src/PTDoc.UI/Components/Patients/Profile/PatientDemographicsCardEditable.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Patients/Profile/PatientDemographicsCardEditable.razor); [src/PTDoc.Api/Patients/PatientEndpoints.cs:276](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Api/Patients/PatientEndpoints.cs#L276); [src/PTDoc.UI/Pages/PatientProfile.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/PatientProfile.razor) |
| E13 | Provider approval/update paths exist; care team currently lacks fax entry. | [src/PTDoc.UI/Components/Settings/ProviderDirectorySettingsPanel.razor:66](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Settings/ProviderDirectorySettingsPanel.razor#L66); [src/PTDoc.Infrastructure/Services/ProviderDirectoryService.cs:100](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/ProviderDirectoryService.cs#L100); [src/PTDoc.UI/Components/Intake/Cards/CareTeamCard.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Cards/CareTeamCard.razor) |
| E14 | Policy, utilization, settings/details/history are separate panels; chart has duplicate entry points. | [src/PTDoc.UI/Pages/PatientInfo/PatientInfoPage.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/PatientInfo/PatientInfoPage.razor); [src/PTDoc.UI/Components/PatientInfo/PatientInsurancePoliciesPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/PatientInsurancePoliciesPanel.razor); [src/PTDoc.UI/Components/PatientInfo/VisitTrackingUtilizationPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/VisitTrackingUtilizationPanel.razor); [src/PTDoc.UI/Components/PatientInfo/AdditionalAuthorizationSettingsPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/AdditionalAuthorizationSettingsPanel.razor); [src/PTDoc.UI/Components/PatientInfo/AuthorizationDetailsPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/AuthorizationDetailsPanel.razor); [src/PTDoc.UI/Components/PatientInfo/AuthorizationReferralHistoryPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/AuthorizationReferralHistoryPanel.razor); [src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor:85](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor#L85) |
| E15 | Add Patient exposes Authorization Required and authorization number. | [src/PTDoc.UI/Components/AddPatientModal.razor:221](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/AddPatientModal.razor#L221) |
| E16 | Today clinician selection depends on appointment keys; empty slots lack handlers; API role list includes administrative roles. | [src/PTDoc.UI/Pages/Appointments.razor:1139](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/Appointments.razor#L1139); [src/PTDoc.UI/Components/Appointments/ClinicianScheduler.razor:46](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Appointments/ClinicianScheduler.razor#L46); [src/PTDoc.Api/Appointments/AppointmentEndpoints.cs:29](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Api/Appointments/AppointmentEndpoints.cs#L29) |
| E17 | Signed-note review lacks a fax action; Fax Center/integration already exist. | [src/PTDoc.UI/Components/Notes/SoapReviewPage.razor:483](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/SoapReviewPage.razor#L483); [src/PTDoc.UI/Pages/FaxCenter.razor:38](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Pages/FaxCenter.razor#L38); [src/PTDoc.Integrations/Services/HumbleFaxService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Integrations/Services/HumbleFaxService.cs); [docs/HUMBLE_FAX_WIBBI_INTEGRATIONS.md](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/docs/HUMBLE_FAX_WIBBI_INTEGRATIONS.md) |
| E18 | General user notification service is reusable; it does not settle handoff/signature semantics. | [src/PTDoc.Infrastructure/Services/UserNotificationService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Services/UserNotificationService.cs); [docs/PTDocs+_Master_FSD.md](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/docs/PTDocs+_Master_FSD.md) |
| E19 | Shared patient header and chart cards exist; alert authoring/tab was not found. | [src/PTDoc.UI/Components/Notes/PatientContextHeader.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/PatientContextHeader.razor); [src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor) |
| E20 | Daily chief-complaint carry-forward and numbered progress questionnaire exist; daily outcome composition needs runtime verification. | [src/PTDoc.UI/Components/Notes/Workspace/DailyTreatment/SubjectiveSection.razor:7](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/DailyTreatment/SubjectiveSection.razor#L7); [src/PTDoc.UI/Components/Notes/Workspace/ProgressNote/ProgressSubjectiveSection.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/ProgressNote/ProgressSubjectiveSection.razor); [src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor:206](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor#L206) |
| E21 | ROM fields and side exist; no explicit active/passive metric dimension was found. | [src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor); [src/PTDoc.Core/Models/ObjectiveMetric.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Core/Models/ObjectiveMetric.cs); [src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs) |
| E22 | Editor combines MMT/Joint Mobility; catalog already provides separate grade lists. | [src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor:688](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor#L688) |
| E23 | Aggregate definitions/results/scorers exist; intake asks for prior score rather than rendering per-item forms. | [src/PTDoc.Application/Data/OutcomeMeasureReferenceData.json](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/Data/OutcomeMeasureReferenceData.json); [src/PTDoc.Infrastructure/Outcomes/OutcomeMeasureRegistry.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Outcomes/OutcomeMeasureRegistry.cs); [src/PTDoc.Infrastructure/Outcomes/OutcomeMeasureService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Infrastructure/Outcomes/OutcomeMeasureService.cs); [src/PTDoc.Core/Models/OutcomeMeasureResult.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Core/Models/OutcomeMeasureResult.cs); [src/PTDoc.UI/Components/Intake/Steps/OutcomeMeasuresStep.razor:49](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Steps/OutcomeMeasuresStep.razor#L49) |
| E26 | PatientPersonalGoals exists in assessment, with no corresponding intake capture found. | [src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor:274](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor#L274) |
| E27 | Clinical narrative precedes contributing assessment inputs. | [src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor:19](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor#L19) |
| E28 | Several plan/summary generation actions use GeneratePlanOfCareAsync(BuildPlanRequest()). | [src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor:1029](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor#L1029); [src/PTDoc.AI/Services/ClinicalGenerationService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.AI/Services/ClinicalGenerationService.cs); [src/PTDoc.Application/AI/IAiClinicalGenerationService.cs](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.Application/AI/IAiClinicalGenerationService.cs) |
| E29 | Targeted source inspection did not establish an eight-goal cap or loss cause. | [src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor) |
| E30 | Progress questions/activity fields exist; percent-improved absent there; generic PlanTab has frequency support. | [src/PTDoc.UI/Components/Notes/Workspace/ProgressNote/ProgressSubjectiveSection.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/ProgressNote/ProgressSubjectiveSection.razor); [src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor) |
| E31 | Discharge already has some requested subjective fields; recommendations are free text. | [src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargeSubjectiveSection.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargeSubjectiveSection.razor); [src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargePlanSection.razor:49](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargePlanSection.razor#L49) |
| E32 | Discharge generation handlers only invoke change notification. | [src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargePlanSection.razor:222](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Notes/Workspace/DischargeNote/DischargePlanSection.razor#L222) |
| E33 | Settings/shared-shell overflow cause is unverified; coordinate current Settings UI stack. | [src/PTDoc.UI/Components/Settings/RolesPermissionsSettings.razor.css](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Settings/RolesPermissionsSettings.razor.css) |
| E34 | Basic intake currently labels the field Sex at Birth; full semantic/storage inventory remains open. | [src/PTDoc.UI/Components/Intake/Cards/BasicInfoCard.razor:23](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/Intake/Cards/BasicInfoCard.razor#L23) |
| E35 | Existing utilization/authorization UI is context; zero-visit runtime behavior was not verified. | [src/PTDoc.UI/Components/PatientInfo/VisitTrackingUtilizationPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/VisitTrackingUtilizationPanel.razor); [src/PTDoc.UI/Components/PatientInfo/AuthorizationDetailsPanel.razor](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/src/PTDoc.UI/Components/PatientInfo/AuthorizationDetailsPanel.razor) |

### Unavailable evidence and verification limits

The exact discharge workflow document/revision, approved instrument scoring supplements, a clinical assignment matrix, current role/sign-off decisions, authenticated beta release evidence, fax sandbox access, subscription commercial rules and reproducible synthetic AI quality examples were not supplied or verified. An accessible repository and connected GitHub search do not imply access to these materials. No deadline, effort estimate, regulatory conclusion or production-readiness claim is inferred.

## 3. Client Feedback Analysis

| Theme | Problem and supported direction / impact | Traceability | Evidence strength | Unresolved points |
| --- | --- | --- | --- | --- |
| Architecture, tenancy and ownership | Single-clinic foundations must support the proposed organization/clinic hierarchy without broadening access. Local owners should administer their clinicians; commercial hierarchy remains a proposal. | R001,R085; PR 05 | Medium: direct discussion, source clinic model verified. | Organization/clinic ownership, memberships, cross-clinic clinical access. |
| Authentication and save reliability | Active work and same-browser tabs should retain valid access; expired sessions need a clear recovery path. Save errors must not trap navigation or lose drafts. | R003,R005–R007,R048,R061; PR 01–03 | High report confidence; runtime cause unverified. | Deployment parity, actual activity renewal, anti-piggyback invariant, timeline display zone. |
| Roles and Settings | Role configuration and appointment visit types must persist under authorized administration; extra scrolling needs verification. | R002,R004,R037; PR 04,33 | Source preview verified; open Settings stack overlaps. | Integrated stack build, role matrix and current responsive reproducer. |
| Patient chart and intake usability | Reuse known demographics/policies, expose existing edits and entry actions, improve review focus and clear multi-select/body controls. | R008–R009,R013–R016,R018–R024,R028,R031,R047,R063; PR 07,09–12 | High explicit decisions; source wiring gaps verified. | Ambiguous legacy blank prefill and unidentified visual aside; sex/gender separate. |
| Note-type intake and personal goals | Full evaluation/re-evaluation, concise daily/progress/discharge questionnaires and review; carry patient personal goals into clinician-reviewed goal work. | R010,R062,R064,R074; PR 08,26 | High final decisions; fixed current steps verified. | Repeat encounters/old links; missing exact discharge content. |
| Scheduling | Display eligible clinician columns without bookings and make empty slots create prefilled appointments. PT/PTA eligibility must apply on server. | R032–R036; PR 16 | High final agreement, despite earlier “needs discussion” summary. | Off-duty roster and non-admin defaults; multi-role administrative users. |
| Insurance and authorization | Reduce duplicate entry and save fragmentation while preserving distinct policy/history meanings. | R029–R030,R044–R046,R089; PR 14,15,35 | High consolidation; contradictory initial field placement and exploratory zero-visit safeguard. | Authorization Required placement/Unknown semantics, historical edits and zero-visit policy. |
| Provider communication | Maintain directory entries and fax details, fax exact signed notes from review, and select clinic patients conveniently. | R023,R025–R027,R038–R042; PR 13,17,18 | High fax/directory request; handoff semantics incomplete. | Delete/archive and maintainer policy; handoff scope/sign-off; fax sandbox. |
| Patient clinical alerts | Patient-specific clinician alerts should remain visible in note headers. | R043; PR 19 | High feature intent; permission/lifecycle decisions absent. | Who can edit/retire others’ alerts and current alerts versus signed content. |
| Daily and evaluation documentation | Use concise daily pain/activity questions, no new daily outcomes, and put assessment narrative after contributing inputs. | R049–R059,R068–R069; PR 20,27 | High explicit list/order; duplicate/current-data complaints remain ambiguous. | Exact duplicate frequency field and unidentified current/prior issue. |
| ROM, MMT and joint mobility | Distinct movement/side/mode observations; normal before current; separate grading and selection-only carry-over. | R053–R057,R065; PR 21,22 | High decisions; combined current editor verified. | Legacy unknown mode, approved reference values and rare catalog exceptions. |
| Outcome measures | Patients answer approved items and system scores; clinician-administered tasks remain clinician input; carry results with provenance. | R011–R012,R066–R067,R095; PR 23–25,36 | High workflow intent; content/scoring incomplete for several supplied forms. | Exact variants, assignment, missing/unable rules, scoring examples and reproduction rights. |
| Progress and discharge | Use recorded and newly difficult activities, percent improvement and progress frequency; discharge follows the missing specific document. | R052,R075–R083,R094; PR 30–32 | High explicit additions; discharge vocabulary incomplete. | Progress question 8; approved discharge revision/options. |
| AI assistance | Generate section-specific SOAP and reviewable plan suggestions; diagnose goal quality/loss without assuming a literal eight-item cap. | R070–R073,R079,R083; PR 28,29,32 | Source shared/stub paths verified; output quality/runtime loss unverified. | Synthetic good/bad examples and clinician acceptance rubric. |
| Signup and process | Individual paid immediate access and organization demo/onboarding were favored but left for exploration. Developer sends update messages; client supplies materials. | R084–R086,R090–R094; PR 06 plus process actions | Medium commercial proposal; high process commitment. | Entitlements/payment failure and owner authority. No app-notification feature follows from update messages. |

### Daily question map: use labels, not an unexplained numeric list

The late exchange at 01:45:45–01:48:15 governs the **daily** questionnaire. The numbering below is cross-checked against the inspected progress component used as the discussion’s reference. These are label summaries, not permission to rewrite clinically approved wording. Progress deletions remain a separate question.

| Reference number | Question/topic | Daily decision |
| --- | --- | --- |
| 1 | Overall condition since initial evaluation | Keep |
| 2 | Making progress toward goals | Remove |
| 3 | Current pain, with existing best/worst detail | Keep |
| 4 | Pain change since last visit | Keep |
| 5 | Symptom frequency | Keep |
| 6 | Ability to perform daily activities | Keep |
| 7 | Activities improved / same / worse / newly difficult | Keep |
| 8 | How condition continues to affect ability | Remove |
| 9 | Return to previously avoided activities | Remove |
| 10 | HEP performed as prescribed | Keep |
| 11 | Home-program response/detail | Remove |
| 12 | Setbacks or new symptoms | Remove |
| 13 | Falls, ER visits or medical changes | Remove |
| Unnumbered | Additional information free text | Keep |

### Prior roadmap reconciliation

S03 is superseded as an implementation plan, while its useful grouping is preserved. It did not verify current source or runtime behavior. The following crosswalk prevents duplicate implementation and corrects overstatements.

| Old roadmap item | New destination | Correction |
| --- | --- | --- |
| 01 Sessions | 01 | Separate runtime diagnosis from assumed token-storage fixes. |
| 02 Roles/clinic boundaries | 04,05 | Do not merge role troubleshooting with an unapproved organization migration; coordinate open stack. |
| 03 Persistence | 07; ROM selection in 22 | Do not combine unrelated clinical metric mapping with patient intake prefill. |
| 04 Note-aware intake | 08 | Include re-evaluation and preserve repeat-encounter history. |
| 05 Review/layout | 09 | Exact component focus satisfies transcript; inline editing is not mandatory. |
| 06 History/consent/limitations | 10,11; identity decision 34 | Separate consent semantics and unresolved sex/gender policy. |
| 07 Personal goals | 26 | Reuse existing assessment field; add missing intake projection. |
| 08 Chart/lookup | 12,17 | Chart editing and fax patient lookup follow their own workflows. |
| 09 Directory/care team | 13 | Remove email; it was explicitly declined. |
| 10 Insurance | 14,15; session in 01 | Separate disputed Authorization Required from approved consolidation; auth-session tokens are unrelated to insurance. |
| 11 Scheduling | 16 | Enforce PT/PTA on server and preserve empty-grid behavior. |
| 12 Fax/forwarding | 17,18 | Signed fax is concrete; handoff/signatures require policy. |
| 13 Notifications | 19 | Feature intent confirmed; alert lifecycle/permissions need decision. |
| 14 Daily | 20 | Apply final explicit question map only to daily. |
| 15 ROM/MMT | 21,22 | Separate measurement representation from distinct grading/selection reuse. |
| 16 Outcomes | 23,24,25,36 | Version/scoring contract, patient forms, note projection and clinician administration are separate reviewable phases. |
| 17 Evaluation/SOAP | 27,28; goal diagnosis 29 | Do not bundle narrative layout with generation quality/loss. |
| 18 Progress | 30,32 | Newly difficult uses full relevant catalog; AI wiring separate. |
| 19 Discharge | 31,32 | Exact content is blocked by missing document; broken generation wiring is independently inspectable. |
| 20 Settings polish | 33; timeline 03 | Bound to evidenced scrollbar defect, not open-ended visual cleanup. |
| 21 Update notifications | Process action P01 | No software PR and no external message sent by this task. |

## 4. Proposed GitHub Work Items

### Shared implementation constraints and PR-template use

These are development briefs, using the requested brief structure. For an actual GitHub PR, retain the required repository template in [.github/pull_request_template.md](https://github.com/BlackHouseDeveloper/PTDoc/blob/0372ca446910322337c9f005f6aeddffcb51d348/.github/pull_request_template.md) and populate it from the relevant brief:

| Required repository section | Brief content to carry into it |
| --- | --- |
| Summary → Title / Description / Type of Change / Impact Areas | Title, user problem, final behavior, scope, source IDs and actual changed layers |
| Checklist → Core Requirements / Code Quality & Testing / Build Artifacts / Functional Testing / Platform-Specific Validation / Documentation & Communication | Definition of Done and verified architecture, test, platform, changelog and documentation evidence; leave unverified boxes unchecked |
| Testing Instructions → Setup / Functional Testing / Accessibility / Cross-Platform | Feature-specific acceptance steps, synthetic fixtures, required roles and supported clients |
| Verification Commands | Only relevant repository/workflow commands, with actual results supplied after execution |
| Review Feedback | Current thread-aware review dispositions when a real review is separately requested; no comments/replies were sent here |
| Additional Context | Source IDs, decisions, dependencies, migration/rollout risks and unresolved limitations |

**Technical Recommendation:** keep Core independent, Application dependent only on Core, Infrastructure implementing contracts, and composition in the appropriate roots. Preserve Web’s DB-stateless constraint and MAUI’s established offline storage/sync boundaries. Shared Blazor changes belong in PTDoc.UI; use explicit parameters, immutable parameter handling, ChildContent for wrappers and existing design tokens. Runtime reference catalogs, rather than reference-only clinicrefdata documents, drive application behavior. No new file placement is prescribed before architecture review.

**Technical Recommendation:** for each affected Web/shared MAUI workflow, verify save/reload, keyboard focus and supported narrow layouts. Backend rules must enforce clinical state, roles and clinic isolation. Signed notes and submitted intake evidence remain immutable. Add provider migrations only for demonstrated schema changes and verify each configured provider; deploy compatible readers before new payload writers where needed. Existing main/release/tag rules in CI.md remain authoritative.

All tests below are **proposed verification**, not completed checks. Per AGENTS.md, the implementer must ask the user to run heavy build/test commands and use their output unless separately authorized under the CI-reproduction exception. No commit is permitted without confirmed build/test results or explicit permission to commit without them. Update CHANGELOG at the end of a future repository-changing session. This artifact-only session needs no changelog entry.

For a decision brief, Definition of Done means the decision/specification is complete first; implementation, migration and test boxes cannot be checked until separately authorized code exists and passes. Each acceptance list is numbered here as AC1, AC2, etc. for its Open Questions references.

<a id="pr-01"></a>

### PR 01: Verify and repair active-use and multi-tab sessions

**Status:** Verification Required\
**Category:** Authentication and Session Management\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Web/API authentication; E01\
**Dependencies:** Coordinate with existing GitHub #153/#154; controlled Web/API build and synthetic accounts.\
**Source IDs:** R003, R005, R048, R061\
**Verification Status:** Unable to Verify

#### User Problem

Clinicians report being logged out while working, and a new tab can appear signed out or empty.

#### Confirmed Requirement

Active interaction should retain an otherwise valid session; a same-browser tab should share sign-in. Preserve the session protection the developer called anti-piggybacking. The client did not request unlimited sessions or bearer tokens in browser storage.

#### Background and Evidence

- **R003 — Reported Defect:** Local/beta screens and button behavior differ. Source: S02 Detailed notes/screenshots, Timestamped transcript; 07:23; 10:59; P0135,P0138,P0314–0362. Speakers: January Austin, Admin Black House Developers.
- **R005 — Reported Defect:** Expired sessions leave apparently active pages without data. Source: S02 Timestamped transcript; 12:00; P0357–0366. Speakers: January Austin, Admin Black House Developers.
- **R048 — Confirmed Requirement:** Prevent inactivity timeout during active app use. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:35:34; P0042,P0098,P0177,P1413–1414. Speakers: January Austin, Admin Black House Developers.
- **R061 — Confirmed Requirement:** New tab inherits browser sign-in without removing anti-piggyback/session protection. Source: S02 Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:00:24; P0108,P0185,P1805–1809. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Client-reported/meeting-observed failures. Repository-verified: Web cookie configuration has a 15-minute sliding expiry and an eight-hour cookie MaxAge; these settings alone do not prove active Blazor circuits renew correctly or enforce an absolute server deadline. No runtime reproduction.

#### Expected Behavior

Active users keep working across tabs. An expired/revoked session produces a clear sign-in transition instead of empty authenticated-looking screens. An explicit logout invalidates access across tabs.

#### Scope

Reproduce active editing, idle expiry, new-tab entry and stale-session recovery; repair only the demonstrated failure.

#### Out of Scope

Changing clinic policy durations, implementing subscription access, or weakening server authorization.

#### Implementation Guidance

**Technical Recommendation:** trace cookie, circuit and API credential lifetimes together in E01. Compare Web/API source SHA and release ID before debugging. Reuse the existing secure cookie/token design; coordinate with #153/#154 policy changes. Instrument reason codes without credentials; distinguish expected expiry from transport disconnect.

#### Data and Migration Considerations

No clinical migration expected. In-memory drafts need explicit recovery behavior coordinated with PR 02; never silently claim a failed save succeeded.

#### Security and Privacy Considerations

**Technical Recommendation:** keep HttpOnly/Secure protections, server revocation, idle and absolute policy checks; test two different users in separate browser profiles and reject cross-clinic requests.

#### Acceptance Criteria

- [ ] **AC1:** Given a valid session and repeated editing/navigation within its allowed lifetime, when the configured idle interval passes, then active work does not cause an unexplained logout.
- [ ] **AC2:** Given a signed-in browser, when a patient route opens in a second tab, then both tabs have the same permitted user/clinic context without a second login.
- [ ] **AC3:** Given explicit logout or server revocation, when either tab sends another protected request, then access is denied and the UI requests sign-in.
- [ ] **AC4:** Given an expired session, when a page loads, then it displays a recoverable authentication state instead of empty clinical controls.

#### Testing and Verification

Session integration tests and multi-tab browser tests with synthetic PT/Admin accounts; active circuit, idle tab, logout, refresh and API 401 cases. Record exact release and clocks. Run only after separately authorized implementation; no tests ran in this analysis.

#### Rollout and Regression Risks

Auth changes can affect all clients. Validate against #153/#154 first; rollback configuration/code together and never migrate credentials into browser-readable storage.

#### Open Questions

Engineering and security owner: identify the existing anti-piggyback invariant and activity-renewal contract before coding; reproduction can start now. Its preservation controls AC2–AC3.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** AC1–AC3 (activity/session invariant).

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-02"></a>

### PR 02: Recover from note-save errors without trapping navigation

**Status:** Verification Required\
**Category:** Bug Fix\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Note workspace and autosave recovery; E02\
**Dependencies:** PR 01 diagnosis; a safe draft plus failure/conflict fixtures.\
**Source IDs:** R006, R091\
**Verification Status:** Unable to Verify

#### User Problem

A reported three-banner error leaves clinicians unable to save or leave the note without logging out.

#### Confirmed Requirement

Investigate and fix the stuck-note/evaluation workflow. Reports at 12:59, 01:56:16 and 02:01:46 describe the same class of failure and form one brief.

#### Background and Evidence

- **R006 — Reported Defect:** Save-error banners block note/evaluation navigation and require relogin. Source: S02 Detailed notes/screenshots, Timestamped transcript; 12:59; 01:56:16; 02:01:46; P0139,P0183,P0375,P1732–1747,P1822–1825. Speakers: January Austin, Admin Black House Developers.
- **R091 — External Dependency:** Client captures screenshot/context if note-navigation error recurs. Source: S02 Timestamped transcript; 21:15–22:28; P0458–0471. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Client-reported and later recognized on screen. The developer alternately thought it was fixed and was uncertain. Repository-verified: retry, save-conflict, retained-draft and incomplete-navigation controls exist in E02; current failure not reproduced.

#### Expected Behavior

Users can retry a recoverable save or intentionally stay/leave with an explicit warning about unsaved data. A server conflict cannot silently overwrite a newer note.

#### Scope

Exercise error lifecycle, navigation guard and draft state; fix the blocking path if reproduced.

#### Out of Scope

Redesigning all validation, bypassing required clinical rules, auto-signing or unlocking signed notes.

#### Implementation Guidance

**Technical Recommendation:** fault-inject API 401/409/500 and disconnected responses around the existing autosave/navigation handlers. Keep a single authoritative save state; release busy state in failure paths, provide focused actionable error messages, and avoid replaying stale writes.

#### Data and Migration Considerations

No migration assumed. Preserve draft values, server concurrency version and signed-note hashes. Any destructive discard must be an explicit user action.

#### Security and Privacy Considerations

**Technical Recommendation:** sanitize errors, retain access checks after reauthentication and never recover a draft into a different patient or clinic.

#### Acceptance Criteria

- [ ] **AC1:** Given a writable evaluation draft and a failed save, when the error appears, then current unsaved inputs remain visible and retry/stay/navigation choices respond.
- [ ] **AC2:** Given a stale note version, when saving, then the newer server note is not overwritten and the user can inspect the conflict.
- [ ] **AC3:** Given explicit leave/discard after a warning, when navigation completes, then no hidden retry writes the abandoned draft.
- [ ] **AC4:** Given a signed note or lost edit permission, when recovery is attempted, then it cannot create an editable signed copy.

#### Testing and Verification

Unit tests of save-state transitions; component tests for banners and navigation; API concurrency tests; browser failure injection on Evaluation and Daily. Client supplies a redacted recurrence screenshot if needed.

#### Rollout and Regression Risks

Regression risk is accidental data loss or duplicate writes. Preserve old server versions and test both transient failures and real validation rejection.

#### Open Questions

QA/client: supply recurrence route, note state, timestamp and redacted error evidence if current fixtures do not reproduce it. Investigation can proceed; a code fix needs a reproducible failing case.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** Reproduction and recovery criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-03"></a>

### PR 03: Correct patient timeline timestamps using the agreed display time zone

**Status:** Verification Required\
**Category:** Bug Fix\
**Priority:** Medium\
**Confidence:** High\
**Affected Area:** Patient timeline; E03\
**Dependencies:** Synthetic note/intake/appointment timestamps; clinic time-zone decision.\
**Source IDs:** R007\
**Verification Status:** Unable to Verify

#### User Problem

The meeting showed timeline dates/times that did not match the participants’ expectation.

#### Confirmed Requirement

Investigate and correct patient clinical-info timeline timestamps. The meeting did not specify whether display follows clinic time, browser time or event location.

#### Background and Evidence

- **R007 — Reported Defect:** Patient timeline date/time appears incorrect. Source: S02 Detailed notes/screenshots, Timestamped transcript; 13:53–15:01; P0139,P0384–0399. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Meeting-observed discrepancy. Repository-verified: PatientProfile creates note, appointment and intake rows with DateTime.SpecifyKind(...Utc).ToLocalTime(); in a server-rendered host this warrants checking host-zone effects. It is a hypothesis, not a reproduced root cause.

#### Expected Behavior

Timeline ordering and displayed date/time represent the stored event consistently in the agreed zone, including date boundaries.

#### Scope

Timestamp source, DateTime kind and presentation conversion for the three timeline event types.

#### Out of Scope

Changing dates of service or rewriting historical event instants.

#### Implementation Guidance

**Technical Recommendation:** trace UTC values before presentation; distinguish LastModified, submitted and appointment-start events. Use existing clinic TimeZoneId if product selects clinic time; do not assume the developer’s workstation zone. Keep date-only clinical fields date-only.

#### Data and Migration Considerations

Normally a presentation fix. Investigate stored data before deciding on any migration; do not apply a blanket time offset.

#### Security and Privacy Considerations

**Technical Recommendation:** inspect synthetic events and record only identifiers/reason codes in logs.

#### Acceptance Criteria

- [ ] **AC1:** Given the same UTC event in two deployment host time zones, when displayed under the agreed product zone, then the visible date/time is identical.
- [ ] **AC2:** Given events near midnight or a daylight-saving transition, when sorted and rendered, then chronology and local dates remain correct.
- [ ] **AC3:** Given an intake without a submission timestamp, when shown in the timeline, then its documented fallback is used without inventing a completed event.

#### Testing and Verification

Unit conversion tests and browser comparison of synthetic timestamps against raw API values; cover note/intake/appointment, daylight-saving boundaries and date-only fields.

#### Rollout and Regression Risks

A display fix must not shift signed dates or persisted events. No data rewrite unless separately evidenced and reviewed.

#### Open Questions

January/product owner: should timeline timestamps use clinic time or viewer time? Diagnosis proceeds; final display acceptance depends on this answer.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** Timestamp display criterion.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-04"></a>

### PR 04: Validate role configuration and visit types against the existing Settings stack

**Status:** Blocked by Dependency\
**Category:** Permissions and Multi-Tenancy\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Settings Roles and Scheduling; E04\
**Dependencies:** Existing GitHub #153 (services) and #154 (connected UI); approved role matrix.\
**Source IDs:** R004, R037\
**Verification Status:** Partially Implemented

#### User Problem

Admins cannot configure roles, and the client needs configurable appointment visit types.

#### Confirmed Requirement

Troubleshoot role configuration and verify the promised visit-type Settings workflow. This is a verification/follow-up slice of existing work, not a second Settings implementation.

#### Background and Evidence

- **R004 — Reported Defect:** Admin Select Role to Configure is unusable. Source: S02 Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 08:47; P0070,P0136,P0331. Speakers: Admin Black House Developers.
- **R037 — Confirmed Requirement:** Configure additional visit types in Settings. Source: S02 Detailed notes/screenshots, Timestamped transcript; 01:04:20; P0168,P1089–1097; GitHub #153/#154. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: role controls explicitly show PT-only preview and nonpersisted selections. #153 is open; #154 is an open draft and GitHub reported it not mergeable. Their bodies describe the missing services/UI, but their code and tests were not reviewed here.

#### Expected Behavior

After the existing stack is integrated, authorized configuration persists across reload and appointment forms use saved clinic visit types; unsupported actions have a clear explanation.

#### Scope

Reconcile meeting cases with #153/#154 acceptance evidence; file a separate delta only for a demonstrated residual gap.

#### Out of Scope

New role privileges, organization ownership policy, unrelated kiosk/auto-check-in expansion, or duplicating the stack.

#### Implementation Guidance

**Technical Recommendation:** compare approved role capability definitions with API enforcement and Settings presentation. Test allowed and denied operations, stale versions and appointment compatibility. Use existing stack migrations; do not invent replacement settings tables.

#### Data and Migration Considerations

Role assignments, visit-type records and existing appointments may be affected by the stack. Verify old visit-type references continue to render and that role edits do not strand the last administrator.

#### Security and Privacy Considerations

**Technical Recommendation:** server permissions and clinic scope must match UI; selecting a role must not itself grant that role. Record non-PHI configuration audits.

#### Acceptance Criteria

- [ ] **AC1:** Given an authorized clinic administrator after stack integration, when selecting a configurable role, then its saved settings load and permitted changes survive a second session.
- [ ] **AC2:** Given an unauthorized role or different clinic, when sending the same mutation directly to the API, then it is denied.
- [ ] **AC3:** Given a new visit type saved in clinic A, when creating an appointment in A, then it is selectable; clinic B does not inherit it.
- [ ] **AC4:** Given an appointment with a historical/deactivated type, when opened, then its stored meaning is preserved.

#### Testing and Verification

Use stack unit/integration/bUnit and RBAC/Tenancy suites, plus Settings-to-appointment browser checks. PR-body test claims are not this analysis’s test evidence.

#### Rollout and Regression Risks

Merge/rebase conflicts and privilege drift are the main risks. Validate stack release parity before calling the meeting defects resolved.

#### Open Questions

Engineering owner: supply integrated stack commit and deployment evidence. Validation preparation can proceed, but completion depends on that build.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** All post-stack configuration criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-05"></a>

### PR 05: Decide organization and clinic ownership before migrating tenancy

**Status:** Needs Clarification\
**Category:** Permissions and Multi-Tenancy\
**Priority:** Needs Decision\
**Confidence:** Medium\
**Affected Area:** Organization/clinic membership and local clinician administration; E05\
**Dependencies:** Client hierarchy and role decision; coordinate #153/#154.\
**Source IDs:** R001, R085\
**Verification Status:** Conflicts With Current Implementation

#### User Problem

The current clinic-first model does not express the proposed organization owner managing multiple clinics and their staff.

#### Confirmed Requirement

The developer proposed organization above clinic; January wants a customer’s owner/admin to add its clinicians without platform-owner approval for every person. Exact hierarchy, cross-clinic access and approval authority remain unresolved.

#### Background and Evidence

- **R001 — Technical Work:** Multi-clinic support and persistence need a sound hierarchy. Source: S02 Detailed notes/screenshots, Timestamped transcript; 03:27–04:38; 02:45:29; P0132,P0270–0287,P2417–2437. Speakers: January Austin, Admin Black House Developers.
- **R085 — Proposed Feature:** Organization customers use demo/onboarding, then local owners add clinicians without platform-owner approval. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:41:02–02:45:29; P0200,P2368–2429. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: Clinic and User.ClinicId exist; the inspected Core model has no organization aggregate (OrganizationName is only provider-directory text). This is not evidence that tenant isolation is currently breached.

#### Expected Behavior

A decision brief establishes ownership, membership, invitations, cross-clinic access and conversion rules before schema work starts.

#### Scope

Document an explicit role/tenant matrix and migration design with single-clinic and multi-clinic examples.

#### Out of Scope

Automatic access to all clinics in an organization, platform-wide PHI access, billing implementation or schema mutation in this planning task.

#### Implementation Guidance

**Technical Recommendation:** retain clinic data boundaries while adding organization relationships only after approval. Analyze null/legacy clinic IDs, background jobs, sync/export/fax scopes and claims refresh. Choose additive backfill and validation rather than silently changing every ClinicId meaning.

#### Data and Migration Considerations

Potential migration of clinics, users and role relationships; patient/note ownership must remain stable. Signed records must not be reassigned as a side effect of subscription or organization setup.

#### Security and Privacy Considerations

**Technical Recommendation:** explicitly separate platform administration, organization administration and clinical access. No implicit parent-organization access to clinical records.

#### Acceptance Criteria

- [ ] **AC1:** Given a proposed organization with two clinics, when the client reviews the role matrix, then permitted patient access and staff administration are explicitly recorded for each role.
- [ ] **AC2:** Given legacy single-clinic users and records, when a migration rehearsal runs after approval, then every record retains a verifiable owning clinic and no access broadens unintentionally.
- [ ] **AC3:** Given an administrator from another organization, when accessing clinic records or memberships, then access is denied.

#### Testing and Verification

First validate decision examples with January and engineering. After approval, add provider-migration, claims, RBAC/Tenancy, sync and rollback tests using two organizations and three synthetic clinics.

#### Rollout and Regression Risks

Highest architectural risk: accidental access expansion and destructive tenant remapping. Block schema implementation until ownership decisions are explicit.

#### Open Questions

January with engineering/security: define organization versus clinic owner, multi-clinic staff membership, who can invite/approve which clinician, and cross-clinic patient access. All schema/access criteria depend on this; existing single-clinic UI work can proceed.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All ownership/migration criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-06"></a>

### PR 06: Decide individual subscription and organization onboarding rules

**Status:** Needs Clarification\
**Category:** Needs Clarification\
**Priority:** Needs Decision\
**Confidence:** Medium\
**Affected Area:** Registration/subscription lifecycle; E06\
**Dependencies:** PR 05 decisions; subscription provider, entitlement and payment-failure rules.\
**Source IDs:** R084, R086\
**Verification Status:** Conflicts With Current Implementation

#### User Problem

An individual buyer should not wait for the platform owner to manually approve use, while organizations need a different onboarding path.

#### Confirmed Requirement

January favored immediate paid single-provider EMR access, no added staff and no billing bundle; organization customers would use a demo/onboarding flow. The discussion ended with further exploration, so it does not approve a production entitlement flow.

#### Background and Evidence

- **R084 — Proposed Feature:** Individual paid subscribers should get immediate access, EMR-only, without adding staff. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:37:14–02:43:22; P0015,P0200,P2315–2396. Speakers: January Austin, Admin Black House Developers.
- **R086 — External Dependency:** Client explores and confirms organization/signup alternatives. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:46:34; 02:52:58; P0202,P2443–2448,P2565. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: UserRegistrationService requires an active clinic and returns PendingApproval. No subscription entitlement implementation was verified. The existing copay payment PR is not subscription infrastructure.

#### Expected Behavior

A signed-off decision defines registration, payment confirmation, access, limits, organization enrollment and account recovery before code is scoped.

#### Scope

Decision and integration brief with example success/failure journeys; keep individual versus organization paths explicit.

#### Out of Scope

Choosing prices, payment provider, trials, tax policy, recurring-charge terms or deadlines.

#### Implementation Guidance

**Technical Recommendation:** separate identity, tenant membership and paid entitlements; never grant access solely from a browser success redirect. Reuse registration validation and server-side authorization; specify idempotent provisioning and failure recovery after product decisions.

#### Data and Migration Considerations

Existing pending registrations, memberships and approved users need an explicit compatibility policy. Do not auto-approve existing users during migration.

#### Security and Privacy Considerations

**Technical Recommendation:** no card data in PTDoc logs; use the selected provider’s approved payment flow. Paid access cannot bypass clinic isolation or clinician scope.

#### Acceptance Criteria

- [ ] **AC1:** Given a proposed individual signup, when the decision is approved, then its payment trigger, immediate-access boundary and staff limit are unambiguous.
- [ ] **AC2:** Given failed or duplicated payment/provisioning events, when the eventual implementation processes them, then it neither grants unpaid access nor creates duplicate organizations.
- [ ] **AC3:** Given an existing pending registration, when onboarding changes roll out, then its approved migration rule is applied without silently broadening access.

#### Testing and Verification

Product decision walkthrough first; later provider sandbox, entitlement state-machine, duplicate-event, registration validation and tenant-isolation tests.

#### Rollout and Regression Risks

Commercial and access-control risks are unresolved; no payment or production-access work is ready.

#### Open Questions

January/business owner: confirm whether the favored proposal is approved, who approves organization setup, payment provider/entitlement lifecycle, and how existing users move. All implementation depends on these answers.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All entitlement/onboarding criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-07"></a>

### PR 07: Prefill intake and preserve patient and policy data across entry flows

**Status:** Ready for Development\
**Category:** Data Migration or Persistence\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Patient creation, appointment patient entry, intake seeding; E07,E12,E15\
**Dependencies:** Existing clinic-scoped patient/policy contracts; PR 15 affects only authorization-field placement.\
**Source IDs:** R020, R031\
**Verification Status:** Partially Implemented

#### User Problem

Patients repeat known details and information entered during creation does not reliably reach the chart or intake.

#### Confirmed Requirement

Carry applicable Add Patient/Add Appointment demographics and primary/secondary policy information into the patient profile and created/sent intake, including previously sent editable intakes that lack known information.

#### Background and Evidence

- **R020 — Confirmed Requirement:** Prefill known demographics/insurance into created and sent intakes. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 43:01; P0024,P0078,P0152,P0784–0791. Speakers: January Austin, Admin Black House Developers.
- **R031 — Confirmed Requirement:** Carry applicable Add Patient/Add Appointment data into profile and primary/secondary policies. Source: S02 Assigned next steps, Timestamped transcript; 54:37; 01:26:25; P0086,P0953,P1319–1322. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: IntakeService accepts an optional seed but otherwise constructs an empty draft. Existing patient update and policy contracts exist. End-to-end propagation and every caller were not runtime verified.

#### Expected Behavior

New, resumed and previously sent editable intakes expose applicable known patient/policy information while preserving patient answers and clinician corrections.

#### Scope

Trace each creation/delivery path and map existing demographics and policy fields; validate initial save, delivery, resume and reload.

#### Out of Scope

ROM-to-MMT mapping, automatic copying of old clinical answers, subscriber eligibility decisions or authorization placement.

#### Implementation Guidance

**Technical Recommendation:** reuse IntakeDeliveryService, IntakeDraftCanonicalizer, IntakeDomainProjection and NoteWorkspacePayloadMapper. Seed new drafts once. For existing sent editable drafts, fill only provably untouched fields; when legacy blank provenance is unknown, offer known profile values for explicit confirmation instead of silently overwriting an intentional blank. Preserve answers, concurrency versions and submitted/locked records.

#### Data and Migration Considerations

Preserve policy identity, primary/secondary designation and provenance. No new patient master schema is assumed. Distinguish never-answered from intentionally cleared values before backfilling editable drafts; unknown legacy blanks require confirmation. Locked intakes remain immutable.

#### Security and Privacy Considerations

**Technical Recommendation:** obtain patient and policies using the intake’s clinic and authorized patient link; never accept a client-provided patient ID as proof of access.

#### Acceptance Criteria

- [ ] **AC1:** Given a patient with saved address, phone and two policies, when a new authorized intake is created or sent, then applicable fields match those saved values and policy order.
- [ ] **AC2:** Given an intake with edited answers, when reopened, then initialization does not overwrite those answers.
- [ ] **AC3:** Given Add Patient or appointment entry succeeds, when the profile and a new intake open, then the entered patient data is retained after reload.
- [ ] **AC4:** Given a patient in another clinic, when an intake seed request references that patient, then access is denied.
- [ ] **AC5:** Given a previously sent editable intake missing known demographics or policy data, when reopened, then untouched fields receive the known information or ambiguous legacy blanks offer it for confirmation without replacing existing answers.

#### Testing and Verification

Mapping/unit tests plus API integration for create, deliver, resume and conflict; one synthetic patient per clinic, two policies, blank optional values and conflicting draft edits.

#### Rollout and Regression Risks

Accidental stale overwrite or policy duplication. Roll out with mapping comparison and reject ambiguous writes.

#### Open Questions

Product/engineering: confirm any fields deliberately excluded from prefill; ordinary saved demographics and policies are approved scope. Clinical history reuse is excluded.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** AC1 and AC5 for deliberately excluded or ambiguous legacy fields.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-08"></a>

### PR 08: Make intake content and repeat encounters follow the destination note type

**Status:** Ready for Development\
**Category:** Intake\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Intake orchestration and note routing; E07,E08\
**Dependencies:** PR 07 seed rules; PR 20/30/31 define corresponding questionnaires; PR 24 provides outcome forms.\
**Source IDs:** R010, R062, R074\
**Verification Status:** Partially Implemented

#### User Problem

Patients are shown irrelevant full intake sections and intake responses do not consistently align with the intended note.

#### Confirmed Requirement

Full history/pain flow remains for evaluation and re-evaluation. Daily uses its short questionnaire and review; progress/discharge use their questionnaire, outcomes and review. All completed intakes retain a review step.

#### Background and Evidence

- **R010 — Confirmed Requirement:** Restrict full history/pain intake; later retain full evaluation/re-evaluation flow. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 20:15; 02:04:17–02:05:31; P0019,P0050,P0072,P0109–0110,P0441–0445,P1851–1869. Speakers: January Austin, Admin Black House Developers.
- **R062 — Reported Defect:** Older intake/limitations controls appeared missing then reappeared. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:06:42–02:10:47; P0188,P1882–1946. Speakers: January Austin, Admin Black House Developers.
- **R074 — Confirmed Requirement:** Keep intake questionnaires aligned with destination note subjective questions. Source: S02 Timestamped transcript; 17:57; 02:25:38; P0421–0423,P2132–2133. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: IntakeProgressBar has five fixed steps and wizard navigation is fixed. IntakeService reuses an unlocked patient draft and rejects a new draft if any locked intake exists; repeat-encounter behavior needs an explicit supported path. Some older controls reappeared during the meeting.

#### Expected Behavior

A new intake is associated with its intended encounter/note type, shows only applicable steps and routes its answers into that note’s matching subjective data.

#### Scope

Add explicit note-type-aware step definitions and safe repeat-encounter lifecycle; preserve prior submissions and review history.

#### Out of Scope

Removing pain questions from the daily questionnaire, suppressing re-evaluation history, or guessing the missing discharge questionnaire.

#### Implementation Guidance

**Technical Recommendation:** share a versioned questionnaire/step definition between intake and destination note. Replace patient-wide locked-intake exclusion with an authorized encounter-specific rule only after tracing delivery/link contracts. Keep already issued links tied to their original schema/version.

#### Data and Migration Considerations

Likely additive encounter/type/version linkage and projection changes; assess migrations across configured providers. Historical signed/locked responses must remain renderable. Do not rewrite them to the latest questionnaire.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce patient-link scope, note ownership and submission state server-side; a URL type change cannot reveal other questionnaires or records.

#### Acceptance Criteria

- [ ] **AC1:** Given a new evaluation or re-evaluation intake, when opened, then full approved history/pain sections and review are available.
- [ ] **AC2:** Given a daily intake, when opened, then its short questionnaire and review appear without the full history/pain modules or new outcome entry.
- [ ] **AC3:** Given a progress intake, when completed, then its questionnaire and approved outcomes reach the matching progress note.
- [ ] **AC4:** Given a prior locked intake and a valid later encounter, when creating the next intake, then the prior record remains unchanged and the new encounter can receive a distinct intake.
- [ ] **AC5:** Given an existing issued link, when the definition changes, then its saved answers still render under the issued version.

#### Testing and Verification

Note-type matrix tests, repeat-encounter API tests, resume/review mapping, old-link compatibility and mobile-width navigation. Discharge activation waits for PR 31’s source; other types can ship independently.

#### Rollout and Regression Risks

Existing links and clinical projections can regress. Introduce by note type and keep version-compatible rendering.

#### Open Questions

Engineering: verify exact current repeat-intake paths and deployed SHA before implementation. January supplies discharge content separately; it blocks that type only.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Discharge activation and repeat-encounter criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-09"></a>

### PR 09: Make intake navigation, review editing and body-map controls clearer

**Status:** Ready for Development\
**Category:** UI/UX\
**Priority:** Medium\
**Confidence:** High\
**Affected Area:** Intake progress, review and body-map controls; E08,E09\
**Dependencies:** Coordinate dynamic steps with PR 08.\
**Source IDs:** R009, R013, R018, R019, R093\
**Verification Status:** Partially Implemented

#### User Problem

Patients cannot easily recognize the body-map toggle or locate the specific item selected from review; progress labels wrap awkwardly.

#### Confirmed Requirement

Keep progress labels on one row, simplify confusing intake instructions, make front/back switching visibly actionable, and have Review Edit navigate/focus the exact component.

#### Background and Evidence

- **R009 — Confirmed Requirement:** Keep intake review progress labels in one row. Source: S02 Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 19:06; P0071,P0142,P0430; screenshot P0143. Speakers: Admin Black House Developers.
- **R013 — Confirmed Requirement:** Simplify/remove confusing patient intake instructions. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 29:49; P0021,P0074,P0147,P0579–0593. Speakers: January Austin, Admin Black House Developers.
- **R018 — Confirmed Requirement:** Make front/back body-map switching visibly actionable. Source: S02 Assigned next steps, Timestamped transcript; 30:46–31:49; P0076,P0608,P0613–0615. Speakers: January Austin, Admin Black House Developers.
- **R019 — Confirmed Requirement:** Review Edit should navigate and focus the exact component. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 40:23–41:25; P0023,P0077,P0151,P0756–0771. Speakers: January Austin, Admin Black House Developers.
- **R093 — Needs Clarification:** Brief visual comments about white bars/selected-body illumination/pain-severity position lack a clear requested change. Source: S02 Timestamped transcript; 01:26:25; P1323. Speakers: January Austin.

#### Current Behavior

Repository-verified: review edit emits only a step enum and wizard changes steps; it does not identify a target component. Screenshots show wrapped labels and current control styling; current responsive behavior was not tested.

#### Expected Behavior

Review Edit opens the proper step with the intended section visible and focused; progress remains readable and body orientation controls communicate selection.

#### Scope

Component-targeted edit events, focus/scroll behavior, approved concise instruction copy and responsive single-row progress treatment.

#### Out of Scope

True inline editing as a required design; speculative changes to white bars, selected-body illumination or pain-severity position from the garbled aside.

#### Implementation Guidance

**Technical Recommendation:** pass a stable section target in review navigation, focus a useful heading/input after render, and respect reduced motion. Reuse token CSS and accessible buttons. A horizontal overflow treatment may preserve a single row on small screens without shrinking text excessively.

#### Data and Migration Considerations

No clinical schema change; stable target identifiers must survive step ordering changes.

#### Security and Privacy Considerations

**Technical Recommendation:** focus/navigation must not expose hidden clinician-only or unrelated patient content.

#### Acceptance Criteria

- [ ] **AC1:** Given Review Edit for a specific section, when activated by mouse or keyboard, then that section is visible and receives useful focus.
- [ ] **AC2:** Given a narrow screen, when progressing through intake, then labels stay in one navigable row without clipping required actions.
- [ ] **AC3:** Given either body orientation, when the toggle is activated, then selection is visibly and accessibly communicated and markings persist.
- [ ] **AC4:** Given existing answers, when using review navigation, then answers are retained without an unintended submission.

#### Testing and Verification

Focused component and browser checks at phone/tablet/desktop widths, keyboard navigation and focus; compare approved copy with source decisions.

#### Rollout and Regression Risks

Focus timing and overflow can harm mobile usability. Verify with dynamic steps before release.

#### Open Questions

January: identify the exact control and desired appearance for the brief white-bar/body-highlight/pain-placement aside if still relevant. That clarification does not block the agreed navigation work.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Only an additional visual change if later approved; agreed criteria are independent.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-10"></a>

### PR 10: Clarify medical history, medications and functional limitations

**Status:** Ready for Development\
**Category:** Intake\
**Priority:** Medium\
**Confidence:** High\
**Affected Area:** MedicalHistoryCard and FunctionalLimitationsCard; E10\
**Dependencies:** PR 08 intake type visibility; existing reference catalog ownership.\
**Source IDs:** R014, R015, R016, R022, R063\
**Verification Status:** Partially Implemented

#### User Problem

Clinical jargon and collapsed options make intake choices harder to understand and complete.

#### Confirmed Requirement

Use Past Medical History, make multiple selection clear, provide Other/free entry, expand limitation groups initially, and use Difficulty with in app-authored limitation wording.

#### Background and Evidence

- **R014 — Confirmed Requirement:** Rename comorbidities to past medical history. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 30:46–34:52; P0022,P0075,P0598–0600,P0647. Speakers: January Austin, Admin Black House Developers.
- **R015 — Confirmed Requirement:** Make medication and medical-history multi-selection explicit. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 31:49–34:52; P0022,P0075,P0620,P0628,P0633,P0645–0647. Speakers: January Austin, Admin Black House Developers.
- **R016 — Confirmed Requirement:** Allow Other/free entry for missing medical-history options; medication Other is also in aligned wording. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 33:27–34:52; P0022,P0075,P0633,P0647–0651. Speakers: January Austin, Admin Black House Developers.
- **R022 — Confirmed Requirement:** Expand functional limitation groups initially. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 45:03–46:16; P0026,P0080,P0154,P0831–0842. Speakers: January Austin, Admin Black House Developers.
- **R063 — Confirmed Requirement:** Use Difficulty with instead of Unable in app-authored functional limitations. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 02:09:50; P0051,P0111,P1919–1927. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: Comorbidities remains a label; selectable catalogs and broad notes exist. The exact Other association and default expansion need verification. Employment/imaging controls were found during the meeting and are not newly missing requirements.

#### Expected Behavior

Patients see understandable labels, can choose multiple applicable entries, supply a missing item and scan expanded body-region limitation choices.

#### Scope

Labels/instructions, Other value binding, default expanded state and app-authored limitation copy.

#### Out of Scope

Rewording validated outcome instruments, changing diagnoses or adding duplicate employment/imaging fields.

#### Implementation Guidance

**Technical Recommendation:** retain stable catalog IDs while changing presentation text. Use explicit multi-select instructions and bind Other text to its section. Reuse runtime WorkspaceReferenceCatalog rather than treating clinicrefdata notes as production data.

#### Data and Migration Considerations

Preserve existing selected IDs and historical free text. Map renamed labels without replacing stored clinical meaning.

#### Security and Privacy Considerations

**Technical Recommendation:** treat free text as patient health information; avoid analytics payloads and validate normal text limits server-side.

#### Acceptance Criteria

- [ ] **AC1:** Given medical history or medications with multiple applicable entries, when several are selected and saved, then all survive reload.
- [ ] **AC2:** Given an unlisted item, when Other text is entered, then it appears under the correct section in review and the clinician view.
- [ ] **AC3:** Given a new intake limitation step, when loaded, then relevant groups are expanded initially and app-authored choices use Difficulty with.
- [ ] **AC4:** Given a standardized questionnaire, when these labels change, then its approved item wording is unchanged.

#### Testing and Verification

Binding/reload tests and keyboard multi-selection; legacy selected values, blank Other text, multiple body regions and review projection.

#### Rollout and Regression Risks

Changing labels must not orphan historical IDs or alter instrument validity.

#### Open Questions

January: review final plain-language copy and Other placement during acceptance; no new clinical catalog is assumed.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Copy/Other presentation criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-11"></a>

### PR 11: Group PHI release information and remove intake revocation controls

**Status:** Ready for Development\
**Category:** Intake\
**Priority:** Medium\
**Confidence:** High\
**Affected Area:** ReviewStep and canonical consent packet; E11\
**Dependencies:** Existing consent packet schema and approved text.\
**Source IDs:** R021, R047\
**Verification Status:** Partially Implemented

#### User Problem

Related PHI-release details are separated and the intake exposes unwanted revocation controls.

#### Confirmed Requirement

Place release consent with authorized contacts/additional details and remove the revoke controls from this intake workflow.

#### Background and Evidence

- **R021 — Confirmed Requirement:** Group PHI release consent with authorized contacts/additional details. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 44:01–45:03; P0025,P0079,P0153,P0800–0822. Speakers: January Austin, Admin Black House Developers.
- **R047 — Confirmed Requirement:** Remove intake consent-revocation controls. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:33:47; P0041,P0097,P0176,P1400–1404. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: consent status and contacts are separated in ReviewStep, and revocation checkboxes exist. The canonical packet also represents consent state; removing UI is not permission to erase historical revocations.

#### Expected Behavior

Patients review the relevant release choice and its contact details together; this intake no longer offers the identified revocation controls.

#### Scope

Layout and input binding changes, with compatible display of existing packets.

#### Out of Scope

Changing legal consent text, eliminating all organizational revocation processes, deleting historical revocation facts or inventing compliance policy.

#### Implementation Guidance

**Technical Recommendation:** reuse IntakeConsentPacket and existing validation; remove only the identified entry controls. Preserve backend interpretation of historical values and make review output truthful.

#### Data and Migration Considerations

No destructive migration. Existing revoked records retain their state and evidence; do not convert them to consent by omission.

#### Security and Privacy Considerations

**Technical Recommendation:** consent changes require current authorized intake context and existing audit/provenance behavior. Do not log contact details.

#### Acceptance Criteria

- [ ] **AC1:** Given a patient completing release consent, when reviewing it, then the release choice and authorized-contact details appear together.
- [ ] **AC2:** Given a new intake, when reaching consent, then the removed revoke controls are absent.
- [ ] **AC3:** Given a historical revoked packet, when viewed after rollout, then its recorded state remains unchanged and is not presented as affirmative consent.

#### Testing and Verification

New, resumed and historical packet rendering; consent/contact persistence and required-field validation.

#### Rollout and Regression Risks

Omitted controls can accidentally reset booleans. Test serialization defaults and historical records explicitly.

#### Open Questions

Clinic policy owner: confirm the separate authorized revocation process remains available where required; no broader policy change is included.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Historical consent criterion and workflow documentation.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-12"></a>

### PR 12: Complete patient chart editing and patient-entry actions

**Status:** Ready for Development\
**Category:** Enhancement\
**Priority:** High\
**Confidence:** High\
**Affected Area:** PatientProfile and demographics editor; E12\
**Dependencies:** Existing patient update API; PR 07 for intake propagation.\
**Source IDs:** R008, R024, R028\
**Verification Status:** Partially Implemented

#### User Problem

Existing patient fields cannot all be corrected from the chart, and primary entry actions are hard to reach.

#### Confirmed Requirement

Edit existing basic patient information including address, phone and emergency contact; expose Add Patient from the patient page; scroll/focus the note-type choices after Start New Note.

#### Background and Evidence

- **R008 — Confirmed Requirement:** Start New Note should scroll/focus the note-type choices. Source: S02 Detailed notes/screenshots, Timestamped transcript; 23:31; P0486; screenshot P0146. Speakers: Admin Black House Developers.
- **R024 — Confirmed Requirement:** Make all existing basic patient fields editable, including address/phone/emergency contact. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 48:08–49:49; P0028,P0082–0083,P0157,P0873–0891. Speakers: January Austin, Admin Black House Developers.
- **R028 — Confirmed Requirement:** Expose Add Patient on the patient page. Source: S02 Assigned next steps, Timestamped transcript; 52:23; P0086,P0922–0924. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: editable card exposes name/DOB/email while PatientEndpoints supports phone/address/emergency updates. Start New Note reveals a chooser; no explicit focus behavior was found. No Add Patient action was found on PatientProfile.

#### Expected Behavior

Authorized users can update all already-supported basic fields, create a patient from the relevant chart/list context and immediately reach note-type choices.

#### Scope

Connect existing fields and actions with validation, save/reload and meaningful focus.

#### Out of Scope

New clinical identity categories, sex/gender decisions, or new authorization fields.

#### Implementation Guidance

**Technical Recommendation:** reuse existing update DTO/version handling and AddPatientModal. Keep [Parameter] inputs immutable and use shared UI tokens. Route a newly created patient through existing ownership and duplicate-validation behavior.

#### Data and Migration Considerations

No new schema assumed. Preserve omitted fields on patch/update and handle concurrent edits without silently overwriting another user.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce clinic-scoped update/create permission at the API; contact fields and DOB must not enter UI telemetry.

#### Acceptance Criteria

- [ ] **AC1:** Given an authorized user editing phone, address and emergency contact, when saving and reloading, then all changes persist and unchanged fields remain intact.
- [ ] **AC2:** Given invalid required data or a version conflict, when saving, then a clear error preserves the user’s edits.
- [ ] **AC3:** Given the patient page, when Add Patient is selected, then the existing creation workflow opens and saves a clinic-scoped patient.
- [ ] **AC4:** Given Start New Note, when activated, then the chooser is in view and keyboard focus reaches its choices.

#### Testing and Verification

API binding/concurrency checks plus component/browser create-edit-reload and focus cases; denied update from another clinic.

#### Rollout and Regression Risks

Partial updates may clear omitted fields; creation from a chart must not accidentally edit the current patient.

#### Open Questions

Product owner: confirm whether Add Patient belongs on profile, patient list, or both; the meeting supports exposure on the patient page. This is a placement review, not a new workflow decision.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Add Patient placement criterion.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-13"></a>

### PR 13: Edit provider-directory entries and carry fax details into care teams

**Status:** Ready for Development\
**Category:** Provider Communication\
**Priority:** High\
**Confidence:** High\
**Affected Area:** ProviderDirectorySettingsPanel, ProviderDirectoryService, CareTeamCard; E13\
**Dependencies:** Existing provider update and approval policy; coordinate Settings stack.\
**Source IDs:** R023, R026\
**Verification Status:** Partially Implemented

#### User Problem

Clinicians need to correct/remove provider entries and reuse fax information instead of repeatedly typing it.

#### Confirmed Requirement

Replace the approval/rejection workflow with edit/delete and carry selected provider details, including fax, into care-team intake fields. Email was explicitly declined.

#### Background and Evidence

- **R023 — Confirmed Requirement:** Replace provider approval/rejection with edit/delete. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 47:11–48:08; P0027,P0081,P0155,P0859–0872. Speakers: January Austin, Admin Black House Developers.
- **R026 — Confirmed Requirement:** Add care-team fax and carry selected provider details into intake. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 52:23–53:28; P0029,P0085,P0915–0919,P0930–0934. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: directory UI has Approve/Reject, service has candidate update and approval/merge operations, and care-team UI has phone but no fax.

#### Expected Behavior

Authorized directory maintainers can edit or remove an entry from future selection; selecting a provider populates the appropriate saved contact/fax details.

#### Scope

Directory action redesign, existing-record compatibility and care-team fax propagation.

#### Out of Scope

Care-team email, automatic external provider verification or retroactive mutation of signed notes.

#### Implementation Guidance

**Technical Recommendation:** reuse existing update contracts. Implement delete as archive/deactivation when an entry has clinical references; confirm that visible behavior satisfies the client. Preserve candidate/merge provenance instead of bypassing server permission checks.

#### Data and Migration Considerations

Existing pending/approved entries need a transition rule; referenced entries retain historical names/fax snapshots. Add fax projection only if existing contracts lack it.

#### Security and Privacy Considerations

**Technical Recommendation:** restrict maintenance to approved existing roles, validate fax format and clinic visibility, and audit changes without exposing unnecessary patient details.

#### Acceptance Criteria

- [ ] **AC1:** Given an authorized maintainer, when correcting a provider and reloading, then the edited details persist.
- [ ] **AC2:** Given a removed referenced provider, when selecting a provider for a new intake, then it is unavailable while old clinical records remain readable.
- [ ] **AC3:** Given selection of a provider with a fax number, when opening intake review, then the correct details are populated and retained.
- [ ] **AC4:** Given an unauthorized user, when directly invoking directory mutation, then the API denies it.

#### Testing and Verification

Directory update/archive/merge compatibility, permission checks and intake selection/reload tests.

#### Rollout and Regression Risks

Hard deletion could break referrals and fax history; transition pending entries deliberately.

#### Open Questions

January and security owner: approve directory-maintainer roles and whether archive satisfies Delete. Existing authorized editing can proceed; destructive deletion cannot.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Directory removal and mutation-permission criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-14"></a>

### PR 14: Consolidate insurance policy and authorization editing

**Status:** Ready for Development\
**Category:** Insurance and Authorization\
**Priority:** High\
**Confidence:** High\
**Affected Area:** PatientInfo panels and chart links; E14\
**Dependencies:** Existing policy/authorization version contracts; PR 15 decides Authorization Required placement.\
**Source IDs:** R044, R045, R046\
**Verification Status:** Partially Implemented

#### User Problem

Duplicate entry points and scattered forms make policy and authorization information difficult to maintain consistently.

#### Confirmed Requirement

Remove the duplicate Insurance & Authorization entry; place visit limit and calendar/plan-year fields with policy entry; remove duplicate adjuster fields; combine related authorization/PCP/referral details and history with one save.

#### Background and Evidence

- **R044 — Confirmed Requirement:** Remove duplicate Insurance & Authorization entry below chart tabs. Source: S02 Assigned next steps, Timestamped transcript; 01:19:58; P0094,P1254. Speakers: Admin Black House Developers.
- **R045 — Confirmed Requirement:** Move visit limit/calendar or plan year into policy entry and remove duplicate adjuster fields. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:24:19–01:25:21; P0038,P0095,P0174,P1288–1311. Speakers: January Austin, Admin Black House Developers.
- **R046 — Confirmed Requirement:** Combine authorization/PCP/referral settings, details and editable history; save once. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:28:13–01:33:47; P0040,P0096,P0175,P1332–1399. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: separate policy, utilization, additional-settings, details and history panels exist, plus duplicate chart entry points.

#### Expected Behavior

Users edit each policy and its related authorization/referral information in a coherent workflow and receive one clear save result.

#### Scope

UI consolidation and an atomic or explicitly coordinated save of already-supported fields; editable history subject to existing record rules.

#### Out of Scope

Changing benefit rules, conflating historical and active authorizations, or resolving the disputed Add Patient field by assumption.

#### Implementation Guidance

**Technical Recommendation:** retain distinct domain records behind the unified UI. Reuse existing service contracts; if one-save requires orchestration, implement it server-side in the appropriate Application/Infrastructure boundary with concurrency validation.

#### Data and Migration Considerations

Preserve primary/secondary policy links, historical validity dates, utilization and referral provenance. No destructive table merge is implied. Migrate only if contract gaps are demonstrated.

#### Security and Privacy Considerations

**Technical Recommendation:** API permission and clinic ownership apply to every child record; reject forged policy/authorization IDs and audit substantive edits.

#### Acceptance Criteria

- [ ] **AC1:** Given a patient chart, when accessing insurance, then a single intended chart entry opens the consolidated workflow.
- [ ] **AC2:** Given a policy with visit limit and plan-year information, when saved once and reloaded, then all intended changes persist without duplicate adjuster fields.
- [ ] **AC3:** Given multiple authorizations and referrals, when editing one historical entry, then unrelated records and utilization remain unchanged.
- [ ] **AC4:** Given a validation/concurrency failure in one section, when saving, then the UI does not report a fully successful save or silently lose edits.

#### Testing and Verification

Policy/authorization mapping, multi-record concurrency, partial-failure and historical-record tests; responsive one-save workflow.

#### Rollout and Regression Risks

Consolidation may accidentally combine separate clinical/business meanings. Compare before/after synthetic records.

#### Open Questions

January: confirm which historical fields remain editable versus corrected through a new entry. Existing immutability/audit rules remain the default.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Historical edit criterion.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-15"></a>

### PR 15: Resolve authorization-field placement in patient and policy entry

**Status:** Needs Clarification\
**Category:** Insurance and Authorization\
**Priority:** High\
**Confidence:** Medium\
**Affected Area:** AddPatientModal and policy editor; E15\
**Dependencies:** Client decision C01; PR 14 policy consolidation.\
**Source IDs:** R029, R030\
**Verification Status:** Unable to Verify

#### User Problem

Initial registration asks for authorization details before benefits verification, but later discussion appears to reintroduce Authorization Required.

#### Confirmed Requirement

Removing the authorization number from initial Add Patient is explicit. Authorization Required is contradictory: early removal versus later Yes/No discussion in a new-policy context that the developer restated as Add Patient.

#### Background and Evidence

- **R029 — Confirmed Requirement:** Remove authorization number from initial Add Patient. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 54:37; P0030,P0087,P0950–0953. Speakers: January Austin, Admin Black House Developers.
- **R030 — Needs Clarification:** Resolve removal versus later addition of Authorization Required in Add Patient/policy. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 54:37 versus 01:26:25–01:28:13; P0030,P0039,P0087,P0095,P0946–0953,P1323–1332. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: AddPatientModal currently includes Authorization Required and authorization number. That matches neither a settled final placement nor proof the client changed their mind.

#### Expected Behavior

An approved field-placement matrix states what is collected at initial patient creation versus verified policy entry, including Unknown versus Yes/No.

#### Scope

Decision and then a small field-placement/UI-contract change; preserve stored authorization values.

#### Out of Scope

Inventing benefit-verification rules, defaulting unknown to No or deleting existing authorization records.

#### Implementation Guidance

**Technical Recommendation:** implement the uncontested number removal as an independent sub-slice if desired; keep the disputed toggle gated until answered. Do not overload missing JSON with a false value.

#### Data and Migration Considerations

No data deletion. Preserve existing unknown/yes/no semantics and old numbers in their authoritative records.

#### Security and Privacy Considerations

**Technical Recommendation:** retain authorization-edit permissions and provenance of verified versus patient-entered information.

#### Acceptance Criteria

- [ ] **AC1:** Given an approved placement matrix, when a new patient is entered, then only approved initial fields appear.
- [ ] **AC2:** Given unknown benefit status, when a patient is created, then the saved state is not silently converted to No.
- [ ] **AC3:** Given an existing authorization number, when entry fields change, then the historical value remains available in its authorized policy/authorization view.

#### Testing and Verification

After decision, form/API nullability tests and existing-record compatibility; compare new-patient and policy-entry flows.

#### Rollout and Regression Risks

Wrong defaults can misrepresent coverage. Do not ship a guessed toggle placement.

#### Open Questions

January: should Authorization Required appear in Add Patient, in the new-policy form after verification, or both; should Unknown remain available? This blocks the toggle change, not PR 14.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All field-placement/default criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-16"></a>

### PR 16: Create appointments from empty clinician slots and enforce schedulable roles

**Status:** Ready for Development\
**Category:** Scheduling\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Appointments, ClinicianScheduler and appointment API; E16\
**Dependencies:** Existing clinic schedules and visit types; coordinate PR 04.\
**Source IDs:** R032, R033, R034, R035\
**Verification Status:** Partially Implemented

#### User Problem

Empty schedules hide useful clinician columns and users cannot create a prefilled appointment from a blank slot.

#### Confirmed Requirement

Admin Today shows clinician columns, Week can focus a clinician, empty slots remain usable, and selecting a slot prepopulates clinician/date/time. Schedulable clinicians are PT/PTA. Horizontal scrolling was accepted.

#### Background and Evidence

- **R032 — Requested Enhancement:** Admin Today shows clinician columns; Week can select a clinician. Source: S02 Detailed notes/screenshots, Timestamped transcript; 55:44–01:04:20; P0014,P0162,P0971–0995,P1027,P1088–1089. Speakers: January Austin, Admin Black House Developers.
- **R033 — Confirmed Requirement:** Click empty appointment slot to prefill date/time/clinician. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 59:24–01:00:20; P0031,P0088,P0164,P1027–1036. Speakers: January Austin, Admin Black House Developers.
- **R034 — Confirmed Requirement:** Restrict schedulable choices to PT and PTA. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 01:00:20; P0032,P0089,P1044–1047. Speakers: January Austin, Admin Black House Developers.
- **R035 — Confirmed Requirement:** Show clinician/hour slots without existing bookings. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:01:48–01:02:32; P0033,P0090,P0166,P1060–1073. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: Today filters clinicians by visible appointments; empty slot elements have no interaction; API schedulable-role list includes several administrative roles beyond PT/PTA.

#### Expected Behavior

Eligible clinicians are shown independently of bookings, empty slots launch the appointment form and UI/API agree on eligible assignees.

#### Scope

Empty-grid population, accessible slot action, prefill and server-enforced role filtering.

#### Out of Scope

Schedule faxing, drag-and-drop, availability optimization, or assuming every administrator is a treating clinician.

#### Implementation Guidance

**Technical Recommendation:** derive columns from authorized clinic roster and schedule rules, not appointment keys. Use clinic time-zone conversion and existing appointment validation. Permit an admin who also has an eligible clinical role only under the approved role matrix.

#### Data and Migration Considerations

No bulk reassignment of old appointments. Historical appointments with now-ineligible assignees remain readable; future edits follow an explicit compatibility rule.

#### Security and Privacy Considerations

**Technical Recommendation:** reject ineligible/cross-clinic clinician IDs at API boundaries; UI filtering alone is insufficient.

#### Acceptance Criteria

- [ ] **AC1:** Given a day with no bookings and eligible clinicians, when Admin opens Today, then usable clinician/time columns are visible.
- [ ] **AC2:** Given an empty slot, when activated by click or keyboard, then the form has the slot’s clinician/date/time and allows completion.
- [ ] **AC3:** Given Week view, when an individual clinician is selected, then that clinician’s schedule appears without requiring a pre-existing booking.
- [ ] **AC4:** Given an admin-only or other-clinic assignee, when submitting directly to the API, then creation is rejected.
- [ ] **AC5:** Given a daylight-saving boundary, when selecting a slot, then the saved instant and displayed clinic time agree.

#### Testing and Verification

Roster/role API tests and empty-day, mixed-roster, keyboard, narrow-width, week-filter and time-zone browser checks.

#### Rollout and Regression Risks

Historical administrative bookings and multi-role users need compatibility review; preserve old records.

#### Open Questions

January: confirm off-duty clinician visibility and default view for non-admin roles. Core empty-slot behavior can proceed using existing availability rules.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Empty-column/default-view examples and multi-role eligibility.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-17"></a>

### PR 17: Fax signed notes from review and improve clinic patient selection

**Status:** Ready for Development\
**Category:** Provider Communication\
**Priority:** High\
**Confidence:** High\
**Affected Area:** SoapReviewPage, FaxCenter and existing fax integration; E17\
**Dependencies:** Existing HumbleFax configuration and signed-note export; sandbox credentials for release verification.\
**Source IDs:** R025, R038\
**Verification Status:** Partially Implemented

#### User Problem

Users must leave the note review workflow to fax it and Fax Center patient selection is cumbersome.

#### Confirmed Requirement

Provide fax composition equivalent to Fax Center from a signed note; offer current-clinic patient choices in Fax Center.

#### Background and Evidence

- **R025 — Confirmed Requirement:** Fax Center patient lookup should offer current-clinic patient choices. Source: S02 Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 51:13; P0084,P0159,P0907. Speakers: Admin Black House Developers.
- **R038 — Confirmed Requirement:** Fax a signed note from review using fax-center-equivalent composition. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:05:40–01:07:44; P0034–0035,P0091,P0169,P1108–1144. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: review has export/submit but no fax action. Fax Center has a text search and result selection with a limited result count; signed-note fax infrastructure already exists.

#### Expected Behavior

An authorized user can open existing fax composition with the signed note selected, verify destination and explicitly send. Patient selection supports finding any eligible clinic patient.

#### Scope

Reuse fax composition from review; replace/improve lookup with a searchable, paginated selector.

#### Out of Scope

Multi-note fax bundles (exploratory), automatic sending, schedule faxing or a new fax vendor.

#### Implementation Guidance

**Technical Recommendation:** reuse canonical signed-document rendering and existing fax service/status tracking. Use search-backed paging rather than loading every patient into the browser. Keep destination review and explicit Send.

#### Data and Migration Considerations

Preserve transmission/document identifiers and audit history. A note edit/version change must not substitute an unsigned draft after selection.

#### Security and Privacy Considerations

**Technical Recommendation:** verify patient/note/clinic permissions and recipient before enqueueing; protect fax content and credentials; use synthetic documents in sandbox tests.

#### Acceptance Criteria

- [ ] **AC1:** Given an authorized user reviewing a signed note, when choosing Fax, then composition opens with that exact signed version and patient context.
- [ ] **AC2:** Given a draft/unsigned or inaccessible note, when attempting fax through UI or API, then it is not sent.
- [ ] **AC3:** Given a patient beyond the first result page, when searched in Fax Center, then the correct clinic-scoped patient can be selected.
- [ ] **AC4:** Given explicit Send with a verified destination, when the provider accepts or fails the request, then status and retry behavior are clear without duplicate dispatch.

#### Testing and Verification

Mocked provider success/failure/idempotency, signed-version/permission tests and sandbox delivery using synthetic content. Production recipient tests are excluded.

#### Rollout and Regression Risks

Wrong-recipient and duplicate-send risk; preserve explicit confirmation and traceable status.

#### Open Questions

Integration owner supplies sandbox access and test destination before end-to-end acceptance. Development can use mocks.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Provider delivery/failure criterion at sandbox acceptance.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-18"></a>

### PR 18: Define clinician handoff and shared-treatment sign-off

**Status:** Needs Clarification\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** Medium\
**Affected Area:** Note handoff, PT/PTA roles and notifications; E18\
**Dependencies:** Approved PT/PTA sign-off matrix; existing note lifecycle and notification services.\
**Source IDs:** R041, R042\
**Verification Status:** Unable to Verify

#### User Problem

Clinicians sharing treatment need to send work to each other and know who should complete or sign it.

#### Confirmed Requirement

Add Send to between PT/PTA with a notification for each forwarded note. Shared-treatment completion/sign-off was discussed, but ownership, editable states and signature responsibilities were not settled.

#### Background and Evidence

- **R041 — Needs Clarification:** Support PTA/PT shared treatment completion and sign-off responsibilities. Source: S02 Detailed notes/screenshots, Timestamped transcript; 01:08:58–01:11:16; P0171,P1155–1182. Speakers: January Austin, Admin Black House Developers.
- **R042 — Confirmed Requirement:** Add Send to between PT/PTA and a notification for each forwarded note. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:12:54–01:14:26; P0036,P0092,P0172,P1191–1203. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: notification services exist and note signing has established role/lifecycle rules. No note handoff action was found; no approved new co-sign policy was verified.

#### Expected Behavior

A decision specifies sender/recipient scope, what is handed off, workflow status and who may edit/sign, then a bounded implementation adds notification-backed handoff.

#### Scope

Decision matrix and later same-clinic note handoff using existing notification infrastructure.

#### Out of Scope

Expanding PTA signing permissions, cross-clinic forwarding, sending an entire chart by default or treating notification as clinical acceptance.

#### Implementation Guidance

**Technical Recommendation:** model handoff separately from signatures and maintain immutable signed versions. Use idempotent server commands and notification creation; decide reassignment versus collaboration explicitly.

#### Data and Migration Considerations

Potential handoff event/state persistence with sender/recipient/note version. Do not overwrite original authorship.

#### Security and Privacy Considerations

**Technical Recommendation:** recipient eligibility and note access must be server enforced; notification previews should minimize patient information.

#### Acceptance Criteria

- [ ] **AC1:** Given an approved handoff matrix, when an eligible clinician forwards an eligible note, then the intended recipient receives one notification linked to the correct note.
- [ ] **AC2:** Given a duplicate submission, when retried, then it does not create duplicate handoffs or notifications.
- [ ] **AC3:** Given a recipient without clinic/note permission, when selected or posted directly, then handoff is denied.
- [ ] **AC4:** Given a signed record, when handed off, then its signature and content remain immutable.

#### Testing and Verification

After decision, state-transition, role/tenancy, notification idempotency and stale-note-version tests; PT-to-PTA and PTA-to-PT examples.

#### Rollout and Regression Risks

Incorrect sign-off semantics can compromise clinical accountability. Keep this blocked on decisions.

#### Open Questions

January/clinical owner: is Send to reassignment, collaboration or review; which note states and recipient roles are allowed; who signs shared visits and whether co-sign is required? These block implementation.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All handoff/sign-off criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-19"></a>

### PR 19: Add clinician-authored patient alerts to chart and note headers

**Status:** Needs Clarification\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Patient chart Notifications tab and shared note header; E19\
**Dependencies:** Existing patient permissions and shared header; lifecycle acceptance by clinical owner.\
**Source IDs:** R043\
**Verification Status:** Unable to Verify

#### User Problem

Clinicians need important patient-specific reminders visible while documenting instead of buried in the chart.

#### Confirmed Requirement

Add a clinician-authored Notifications tab and show persistent patient alerts in the note header.

#### Background and Evidence

- **R043 — Confirmed Requirement:** Clinician-authored patient Notifications tab with persistent note-header alerts. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:16:05–01:21:20; P0037,P0093,P0173,P1212–1262. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: patient header shows identity context and some chart flags are read-only. No matching authoring tab was found. General user notifications exist but do not establish this patient-alert feature.

#### Expected Behavior

Authorized clinicians can maintain patient alerts and see applicable alerts consistently across note types.

#### Scope

Patient-alert create/edit/display and a clear active/inactive lifecycle using existing clinician access boundaries.

#### Out of Scope

Automated diagnosis alerts, externally sent messages, AI risk prediction or replacing handoff inbox notifications.

#### Implementation Guidance

**Technical Recommendation:** keep patient clinical alerts distinct from user inbox delivery. Reuse shared header to avoid inconsistent visibility and define authorship/timestamp/version fields with server validation.

#### Data and Migration Considerations

Likely additive clinic/patient-linked alert persistence; preserve prior text or audit history on edit/deactivation. Do not migrate unrelated flag meanings without mapping.

#### Security and Privacy Considerations

**Technical Recommendation:** patient access controls apply to alert text; avoid sensitive text in push/email previews or analytics. No cross-clinic retrieval.

#### Acceptance Criteria

- [ ] **AC1:** Given an authorized clinician, when saving a patient alert, then it appears in that patient’s Notifications tab and note headers after reload.
- [ ] **AC2:** Given a different patient, when opening a note, then the previous patient’s alert is absent.
- [ ] **AC3:** Given a deactivated alert, when opening a new note, then active presentation follows the agreed lifecycle while history remains accessible to authorized users.
- [ ] **AC4:** Given a patient or unauthorized user, when attempting authoring directly, then existing clinician restrictions are enforced.

#### Testing and Verification

CRUD/version, clinic isolation and shared-header tests across evaluation/daily/progress/discharge, plus long-text and narrow-screen rendering.

#### Rollout and Regression Risks

Overly broad or stale alerts can overwhelm documentation. Keep initial scope manual and patient-specific.

#### Open Questions

January/clinical owner: decide author/edit/deactivate rights (including another clinician’s alert), active versus historical display, and whether signed-note views show current alerts separately from immutable content. A UI/data prototype can proceed, but persistence/permission implementation and AC1/AC3/AC4 require this decision.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** AC1, AC3, AC4.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-20"></a>

### PR 20: Implement the agreed daily subjective and objective scope

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Daily SubjectiveSection and objective composition; E20\
**Dependencies:** PR 08 shares questionnaire definition; existing progress question labels provide mapping.\
**Source IDs:** R049, R050, R051, R058\
**Verification Status:** Partially Implemented

#### User Problem

Daily documentation contains redundant material and does not collect the concise pain/activity update the client requested.

#### Confirmed Requirement

Remove daily chief-complaint carry-forward, put pain/body-region details first, use appropriate selections plus free additional information, keep questions 1/3/4/5/6/7/10 and remove 2/8/9/11/12/13 from the referenced set. Remove new functional outcome entry from daily objective.

#### Background and Evidence

- **R049 — Confirmed Requirement:** Use appropriate multi-select/dropdowns in daily subjective, retaining free-text additional information. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:36:54–01:40:56; P0043,P0099,P0178,P1429,P1468–1476. Speakers: January Austin, Admin Black House Developers.
- **R050 — Confirmed Requirement:** Replace daily chief-complaint carry-forward with top-of-form pain details/body-region data. Source: S02 Assigned next steps, Timestamped transcript; 01:38:09–01:39:15; P0099–0100,P1452–1475. Speakers: January Austin, Admin Black House Developers.
- **R051 — Confirmed Requirement:** Daily question set keeps 1,3,4,5,6,7,10; removes 2,8,9,11,12,13. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:45:45–01:48:15; P0044,P0101,P0179,P1572–1621. Speakers: January Austin, Admin Black House Developers.
- **R058 — Confirmed Requirement:** Remove new functional outcome entry from daily objective. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:54:55; P0049,P0107,P0182,P1720–1723. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: daily chief-complaint carry-forward exists; the progress component contains the numbered 13-question set used in discussion. ObjectiveTab contains an outcome panel; final daily route visibility needs verification.

#### Expected Behavior

Daily note and corresponding patient questionnaire use the agreed compact question set, with no new routine outcome collection in daily objective.

#### Scope

Use the explicit label mapping in section 3; adjust daily-only composition, inputs and persistence.

#### Out of Scope

Applying daily deletions to progress, removing all daily pain questions or deleting historical outcome results.

#### Implementation Guidance

**Technical Recommendation:** store stable question IDs rather than numeric positions. Bind multi-select choices without losing free text; guard daily-only visibility at composition level and preserve old payload rendering.

#### Data and Migration Considerations

Old daily answers remain readable. New schema/version maps retired fields without clearing historical signed notes; shared components must not drop progress values.

#### Security and Privacy Considerations

**Technical Recommendation:** retain note ownership, draft/signing rules and patient-link scope for the matching intake.

#### Acceptance Criteria

- [ ] **AC1:** Given a new daily note, when opening subjective, then pain/body-region input precedes the agreed seven questions and free additional information remains available.
- [ ] **AC2:** Given the approved question map, when saving/reloading, then every kept response persists and removed questions are absent from new daily entry.
- [ ] **AC3:** Given daily objective, when entering treatment data, then no new routine outcome questionnaire/result entry is offered.
- [ ] **AC4:** Given progress or evaluation, when daily composition changes, then their independently approved fields are unaffected.

#### Testing and Verification

Question-definition/unit tests, daily intake-to-note mapping, old payload compatibility, save/reload and note-type regression matrix.

#### Rollout and Regression Risks

Numeric question references can be misapplied; verify labels against the captured version before editing.

#### Open Questions

January: confirm final labels during acceptance; the later explicit daily list governs. The earlier progress-question-8 ambiguity is excluded.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Question-map criterion.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-21"></a>

### PR 21: Represent ROM by movement, side and active/passive measurement

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** ObjectiveTab and ObjectiveMetric/workspace contracts; E21\
**Dependencies:** Approved region/movement catalog and existing metric persistence; coordinate PR 22.\
**Source IDs:** R053, R054, R055, R057\
**Verification Status:** Partially Implemented

#### User Problem

ROM entry does not provide the side and active/passive comparisons needed for clinical documentation.

#### Confirmed Requirement

Show relevant movements for selected body regions while allowing additional regions; show normal before current; support applicable left/right comparisons and active/passive current, normal and prior measurements. Spine does not acquire artificial left/right sides.

#### Background and Evidence

- **R053 — Confirmed Requirement:** ROM shows selected-region movements and permits switching/additional regions. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:49:45–01:51:29; P0045,P0102,P0180,P1636–1666. Speakers: January Austin, Admin Black House Developers.
- **R054 — Confirmed Requirement:** Show normal value before current value. Source: S02 Assigned next steps, Timestamped transcript; 01:51:29; P0103,P1668–1669. Speakers: January Austin, Admin Black House Developers.
- **R055 — Confirmed Requirement:** Support left/right comparisons for paired regions, without artificial spine sides. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 01:51:29–01:54:55; P0046,P0104,P1670–1687,P1720. Speakers: January Austin, Admin Black House Developers.
- **R057 — Confirmed Requirement:** ROM has active/passive current, normal and prior values for each applicable side. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 01:53:46–01:54:55; P0048,P0106,P1702–1717. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: region filtering already exists, current appears before normal, metrics support Side but not an explicit active/passive dimension. Current/prior/normal text fields exist.

#### Expected Behavior

Each ROM observation has unambiguous region/movement/side/mode context and preserves current/prior values without conflation.

#### Scope

Extend the existing metric representation and editor; reuse current region filtering where it already satisfies the request.

#### Out of Scope

Inventing normal ranges, inferring active/passive values from legacy data, or interpreting spine side exception as removal of active/passive.

#### Implementation Guidance

**Technical Recommendation:** use explicit stable dimensions and units; preserve unknown mode for old records. Coordinate typed metrics and workspace JSON mapping, export and sync. Reference values require approved catalog provenance.

#### Data and Migration Considerations

Potential additive metric mode/side mapping across provider migrations and JSON snapshots. Never duplicate a legacy value into both active and passive as measured facts.

#### Security and Privacy Considerations

**Technical Recommendation:** maintain clinic/note scope and draft/signature immutability; no patient identifiers in diagnostics.

#### Acceptance Criteria

- [ ] **AC1:** Given a paired region, when entering left/right active/passive measurements, then each value survives save/reload in its exact cell.
- [ ] **AC2:** Given spine, when entering ROM, then no artificial bilateral requirement is imposed and applicable active/passive data remains representable.
- [ ] **AC3:** Given a selected region plus another added region, when switching between them, then movements and saved values remain correct.
- [ ] **AC4:** Given a legacy metric without mode, when displayed, then it is not falsely labeled as both active and passive.
- [ ] **AC5:** Given a ROM row, when viewed, then approved normal/reference information precedes current measurement.

#### Testing and Verification

Metric round-trip, migration, JSON/typed/export consistency, bilateral/spine/multi-region, missing units and stale-write tests.

#### Rollout and Regression Risks

Clinical mislabeling and legacy data duplication are primary risks. Use additive migration and verify representative old notes.

#### Open Questions

Clinical owner: supply/approve normal-value sources and region-specific applicability where the existing catalog is incomplete; structural work can proceed without inventing values.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Reference-value display and applicable-region cases.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-22"></a>

### PR 22: Separate MMT from joint mobility and reuse ROM movement selections

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** ObjectiveTab, movement catalog and objective contracts; E21,E22\
**Dependencies:** PR 21 shared movement identifiers; approved MMT/joint grading definitions.\
**Source IDs:** R056, R065\
**Verification Status:** Partially Implemented

#### User Problem

Combined strength and joint-mobility controls obscure different measurement meanings and force duplicate movement selection.

#### Confirmed Requirement

Give MMT and joint mobility separate sections/grades; seed MMT movement selections from ROM-selected movements, preserving applicable side comparisons. Joint mobility also preserves the requested applicable left/right distinction; the spine exception applies to artificial sides.

#### Background and Evidence

- **R056 — Confirmed Requirement:** Separate MMT and joint mobility, including their grading. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 01:53:46; 02:14:24; P0047,P0105,P0181,P1694–1699,P1996. Speakers: January Austin, Admin Black House Developers.
- **R065 — Confirmed Requirement:** Reuse ROM-selected movements as MMT selections, not ROM numeric values. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:14:24; P0053,P0113,P0191,P1989–1996. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: ObjectiveTab combines MMT/Joint Mobility and uses a union of grades. ROM-selected movement carry-over is not established as a separate mapping.

#### Expected Behavior

MMT and joint mobility retain distinct values and grading choices. Selecting a ROM movement makes its corresponding MMT choice available without copying degrees into strength grades.

#### Scope

Separate editor/serialization semantics, selection synchronization and legacy compatibility.

#### Out of Scope

Copying ROM numbers, inventing conversion formulas or automatically deleting clinician-entered MMT when ROM selection changes.

#### Implementation Guidance

**Technical Recommendation:** share movement IDs and map selection only. Existing catalog already separates MmtGradeOptions and JointMobilityGradeOptions; stop combining them in the editor. Preserve independent observations and typed/JSON mapping; no new grade definitions are required for this slice.

#### Data and Migration Considerations

Legacy combined entries need explicit classification or retained legacy display; ambiguous values must not be guessed into one category.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce metric-kind validation and note ownership server-side, including offline/sync replay.

#### Acceptance Criteria

- [ ] **AC1:** Given ROM movement selection, when MMT opens, then corresponding movement choices are seeded with empty strength values.
- [ ] **AC2:** Given an entered MMT grade and ROM degrees, when saving/reloading, then each retains its own type and value.
- [ ] **AC3:** Given a joint-mobility grade, when editing MMT, then joint-specific grades are unavailable in MMT and vice versa.
- [ ] **AC4:** Given a removed ROM selection with existing MMT data, when selections refresh, then that clinical value is not silently erased.
- [ ] **AC5:** Given a paired joint-mobility assessment, when left/right values are entered, then both remain distinct after reload; spine is not forced into artificial paired sides.

#### Testing and Verification

Typed/JSON round-trip, movement mapping, grade validation, bilateral and ambiguous legacy-record tests.

#### Rollout and Regression Risks

Combined historical records may be ambiguous. Preserve them with explicit legacy rendering until classified.

#### Open Questions

Clinical owner reviews any requested additions or movement mapping exceptions; the existing separate grade catalogs support the confirmed separation without new clinical definitions.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Only new catalog additions; existing-grade separation criteria are independent.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-23"></a>

### PR 23: Establish versioned outcome definitions and validate supplied source assets

**Status:** Ready for Development\
**Category:** Technical Improvement\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Outcome registry/reference data and supplied assets; E23\
**Dependencies:** Clinical owner approval of per-instrument version/scoring; materials M01–M17.\
**Source IDs:** R012, R095\
**Verification Status:** Partially Implemented

#### User Problem

The system has aggregate score metadata, while supplied forms vary in wording, scoring completeness and administration method.

#### Confirmed Requirement

The client supplied outcome forms for digital questionnaires/scoring. The implementation must use the intended versions; anomalies and omitted scoring rules remain decisions, not permission to infer clinical algorithms.

#### Background and Evidence

- **R012 — External Dependency:** Client supplies outcome-measure PDFs/information (16 PDFs now received; approval tracked in R095). Source: S02 Assigned next steps, Timestamped transcript; 26:15–27:23; 02:53:55; 03:00:35; P0073,P0125,P0525–0555,P2573,P2679–2682; supplied S04/S05. Speakers: January Austin, Admin Black House Developers.
- **R095 — Needs Clarification:** S04/S05 supplied-asset inspection; see M01–M17 in section 8. These anomalies are verification findings, not new client requests.

#### Current Behavior

Repository-verified: aggregate definitions and a scoring registry exist; per-question form content was not found. Sixteen PDFs contain seventeen named instruments and a body diagram. Several forms omit needed scoring rules.

#### Expected Behavior

A versioned, reviewable definition contract supports items/options, administration mode, scoring, missing-answer validation and provenance. Only clinically approved definitions can be activated.

#### Scope

Technical contract and asset-validation fixture framework; produce the approval matrix in section 8 before enabling each instrument.

#### Out of Scope

Activating every supplied PDF, silently correcting HOOS/FAAM/PFDI content, or assuming a filename proves licensing/clinical approval.

#### Implementation Guidance

**Technical Recommendation:** reuse OutcomeMeasureRegistry/Service boundaries; add stable instrument/version/item IDs, explicit nullable answers, scorer version and completion status. Keep source hashes and clinical sign-off separate from code review. Use approved example answer sets as fixtures.

#### Data and Migration Considerations

Additive response/result/version provenance may be needed; preserve legacy aggregate results as imported/manual historical values rather than fabricated item responses.

#### Security and Privacy Considerations

**Technical Recommendation:** score on the server and restrict patient links to assigned instruments; validate item IDs/ranges and avoid logging responses.

#### Acceptance Criteria

- [ ] **AC1:** Given a definition without approved scoring/missing-answer rules, when activation is attempted, then it remains unavailable for clinical collection.
- [ ] **AC2:** Given an approved definition and answer fixture, when scored, then the result exactly matches the approved calculation and retains definition/scorer versions.
- [ ] **AC3:** Given an invalid or incomplete answer set, when submitted, then its status follows the approved rule rather than defaulting blanks to zero.
- [ ] **AC4:** Given an old aggregate score, when definitions are introduced, then no artificial item-level responses are created.

#### Testing and Verification

Definition-schema, server-validation and deterministic scoring fixtures; review every inventory anomaly with clinical owner. This technical slice can proceed before all content is approved.

#### Rollout and Regression Risks

Incorrect clinical scores are the central risk. Activation gates apply per instrument; no all-or-nothing release of unapproved content.

#### Open Questions

January/clinical owner supplies exact variants, missing-answer rules, assignment rules, approved examples and permission to reproduce the supplied forms. Missing evidence blocks each affected instrument, not the contract.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Instrument activation and scorer fixtures.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-24"></a>

### PR 24: Deliver approved patient questionnaires with automatic scoring

**Status:** Blocked by Dependency\
**Category:** Intake\
**Priority:** High\
**Confidence:** High\
**Affected Area:** OutcomeMeasuresStep and outcome response APIs; E23\
**Dependencies:** PR 23 contract; first approved patient instrument cohort; assignment decision.\
**Source IDs:** R011\
**Verification Status:** Partially Implemented

#### User Problem

Patients currently face a score-entry prompt rather than the questions needed to calculate a trustworthy result.

#### Confirmed Requirement

Patients answer applicable outcome questions and PTDoc calculates scores; do not ask patients to compute or supply the resulting numeric score.

#### Background and Evidence

- **R011 — Confirmed Requirement:** Patients answer outcome questionnaires and the system calculates scores. Source: S02 Aligned, Detailed notes/screenshots, Timestamped transcript; 21:15–28:43; P0020,P0145,P0453,P0495–0499,P0569–0571. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: OutcomeMeasuresStep recommends measures and exposes a Previous Outcome Measure or Functional Score text field. Actual per-item patient questionnaires were not found.

#### Expected Behavior

Patients receive assigned approved questionnaires, can resume them, and see clear completion/review behavior; the server produces versioned results.

#### Scope

First delivery PR: one approved cohort and reusable renderer. **Technical Recommendation:** follow with independent instrument-content PRs, each reviewed and fixture-tested, rather than enabling all 17 instruments at once.

#### Out of Scope

Clinician-administered Berg/Tinetti/timed tests, guessed missing-answer rules or removal of historical manually entered score evidence.

#### Implementation Guidance

**Technical Recommendation:** use PR 23 definitions and server scorers; accessible radio/choice controls, clear N/A versus unanswered semantics, progress and resumability. Derive assignment from approved clinical rules, not ad hoc diagnosis inference.

#### Data and Migration Considerations

Persist item answers, completion state and scorer/definition version. Keep historical prior-score text separately identified; do not relabel it a newly computed result.

#### Security and Privacy Considerations

**Technical Recommendation:** validate patient-link assignment and submission state; prevent posting answers/results for another patient or arbitrary instrument.

#### Acceptance Criteria

- [ ] **AC1:** Given an assigned approved patient measure, when the patient answers and submits, then the server computes the approved result without asking for a numeric score.
- [ ] **AC2:** Given a partially completed questionnaire, when resumed, then prior answers are preserved and missing responses follow the instrument rule.
- [ ] **AC3:** Given a tampered client total, when submitted, then the server ignores/rejects it and calculates from validated responses.
- [ ] **AC4:** Given a clinician-administered measure, when a patient opens intake, then it is not presented for self-administration.

#### Testing and Verification

Approved fixture totals, min/max/missing/N/A cases, resume, keyboard and mobile usability, assignment/tenancy and stale-version tests for every activated instrument.

#### Rollout and Regression Risks

Instrument burden and scoring errors; release a small approved cohort, then expand per-instrument gates.

#### Open Questions

January selects first cohort and assignment by body region/note type. Activation depends on these decisions and PR 23.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** Assignment and instrument-scoring criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-25"></a>

### PR 25: Carry computed outcomes into clinical notes with provenance

**Status:** Blocked by Dependency\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Intake projections, outcome results and note objective sections; E23\
**Dependencies:** PR 24 computed patient results; PR 36 clinician results; note-type routing in PR 08.\
**Source IDs:** R066\
**Verification Status:** Partially Implemented

#### User Problem

Clinicians should not retype patient-completed scores or confuse historical scores with current measurements.

#### Confirmed Requirement

Carry calculated intake outcome results into evaluation, progress and discharge as read-only results while preserving clinician-administered measures as a separate workflow.

#### Background and Evidence

- **R066 — Confirmed Requirement:** Carry calculated intake outcomes into evaluation/progress/discharge as read-only results. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:15:27–02:16:49; P0054,P0114,P0192,P2004–2020. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: outcome result entities/registry exist and aggregate entry is supported, but end-to-end item-response-to-read-only-note projection was not verified.

#### Expected Behavior

Notes show the associated instrument, score, completion date and version/source. Clinicians cannot overwrite a computed score as ordinary free text.

#### Scope

Encounter/result association, read-only presentation and export compatibility; distinguish current from prior results.

#### Out of Scope

Rescoring historical results under new algorithms, new daily outcome collection or copying the latest patient result into every encounter without linkage.

#### Implementation Guidance

**Technical Recommendation:** link immutable completed result IDs to the intended note and snapshot necessary display metadata when signing. Corrections should create an attributable corrected result rather than rewriting signed content.

#### Data and Migration Considerations

Preserve prior aggregate/manual results and signed-note snapshots. Define current/prior ordering by encounter/completion metadata, not array order.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce note/patient/result clinic alignment and signed-note immutability server-side.

#### Acceptance Criteria

- [ ] **AC1:** Given a completed intake result linked to an evaluation/progress/discharge encounter, when its note opens, then the correct computed score and provenance appear read-only.
- [ ] **AC2:** Given several results for the same instrument, when displaying current and prior, then the associated encounter determines current and historical values remain distinct.
- [ ] **AC3:** Given a signed note, when a later questionnaire is completed, then the signed note’s recorded result does not silently change.
- [ ] **AC4:** Given a clinician-entered administered measure, when displayed, then its source is distinguished from patient questionnaire scoring.

#### Testing and Verification

Projection/linking tests, multiple encounters/versions, signed export snapshots and cross-clinic result rejection.

#### Rollout and Regression Risks

Wrong-encounter carry-forward is the main risk; require explicit association and visible provenance.

#### Open Questions

Clinical owner: confirm correction workflow and prior-result display; no automatic rescore or reassignment is authorized.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** Current/prior and signed-result correction criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-26"></a>

### PR 26: Capture patient personal goals and use them in SMART-goal drafting

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Functional limitations intake and AssessmentWorkspaceSection; E10,E26\
**Dependencies:** PR 07/08 projection conventions; existing clinician-reviewed AI goal workflow.\
**Source IDs:** R064\
**Verification Status:** Partially Implemented

#### User Problem

Patient priorities are not captured alongside limitations and may not guide clinician goal creation.

#### Confirmed Requirement

Add a personal-goal question after functional limitations and carry it into clinician-reviewed SMART-goal work.

#### Background and Evidence

- **R064 — Confirmed Requirement:** Capture personal goal after limitations and carry into clinician-reviewed SMART-goal work. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:11:59–02:13:24; P0052,P0112,P0190,P1954–1975. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: assessment already has PatientPersonalGoals; corresponding intake capture/projection was not found in the inspected draft/components/domain projection.

#### Expected Behavior

The patient’s own goal is retained as attributed input and available to the clinician and approved goal-drafting request.

#### Scope

Intake capture, review, projection and goal-context plumbing.

#### Out of Scope

Automatic clinical targets/timeframes, automatic acceptance or rewriting the patient’s statement as a measured baseline.

#### Implementation Guidance

**Technical Recommendation:** preserve verbatim patient input and distinguish it from clinician-authored goals. Add to the existing generation context rather than creating a second goal engine; apply the established AI acceptance flow.

#### Data and Migration Considerations

Additive optional intake field and compatible mapping to existing assessment data. Keep old notes with no patient goal valid.

#### Security and Privacy Considerations

**Technical Recommendation:** include only authorized encounter data in AI requests and keep generated suggestions outside signed content until reviewed.

#### Acceptance Criteria

- [ ] **AC1:** Given a patient completing limitations, when entering a personal goal, then it appears in intake review and the linked clinician assessment after reload.
- [ ] **AC2:** Given goal generation, when context is built, then that personal goal is available as patient-reported context without invented measurements.
- [ ] **AC3:** Given a generated suggestion, when the clinician has not accepted it, then it is not treated as an accepted clinical goal.

#### Testing and Verification

Intake-to-note round-trip, old-draft compatibility, request-context contract and accept/reject/edit behavior.

#### Rollout and Regression Risks

Patient wording may be mistaken for measured clinical fact; preserve attribution.

#### Open Questions

January reviews wording and location during acceptance; no new clinical target rules are assumed.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Patient-goal wording and acceptance review.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-27"></a>

### PR 27: Place evaluation assessment narrative after contributing inputs

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** Medium\
**Confidence:** High\
**Affected Area:** Evaluation subjective/assessment layout; E27\
**Dependencies:** Existing note schema and clinician workflow; separate evidence for reported duplicate/stale fields.\
**Source IDs:** R059, R068, R069\
**Verification Status:** Partially Implemented

#### User Problem

The assessment narrative appears before the information clinicians use to compose it.

#### Confirmed Requirement

Move clinical assessment narrative after its contributing evaluation inputs. Investigate the reported duplicate frequency and previous-versus-current concerns only after identifying their exact fields.

#### Background and Evidence

- **R059 — Reported Defect:** Evaluation subjective reportedly repeats frequency questions. Source: S02 Detailed notes/screenshots, Timestamped transcript; 01:57:11–01:58:17; P0184,P1755–1767. Speakers: January Austin, Admin Black House Developers.
- **R068 — Needs Clarification:** Assessment allegedly shows previous rather than current data; client could not identify field. Source: S02 Timestamped transcript; 02:16:49–02:18:08; P2021–2028. Speakers: January Austin, Admin Black House Developers.
- **R069 — Confirmed Requirement:** Place clinical assessment narrative after its contributing evaluation inputs. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:18:08–02:19:35; P0055,P0115,P0193,P2030–2042. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: AssessmentWorkspaceSection renders narrative before goals/motivation/prognosis. The allegedly duplicated frequency/current-data fields could not be reliably identified from the transcript; no defect cause is established.

#### Expected Behavior

Contributing inputs precede the assessment narrative with saved data unchanged. Unverified fields are not removed speculatively.

#### Scope

Layout order and focus/tab sequence; bounded verification tasks for R059/R068.

#### Out of Scope

Deleting an unspecified frequency question or changing current/prior values without a reproducer.

#### Implementation Guidance

**Technical Recommendation:** reorder composition without rewriting saved payloads. Capture a synthetic field-level example before implementing any additional data fix; split a proven defect into a focused follow-up.

#### Data and Migration Considerations

No migration for layout. Any proven mapping fix must preserve existing values and signed records.

#### Security and Privacy Considerations

**Technical Recommendation:** maintain existing draft permissions and clinician acceptance controls.

#### Acceptance Criteria

- [ ] **AC1:** Given an evaluation assessment, when navigating by keyboard or reading order, then contributing inputs precede the clinical narrative.
- [ ] **AC2:** Given a saved draft, when reopening after the layout change, then all existing values remain unchanged.
- [ ] **AC3:** Given a proposed frequency/current-data fix, when evidence is reviewed, then the exact field, expected value and observed value are documented before code changes.

#### Testing and Verification

Component order/focus and saved-draft regression; separate runtime reproduction for ambiguous reports.

#### Rollout and Regression Risks

A broad cleanup could delete useful clinical data; keep implementation limited to the confirmed ordering.

#### Open Questions

January: provide the exact frequency field and an anonymized current-versus-prior example. Those subitems remain Verification Required/Needs Clarification and do not block layout.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Only duplicate-frequency/current-data subitems; layout criteria are independent.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-28"></a>

### PR 28: Generate distinct SOAP summaries from their corresponding sections

**Status:** Ready for Development\
**Category:** AI-Assisted Functionality\
**Priority:** High\
**Confidence:** High\
**Affected Area:** PlanTab, ClinicalGenerationService and AI contracts; E28\
**Dependencies:** Existing AI feature flags/provider configuration; clinician-approved synthetic examples.\
**Source IDs:** R073\
**Verification Status:** Partially Implemented

#### User Problem

Generated summaries are repetitive and do not clearly reflect their corresponding S/O/A/P sections.

#### Confirmed Requirement

Produce distinct Subjective, Objective, Assessment and Plan paragraphs from the appropriate corresponding content.

#### Background and Evidence

- **R073 — Confirmed Requirement:** Generate distinct S/O/A/P summary paragraphs from corresponding note sections. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:21:55–02:25:38; 02:30:51; 02:35:35; P0056,P0116,P0195,P2095–2131,P2228–2232,P2299. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: inspected PlanTab discharge/follow-up/summary methods call the same GeneratePlanOfCareAsync request path. This supports a routing/context concern but does not prove the exact runtime output.

#### Expected Behavior

Each summary operation has an explicit section purpose and context, and output remains a reviewable suggestion.

#### Scope

Section-specific request/context/output contracts and UI acceptance, preserving existing generation infrastructure.

#### Out of Scope

New model/vendor selection, automatic signing or invented clinical facts to fill missing sections.

#### Implementation Guidance

**Technical Recommendation:** differentiate operation purpose server-side and restrict context to relevant authorized note data. Use deterministic contract tests plus a clinician-reviewed fixed synthetic corpus for quality evaluation; do not test only exact prose strings.

#### Data and Migration Considerations

Preserve accepted text, suggestion state and provenance. A regeneration must not overwrite accepted content without explicit user action.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce authorization, generation rate limits and explicit clinician acceptance; exclude unrelated patient data from prompts/logs.

#### Acceptance Criteria

- [ ] **AC1:** Given distinct S/O/A/P source content, when each summary is generated, then its request and displayed output correspond to that section.
- [ ] **AC2:** Given missing source facts, when generating, then no fabricated measurements, diagnoses or completed interventions are introduced.
- [ ] **AC3:** Given accepted text and a new suggestion, when generation completes, then accepted text remains until the clinician explicitly replaces it.
- [ ] **AC4:** Given disabled AI or provider failure, when generation is requested, then a recoverable state appears and manual editing remains usable.

#### Testing and Verification

Purpose/context contract, authorization, disabled/provider-error and accept/reject tests; clinician evaluation for relevance, distinctness and unsupported facts.

#### Rollout and Regression Risks

Clinical hallucination and accepted-text overwrite; staged feature-flag rollout with review.

#### Open Questions

Clinical owner supplies approved quality examples and acceptance rubric; implementation contract is ready, release quality requires review.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Clinical-output relevance/unsupported-facts criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-29"></a>

### PR 29: Reproduce and repair goal-generation loss and candidate exhaustion

**Status:** Verification Required\
**Category:** AI-Assisted Functionality\
**Priority:** High\
**Confidence:** Medium\
**Affected Area:** Goal suggestion state and generation workflow; E29\
**Dependencies:** Synthetic reproduction of eight accepted goals; PR 28 context conventions.\
**Source IDs:** R070, R071\
**Verification Status:** Unable to Verify

#### User Problem

Goal suggestions reportedly disappear, degrade in quality and stop producing new options after eight accepted goals.

#### Confirmed Requirement

Investigate the observed loss/exhaustion and quality regression. The short-versus-long-term concern was withdrawn and does not authorize redesign.

#### Background and Evidence

- **R070 — Reported Defect:** AI goal suggestions regress in quality relative to initial goals. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:19:35; P0194,P2042–2048. Speakers: January Austin, Admin Black House Developers.
- **R071 — Reported Defect:** AI goals disappear during demonstration and yield no new candidates after eight accepted. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:20:46–02:21:55; P0194,P2059–2083. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Client-reported and meeting-observed behavior; targeted source inspection found no confirmed literal eight-goal limit. The cause could be state, filtering, request limits or provider output; none is established.

#### Expected Behavior

Accepted goals remain intact and new requests have a clear result, error or documented limit; quality meets an approved clinical rubric.

#### Scope

Reproduce state transitions and inspect request/response/filtering; repair demonstrated causes and evaluate quality separately.

#### Out of Scope

Assuming an eight-item constant exists, increasing quotas blindly or rewriting all goal templates.

#### Implementation Guidance

**Technical Recommendation:** trace candidate IDs, accepted/rejected states and generation pagination/filtering with non-PHI diagnostics. Preserve accepted goals during retries; evaluate clinical specificity/measurability against approved examples.

#### Data and Migration Considerations

No schema change assumed. If goal IDs/state persist incorrectly, use compatible repair and never discard accepted historical goals.

#### Security and Privacy Considerations

**Technical Recommendation:** maintain clinician acceptance, note ownership and bounded generation; do not log clinical prompt bodies.

#### Acceptance Criteria

- [ ] **AC1:** Given eight accepted goals and a valid new request, when generation completes, then accepted goals remain and the UI shows new candidates or an explicit supported constraint/error.
- [ ] **AC2:** Given a transient generation failure, when retrying, then accepted and pending goal state is not silently lost.
- [ ] **AC3:** Given the approved synthetic quality corpus, when evaluated, then suggestions meet the recorded rubric without invented patient facts.

#### Testing and Verification

First capture reproduction; then state-transition and provider-boundary tests, reload/resume, duplicate candidates and clinician quality review.

#### Rollout and Regression Risks

Blindly raising limits may increase cost without fixing loss; diagnose before coding.

#### Open Questions

January: provide anonymized good/bad examples and clarify whether the eight accepted goals were saved or only selected. Engineering records the exact build and response shape.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** Reproduction and clinical-quality criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-30"></a>

### PR 30: Complete progress activity tracking, improvement and plan frequency

**Status:** Ready for Development\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** ProgressSubjectiveSection and plan composition; E30\
**Dependencies:** Recorded limitation catalog; PR 08 aligned intake; PR 32 handles AI Suggest.\
**Source IDs:** R052, R076, R077, R078, R080\
**Verification Status:** Partially Implemented

#### User Problem

Progress notes need structured change tracking and a way to add newly difficult activities and adjust the plan.

#### Confirmed Requirement

Improved/same/worse choices use recorded intake/evaluation activities; newly difficult uses the full relevant body-region catalog and persists for future visits. Add percent improved and permit progress plan frequency adjustment without adding it to daily.

#### Background and Evidence

- **R052 — Needs Clarification:** Earlier instruction to remove progress question 8 may be a note-type mix-up. Source: S02 Timestamped transcript; 01:41:51–01:43:35; P1503–1547. Speakers: January Austin, Admin Black House Developers.
- **R076 — Confirmed Requirement:** Progress improved/same/worse choices come from recorded intake/evaluation activities. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:26:38–02:27:51; P0057,P0117,P0196,P2148–2166. Speakers: January Austin, Admin Black House Developers.
- **R077 — Confirmed Requirement:** Newly difficult activities use full relevant catalog and persist for later visits. Source: S02 Assigned next steps, Timestamped transcript; 02:28:44–02:29:44; P0118–0119,P2175–2199. Speakers: January Austin, Admin Black House Developers.
- **R078 — Confirmed Requirement:** Progress plan of care permits frequency adjustment; do not add it to daily. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:29:44–02:30:51; P0058,P0120,P0197,P2202–2221. Speakers: January Austin, Admin Black House Developers.
- **R080 — Confirmed Requirement:** Add percent-improved field to progress subjective. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:32:09; P0059,P0122,P0198,P2245–2250. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: progress has fixed activity/question controls; no percent-improved field was found there. Generic PlanTab contains frequency inputs, so progress route visibility/binding must be verified before adding duplicates.

#### Expected Behavior

Clinicians see the patient’s recorded activity baseline, can record change and newly difficult tasks, and retain updated progress plan frequency.

#### Scope

Catalog binding and persistence, percentage validation and progress-only plan composition.

#### Out of Scope

Applying daily question deletions to progress or removing progress question 8 without clarification.

#### Implementation Guidance

**Technical Recommendation:** use stable activity IDs with body-region context; newly difficult is not limited to already selected activities. Append clinical history instead of replacing the baseline; validate percentage in the agreed 0–100 representation.

#### Data and Migration Considerations

Preserve original limitations and per-encounter change states. Newly added activities join future choice sets with provenance; do not mutate old signed notes.

#### Security and Privacy Considerations

**Technical Recommendation:** enforce patient/note scope and version checks for plan changes and catalog additions.

#### Acceptance Criteria

- [ ] **AC1:** Given recorded limitations, when progress opens, then improved/same/worse choices reflect those recorded activities.
- [ ] **AC2:** Given an unrecorded relevant activity, when added as newly difficult, then it is selectable from the full relevant catalog and available at a later visit.
- [ ] **AC3:** Given percent improved and revised frequency, when saving/reloading progress, then valid values persist and invalid percentages are rejected.
- [ ] **AC4:** Given a daily note, when progress frequency support changes, then no new daily frequency control is introduced.

#### Testing and Verification

Activity-history and catalog mapping, multi-region/new activity persistence, percentage bounds, plan save/reload and daily regression.

#### Rollout and Regression Risks

Baseline loss or duplicate activity IDs; retain provenance and use stable IDs.

#### Open Questions

January: does the early instruction to remove question 8 apply to progress or only the later daily list? This blocks that deletion only, not the confirmed progress additions.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Only proposed question-8 deletion; confirmed additions are independent.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-31"></a>

### PR 31: Implement the approved discharge questionnaire and recommendation choices

**Status:** Blocked by Dependency\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** Medium\
**Affected Area:** DischargeSubjectiveSection, DischargePlanSection and discharge intake; E31\
**Dependencies:** Exact referenced discharge document/revision; approved recommendation options; PR 08.\
**Source IDs:** R081, R082, R094\
**Verification Status:** Partially Implemented

#### User Problem

Discharge needs structured patient feedback and recommendations aligned with the client’s source document.

#### Confirmed Requirement

Collect goals, pain, remaining difficulty and improvement; keep discharge reason as clinician input. Recommendations should use multi-select/dropdown choices.

#### Background and Evidence

- **R081 — Confirmed Requirement:** Discharge recommendations use multi-select/dropdown inputs. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:30:51–02:32:09; P0060,P0123,P0198,P2234–2243. Speakers: January Austin, Admin Black House Developers.
- **R082 — Confirmed Requirement:** Discharge subjective follows referenced document: goals, pain, remaining difficulty, improvement; reason is clinician input. Source: S02 Aligned, Assigned next steps, Detailed notes/screenshots, Timestamped transcript; 02:33:32–02:34:30; P0061,P0124,P0198,P2275–2287. Speakers: January Austin, Admin Black House Developers.
- **R094 — External Dependency:** Obtain exact discharge workflow revision referenced during meeting. Source: S02 Timestamped transcript; 02:33:32–02:34:30; P2269–2287. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: discharge already has percent-improved, free-text goals/difficulty and clinician reason; recommendations are a textarea. The exact referenced discharge document was not supplied among the roadmap, meeting and outcome PDFs.

#### Expected Behavior

Approved discharge questions and choice lists appear in the appropriate patient/clinician surfaces without duplicate fields.

#### Scope

Compare the missing source against current components, then implement only gaps and aligned intake projection.

#### Out of Scope

Inventing recommendation choices or a complete questionnaire from a brief transcript paraphrase.

#### Implementation Guidance

**Technical Recommendation:** use stable IDs/versioned definitions shared with intake, reuse existing percentage validation and separate patient responses from clinician decisions.

#### Data and Migration Considerations

Preserve free-text historical recommendations and old questionnaire answers. New choice IDs must not be retroactively inferred from narrative text.

#### Security and Privacy Considerations

**Technical Recommendation:** only authorized clinicians set discharge reason and accept clinical recommendations; patient links cannot mutate clinician-only fields.

#### Acceptance Criteria

- [ ] **AC1:** Given the approved discharge document, when its mapping is reviewed, then every question is assigned to patient or clinician input without unapproved additions.
- [ ] **AC2:** Given completed patient responses, when the linked discharge note opens, then goals, pain, remaining difficulty and improvement are retained.
- [ ] **AC3:** Given clinician recommendation choices, when saving/reloading, then all selected options and allowed additional text persist.
- [ ] **AC4:** Given an old discharge note, when rendered after rollout, then its original narrative and reason remain intact.

#### Testing and Verification

Source-to-field review, choice binding, old payload compatibility, patient/clinician permission tests and discharge intake-to-note browser flow.

#### Rollout and Regression Risks

Missing source is a real blocker for content. Existing component inspection and contract planning can proceed.

#### Open Questions

January supplies the exact discharge document/revision and recommendation vocabulary; clinical owner approves patient-versus-clinician ownership. Content implementation and AC1–AC3 depend on it.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** AC1–AC3.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-32"></a>

### PR 32: Make progress and discharge AI actions produce reviewable suggestions

**Status:** Ready for Development\
**Category:** AI-Assisted Functionality\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Progress next-visit plan and discharge generation handlers; E31,E32\
**Dependencies:** PR 28 section-specific AI contract; PR 31 approved structured fields for full discharge context.\
**Source IDs:** R079, R083\
**Verification Status:** Partially Implemented

#### User Problem

Progress lacks the requested next-visit suggestion action and discharge generation controls do not perform the expected generation.

#### Confirmed Requirement

Add AI Suggest for progress Plan for Next Visit and make discharge recommendation/follow-up/summary actions functional.

#### Background and Evidence

- **R079 — Confirmed Requirement:** Add AI Suggest to progress Plan for Next Visit. Source: S02 Assigned next steps, Timestamped transcript; 02:30:51; P0121,P2222–2227. Speakers: January Austin, Admin Black House Developers.
- **R083 — Reported Defect:** Discharge recommendation/follow-up generation and summary button need functioning behavior. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:35:35; P0199,P2295–2304. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Repository-verified: DischargePlanSection GenerateFullNote/GenerateClinicalSummary handlers only call HandleChangedAsync; they do not invoke a generator. Generic PlanTab also shares generation paths. Actual deployed behavior was not tested.

#### Expected Behavior

Each action invokes its intended generation purpose, shows loading/error states and presents a suggestion for explicit clinician review.

#### Scope

Wire existing UI through the appropriate server AI contracts and preserve manual editing.

#### Out of Scope

Automatic discharge, signing, accepting generated recommendations or guessing PR 31’s missing choice vocabulary.

#### Implementation Guidance

**Technical Recommendation:** share PR 28 purpose/context infrastructure, not one undifferentiated plan prompt. Cancel/ignore stale responses when patient/note context changes and use existing rate limits/feature flags.

#### Data and Migration Considerations

Preserve manual and accepted text separately from pending suggestions. No schema migration assumed unless suggestion provenance is missing.

#### Security and Privacy Considerations

**Technical Recommendation:** server authorizes note access; only approved context is sent to the provider and clinician acceptance remains mandatory.

#### Acceptance Criteria

- [ ] **AC1:** Given a progress draft, when AI Suggest is selected for Plan for Next Visit, then the correct operation returns a reviewable next-visit suggestion.
- [ ] **AC2:** Given a discharge draft, when each generation action is selected, then the corresponding operation runs and its result appears in the intended section.
- [ ] **AC3:** Given provider failure or disabled AI, when the action runs, then a clear recoverable message appears and existing text is retained.
- [ ] **AC4:** Given a response arriving after navigation to another patient, when it completes, then it cannot populate that other patient’s note.

#### Testing and Verification

Handler-to-contract tests, mocked provider responses/errors, stale-response cancellation, disabled flags and explicit accept/reject; clinical review using synthetic notes.

#### Rollout and Regression Risks

Wrong-section output or stale responses can contaminate notes; keep context checks and suggestion review.

#### Open Questions

Clinical owner approves output examples. Full structured discharge-context acceptance waits for PR 31; wiring against existing fields can proceed.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Clinical-output criteria and full discharge context.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-33"></a>

### PR 33: Verify and remove redundant Settings scrolling

**Status:** Verification Required\
**Category:** UI/UX\
**Priority:** Low\
**Confidence:** High\
**Affected Area:** Settings page and shared shell scroll containers; E33\
**Dependencies:** Integrated #153/#154 UI; supported viewport matrix.\
**Source IDs:** R002\
**Verification Status:** Unable to Verify

#### User Problem

The client sees an unnecessary extra scrollbar on Settings.

#### Confirmed Requirement

Remove the redundant Settings scroll container without losing access to content.

#### Background and Evidence

- **R002 — Confirmed Requirement:** Remove the extra Settings scrollbar. Source: S02 Aligned, Assigned next steps, Timestamped transcript; 06:04; P0018,P0069,P0300–0308. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Meeting screenshot/report establishes the complaint; source contains page/shell scrolling but the exact current overflow cause was not reproduced.

#### Expected Behavior

Settings has predictable scrolling and all controls remain reachable at supported viewport sizes.

#### Scope

Reproduce in the integrated Settings build and change only the responsible layout/overflow rule.

#### Out of Scope

Broad application styling cleanup or removing necessary scrolling from long content.

#### Implementation Guidance

**Technical Recommendation:** inspect scroll ownership in the actual layout, use existing tokens and test keyboard focus/zoom before changing overflow. Coordinate with #154 to avoid a parallel CSS fix that is overwritten.

#### Data and Migration Considerations

No data migration.

#### Security and Privacy Considerations

**Technical Recommendation:** keep hidden/offscreen controls accessible only to their authorized roles; layout changes must not reveal restricted panels.

#### Acceptance Criteria

- [ ] **AC1:** Given Settings at supported desktop/tablet/phone widths, when content exceeds the viewport, then one intentional page/content scroll path reaches all controls.
- [ ] **AC2:** Given keyboard navigation and zoomed text, when focus moves through Settings, then focused controls remain visible and usable.

#### Testing and Verification

Visual/browser reproduction with screenshot before/after, long Settings content, keyboard and zoom; no new unit tests solely mirroring CSS.

#### Rollout and Regression Risks

Removing overflow blindly can clip forms; verify against the integrated Settings stack.

#### Open Questions

Engineering supplies the integrated build and exact viewport reproducer; diagnosis can start when available.

**Development impact:** Reproduction and evidence gathering can proceed; a specific fix must follow a verified cause.

**Dependent criteria:** Both viewport/keyboard criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-34"></a>

### PR 34: Decide sex and gender terminology, storage and clinical use

**Status:** Needs Clarification\
**Category:** Needs Clarification\
**Priority:** Needs Decision\
**Confidence:** Medium\
**Affected Area:** Patient demographics and intake identity fields; E34\
**Dependencies:** Client/clinical decision and existing data inventory.\
**Source IDs:** R017\
**Verification Status:** Unable to Verify

#### User Problem

The meeting did not settle how identity labels and clinically relevant sex information should be represented.

#### Confirmed Requirement

The discussion is unresolved; no approved label, allowed values, requiredness or clinical decision rule can be inferred.

#### Background and Evidence

- **R017 — Needs Clarification:** Choose sex/gender terminology and data use. Source: S02 Detailed notes/screenshots, Timestamped transcript; 35:59–39:33; P0013,P0150,P0663,P0697,P0715,P0735–0744. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Meeting discussion and current demographic labels provide context only. This planning pass did not verify the complete storage/export/downstream use of every related field.

#### Expected Behavior

A documented field-purpose and migration decision precedes UI or schema changes.

#### Scope

Inventory existing values/uses, agree terminology and clinically necessary distinctions, then scope implementation.

#### Out of Scope

Inferring identity from appearance/name, enforcing unapproved binary options or automatically converting existing values.

#### Implementation Guidance

**Technical Recommendation:** distinguish display language, data meaning and clinical use; avoid repurposing an existing field without migration/provenance. Review intake, chart, exports and integrations together.

#### Data and Migration Considerations

Any change may affect existing demographics and signed records; preserve original historical values and define explicit mappings only after approval.

#### Security and Privacy Considerations

**Technical Recommendation:** collect only approved necessary information, restrict access and avoid sensitive values in analytics.

#### Acceptance Criteria

- [ ] **AC1:** Given existing field values and consuming workflows, when the decision is reviewed, then each field has an approved purpose, label, options and requiredness.
- [ ] **AC2:** Given a legacy value without an approved mapping, when migration is rehearsed, then it is preserved or flagged for review rather than guessed.
- [ ] **AC3:** Given historical signed documentation, when labels change, then its recorded content is not rewritten.

#### Testing and Verification

Decision examples and data inventory first; later mapping, API validation, accessibility and historical-rendering tests.

#### Rollout and Regression Risks

Incorrect semantic migration can misrepresent patient data. No label-only change before purpose is agreed.

#### Open Questions

January and clinical owner decide labels, options, requiredness, visibility and downstream use. All implementation is blocked on that decision.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All semantic/migration criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-35"></a>

### PR 35: Decide the zero-authorization-visit documentation safeguard

**Status:** Needs Clarification\
**Category:** Insurance and Authorization\
**Priority:** Needs Decision\
**Confidence:** Medium\
**Affected Area:** Authorization utilization and note creation; E35\
**Dependencies:** Clinical/operations policy; existing visit accounting and concurrency rules.\
**Source IDs:** R089\
**Verification Status:** Unable to Verify

#### User Problem

The client wants to avoid discovering too late that authorized visits are exhausted and supporting progress documentation is needed.

#### Confirmed Requirement

Unlike the surrounding third-party complaints, January explicitly connected a zero-visit safeguard to PTDoc. Defaulting toward progress documentation was exploratory and lacks approved trigger/override rules.

#### Background and Evidence

- **R089 — Possible Future Idea:** PTDoc safeguard at zero authorized visits could default to progress documentation. Source: S02 Detailed notes/screenshots, Timestamped transcript; 02:57:56; P0206,P2623–2633. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

Current authorization models and utilization workflows exist; runtime behavior at zero visits was not verified. Third-party signing problems are not proof PTDoc has the same defect.

#### Expected Behavior

An approved policy defines when to warn, suggest progress documentation or restrict an action, and who can override it.

#### Scope

Decision with concrete remaining-visit/date/multiple-policy examples; implementation only after approval.

#### Out of Scope

Automatic note-type conversion, fabricated authorizations, bypassing signature controls or building third-party billing/copay integrations.

#### Implementation Guidance

**Technical Recommendation:** compute eligibility using authoritative server data and existing visit accounting; distinguish pending requests, expired authorizations, exhausted visits and unknown coverage. If adopted, show an explainable suggestion before creating a note.

#### Data and Migration Considerations

No automatic migration of prior notes or authorization balances. Define how concurrent visits and corrections affect counts before changing workflow.

#### Security and Privacy Considerations

**Technical Recommendation:** any override must follow approved role/audit rules; authorization status must not silently change clinical documentation.

#### Acceptance Criteria

- [ ] **AC1:** Given approved examples for zero, unknown and available visits, when evaluated, then each produces the explicitly agreed warning/suggestion/block behavior.
- [ ] **AC2:** Given a draft of another note type, when authorization status changes, then its type/content is not silently converted.
- [ ] **AC3:** Given simultaneous visit creation, when counts change, then the safeguard uses authoritative consistent utilization.

#### Testing and Verification

After decision, utilization boundaries, multiple policies, concurrency and role/override tests with synthetic records.

#### Rollout and Regression Risks

An aggressive safeguard may block necessary clinical work; an overly permissive one may mislead. Policy must precede implementation.

#### Open Questions

January/operations owner: what triggers the safeguard, is it a warning or enforced block, who may override, and should progress documentation be suggested rather than selected automatically? These block implementation.

**Development impact:** Decision gathering and read-only investigation can proceed; implementation of the undecided policy cannot.

**Dependent criteria:** All safeguard behavior criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

<a id="pr-36"></a>

### PR 36: Support approved clinician-administered outcome measures

**Status:** Blocked by Dependency\
**Category:** Clinical Workflow\
**Priority:** High\
**Confidence:** High\
**Affected Area:** Clinician objective outcome entry; E23 and M14–M17\
**Dependencies:** PR 23 versioned contracts; approved Berg/Tinetti/5xSTS/TUG protocol and trial rules.\
**Source IDs:** R067\
**Verification Status:** Partially Implemented

#### User Problem

Some outcomes require clinician observation or timing and cannot be moved into patient self-report intake.

#### Confirmed Requirement

Preserve clinician administration for measures such as Berg and Tinetti; supplied timed tests belong to an approved clinician workflow rather than patient questionnaires.

#### Background and Evidence

- **R067 — Confirmed Requirement:** Retain clinician entry for administered measures such as Berg/Tinetti. Source: S02 Aligned, Detailed notes/screenshots, Timestamped transcript; 02:15:27; P0054,P0192,P2008–2015. Speakers: January Austin, Admin Black House Developers.

#### Current Behavior

The client explicitly named clinician exceptions; supplied assets contain Berg, Tinetti, 5x sit-to-stand and TUG. Existing aggregate result infrastructure does not establish full protocol-aware entry.

#### Expected Behavior

Authorized clinicians record approved task scores or timed trials, with totals/results calculated according to that exact approved protocol and visible provenance.

#### Scope

Protocol-aware clinician entry and scorer fixtures, phased by approved instrument.

#### Out of Scope

Patient self-administration, unsourced fall-risk interpretations, substituting a different timing endpoint or inventing TUG trial aggregation.

#### Implementation Guidance

**Technical Recommendation:** share PR 23 definitions/results but use clinician-specific administration controls. Retain item scores/trials, units, device/help context only where the approved protocol requires it; distinguish unable/not performed from zero.

#### Data and Migration Considerations

Preserve legacy aggregate scores and historical protocols. Do not recompute prior results under a newly selected version.

#### Security and Privacy Considerations

**Technical Recommendation:** server enforces clinician permission, patient/clinic ownership and immutable signed-note associations.

#### Acceptance Criteria

- [ ] **AC1:** Given an approved Berg or Tinetti definition, when valid item observations are entered, then the total matches approved weighted fixtures.
- [ ] **AC2:** Given an approved timed-test protocol, when trials are recorded, then duration units and the approved endpoint/combination rule are preserved.
- [ ] **AC3:** Given a patient intake link, when requesting clinician-only measure entry, then it is unavailable and the API denies unauthorized submission.
- [ ] **AC4:** Given an incomplete/unable assessment, when saved, then it is not silently scored as a complete zero result.

#### Testing and Verification

Instrument fixtures, conditional Berg instructions, Tinetti item weights, timed trials/units, missing/unable cases, permissions and note-result linkage.

#### Rollout and Regression Risks

Protocol substitution would change clinical meaning. Release each approved instrument independently.

#### Open Questions

Clinical owner confirms the supplied 5xSTS standing endpoint, TUG two-trial aggregation, missing/unable rules and any interpretation labels. Those instruments remain disabled until answered.

**Development impact:** Preparation and independent contract work can proceed; dependent feature completion/activation waits for the named prerequisite.

**Dependent criteria:** Protocol/timing and incomplete-assessment criteria.

#### Definition of Done

- [ ] Confirmed requirements implemented (or decision brief approved before implementation).
- [ ] Feature-specific acceptance criteria pass.
- [ ] Automated tests added or updated where applicable; layout-only verification rationale recorded.
- [ ] Relevant regression tests pass.
- [ ] Permissions and tenant boundaries verified.
- [ ] Data migration verified, when applicable.
- [ ] User-facing and technical documentation updated, when applicable.
- [ ] Client validation evidence recorded, when required.

## 5. Implementation Roadmap

Order is a recommended workstream sequence, not a deadline or a requirement to block independent work behind unanswered decisions. “Coordinate” dependencies are shared-contract/integration checkpoints; material/decision gates must be satisfied before the affected behavior is implemented or activated.

| Order | PR | Title | Status | Priority | Category | Dependencies | Can Run in Parallel |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | [PR 01](#pr-01) | Verify and repair active-use and multi-tab sessions | Verification Required | High | Authentication and Session Management | Coordinate with existing GitHub #153/#154; controlled Web/API build and synthetic accounts. | With 02/03 and decision briefs; coordinate auth stack |
| 2 | [PR 02](#pr-02) | Recover from note-save errors without trapping navigation | Verification Required | High | Bug Fix | PR 01 diagnosis; a safe draft plus failure/conflict fixtures. | With 01/03 after agreeing recovery boundary |
| 3 | [PR 03](#pr-03) | Correct patient timeline timestamps using the agreed display time zone | Verification Required | Medium | Bug Fix | Synthetic note/intake/appointment timestamps; clinic time-zone decision. | With 01/02; independent time-zone decision |
| 4 | [PR 04](#pr-04) | Validate role configuration and visit types against the existing Settings stack | Blocked by Dependency | High | Permissions and Multi-Tenancy | Existing GitHub #153 (services) and #154 (connected UI); approved role matrix. | Validation preparation with 05/33; existing stack owns code |
| 5 | [PR 05](#pr-05) | Decide organization and clinic ownership before migrating tenancy | Needs Clarification | Needs Decision | Permissions and Multi-Tenancy | Client hierarchy and role decision; coordinate #153/#154. | With all single-clinic UI work; gates organization migration |
| 6 | [PR 06](#pr-06) | Decide individual subscription and organization onboarding rules | Needs Clarification | Needs Decision | Needs Clarification | PR 05 decisions; subscription provider, entitlement and payment-failure rules. | Decision research with 05; implementation waits |
| 7 | [PR 15](#pr-15) | Resolve authorization-field placement in patient and policy entry | Needs Clarification | High | Insurance and Authorization | Client decision C01; PR 14 policy consolidation. | Decision in parallel with 14 |
| 8 | [PR 18](#pr-18) | Define clinician handoff and shared-treatment sign-off | Needs Clarification | High | Clinical Workflow | Approved PT/PTA sign-off matrix; existing note lifecycle and notification services. | Decision in parallel; no signature implementation yet |
| 9 | [PR 19](#pr-19) | Add clinician-authored patient alerts to chart and note headers | Needs Clarification | High | Clinical Workflow | Existing patient permissions and shared header; lifecycle acceptance by clinical owner. | Decision in parallel; UI prototype only |
| 10 | [PR 34](#pr-34) | Decide sex and gender terminology, storage and clinical use | Needs Clarification | Needs Decision | Needs Clarification | Client/clinical decision and existing data inventory. | Decision alongside all unrelated work |
| 11 | [PR 35](#pr-35) | Decide the zero-authorization-visit documentation safeguard | Needs Clarification | Needs Decision | Insurance and Authorization | Clinical/operations policy; existing visit accounting and concurrency rules. | Decision alongside 14; no guessed safeguard |
| 12 | [PR 07](#pr-07) | Prefill intake and preserve patient and policy data across entry flows | Ready for Development | High | Data Migration or Persistence | Existing clinic-scoped patient/policy contracts; PR 15 affects only authorization-field placement. | With 09–14 using agreed mapping contract |
| 13 | [PR 08](#pr-08) | Make intake content and repeat encounters follow the destination note type | Ready for Development | High | Intake | PR 07 seed rules; PR 20/30/31 define corresponding questionnaires; PR 24 provides outcome forms. | With 20/30 questionnaire contracts; discharge waits for 31 |
| 14 | [PR 09](#pr-09) | Make intake navigation, review editing and body-map controls clearer | Ready for Development | Medium | UI/UX | Coordinate dynamic steps with PR 08. | With 10/11; coordinate shared wizard edits |
| 15 | [PR 10](#pr-10) | Clarify medical history, medications and functional limitations | Ready for Development | Medium | Intake | PR 08 intake type visibility; existing reference catalog ownership. | With 09/11; shared intake integration checkpoint |
| 16 | [PR 11](#pr-11) | Group PHI release information and remove intake revocation controls | Ready for Development | Medium | Intake | Existing consent packet schema and approved text. | With 09/10 |
| 17 | [PR 12](#pr-12) | Complete patient chart editing and patient-entry actions | Ready for Development | High | Enhancement | Existing patient update API; PR 07 for intake propagation. | With 09–11/14 |
| 18 | [PR 13](#pr-13) | Edit provider-directory entries and carry fax details into care teams | Ready for Development | High | Provider Communication | Existing provider update and approval policy; coordinate Settings stack. | With 14/16; coordinate Settings files |
| 19 | [PR 14](#pr-14) | Consolidate insurance policy and authorization editing | Ready for Development | High | Insurance and Authorization | Existing policy/authorization version contracts; PR 15 decides Authorization Required placement. | With 13/16; exclude disputed field |
| 20 | [PR 16](#pr-16) | Create appointments from empty clinician slots and enforce schedulable roles | Ready for Development | High | Scheduling | Existing clinic schedules and visit types; coordinate PR 04. | With 13/14; coordinate visit types in 04 |
| 21 | [PR 17](#pr-17) | Fax signed notes from review and improve clinic patient selection | Ready for Development | High | Provider Communication | Existing HumbleFax configuration and signed-note export; sandbox credentials for release verification. | With 16/20; sandbox only for final integration |
| 22 | [PR 20](#pr-20) | Implement the agreed daily subjective and objective scope | Ready for Development | High | Clinical Workflow | PR 08 shares questionnaire definition; existing progress question labels provide mapping. | With 21/22/30; coordinate 08 definition |
| 23 | [PR 21](#pr-21) | Represent ROM by movement, side and active/passive measurement | Ready for Development | High | Clinical Workflow | Approved region/movement catalog and existing metric persistence; coordinate PR 22. | With 20/23; establish shared metric contract first |
| 24 | [PR 22](#pr-22) | Separate MMT from joint mobility and reuse ROM movement selections | Ready for Development | High | Clinical Workflow | PR 21 shared movement identifiers; approved MMT/joint grading definitions. | After 21 contract; UI work parallel once stable |
| 25 | [PR 23](#pr-23) | Establish versioned outcome definitions and validate supplied source assets | Ready for Development | High | Technical Improvement | Clinical owner approval of per-instrument version/scoring; materials M01–M17. | With other clinical work and source approvals |
| 26 | [PR 26](#pr-26) | Capture patient personal goals and use them in SMART-goal drafting | Ready for Development | High | Clinical Workflow | PR 07/08 projection conventions; existing clinician-reviewed AI goal workflow. | With 20/21/27; coordinate intake mapping |
| 27 | [PR 27](#pr-27) | Place evaluation assessment narrative after contributing inputs | Ready for Development | Medium | Clinical Workflow | Existing note schema and clinician workflow; separate evidence for reported duplicate/stale fields. | With 26/28; separate layout from AI |
| 28 | [PR 28](#pr-28) | Generate distinct SOAP summaries from their corresponding sections | Ready for Development | High | AI-Assisted Functionality | Existing AI feature flags/provider configuration; clinician-approved synthetic examples. | With 27/29; shared AI contract precedes 32 |
| 29 | [PR 29](#pr-29) | Reproduce and repair goal-generation loss and candidate exhaustion | Verification Required | High | AI-Assisted Functionality | Synthetic reproduction of eight accepted goals; PR 28 context conventions. | Reproduction alongside 28 |
| 30 | [PR 30](#pr-30) | Complete progress activity tracking, improvement and plan frequency | Ready for Development | High | Clinical Workflow | Recorded limitation catalog; PR 08 aligned intake; PR 32 handles AI Suggest. | With 20/21/28; coordinate intake mapping |
| 31 | [PR 33](#pr-33) | Verify and remove redundant Settings scrolling | Verification Required | Low | UI/UX | Integrated #153/#154 UI; supported viewport matrix. | After integrated Settings UI; independent visual verification |
| 32 | [PR 24](#pr-24) | Deliver approved patient questionnaires with automatic scoring | Blocked by Dependency | High | Intake | PR 23 contract; first approved patient instrument cohort; assignment decision. | After 23 and per-instrument approval; content cohorts independent |
| 33 | [PR 36](#pr-36) | Support approved clinician-administered outcome measures | Blocked by Dependency | High | Clinical Workflow | PR 23 versioned contracts; approved Berg/Tinetti/5xSTS/TUG protocol and trial rules. | After 23 and approved protocols; separate from patient renderer |
| 34 | [PR 25](#pr-25) | Carry computed outcomes into clinical notes with provenance | Blocked by Dependency | High | Clinical Workflow | PR 24 computed patient results; PR 36 clinician results; note-type routing in PR 08. | After 24/36 result contracts; patient and clinician projections can be phased |
| 35 | [PR 31](#pr-31) | Implement the approved discharge questionnaire and recommendation choices | Blocked by Dependency | High | Clinical Workflow | Exact referenced discharge document/revision; approved recommendation options; PR 08. | Source retrieval in parallel; content waits |
| 36 | [PR 32](#pr-32) | Make progress and discharge AI actions produce reviewable suggestions | Ready for Development | High | AI-Assisted Functionality | PR 28 section-specific AI contract; PR 31 approved structured fields for full discharge context. | After 28 contract; progress before full discharge context |

Stabilize session/save behavior and establish the current deployed/source baseline first. Reconcile the existing Settings stack before altering its permissions UI. Settle organization ownership before any organization-dependent migration; independent current-clinic work can continue under existing isolation boundaries.

Patient/encounter persistence precedes questionnaire/result carry-forward. Define movement/measurement semantics before splitting MMT or migrating metric data. Establish versioned scoring and approve each instrument before collecting real responses; separate clinician-administered forms from patient forms. A common section-specific AI contract precedes wiring more generation buttons. Missing discharge content blocks that content only, while approved progress work and bounded AI wiring can proceed.

## 6. Feedback Traceability Matrix

Each distinct intent appears exactly once as a primary row. Repeated section statements are aliases in the Source cell. Evidence Status is the source classification; PR verification status is separately recorded in each brief. “Ready” dispositions on specific requirements do not override a parent brief’s decision/dependency gate.

| Requirement ID | Client Feedback or Requirement | Source | Evidence Status | Assigned PR | Disposition |
| --- | --- | --- | --- | --- | --- |
| <a id="r001"></a>R001 | Multi-clinic support and persistence need a sound hierarchy | S02: 03:27–04:38; 02:45:29; P0132,P0270–0287,P2417–2437 | Technical Work | [PR 05](#pr-05) | Needs Clarification |
| <a id="r002"></a>R002 | Remove the extra Settings scrollbar | S02: 06:04; P0018,P0069,P0300–0308 | Confirmed Requirement | [PR 33](#pr-33) | Verification Required |
| <a id="r003"></a>R003 | Local/beta screens and button behavior differ | S02: 07:23; 10:59; P0135,P0138,P0314–0362 | Reported Defect | [PR 01](#pr-01) | Verification Required |
| <a id="r004"></a>R004 | Admin Select Role to Configure is unusable | S02: 08:47; P0070,P0136,P0331 | Reported Defect | [PR 04](#pr-04) | Verification Required |
| <a id="r005"></a>R005 | Expired sessions leave apparently active pages without data | S02: 12:00; P0357–0366 | Reported Defect | [PR 01](#pr-01) | Verification Required |
| <a id="r006"></a>R006 | Save-error banners block note/evaluation navigation and require relogin | S02: 12:59; 01:56:16; 02:01:46; P0139,P0183,P0375,P1732–1747,P1822–1825 | Reported Defect | [PR 02](#pr-02) | Verification Required |
| <a id="r007"></a>R007 | Patient timeline date/time appears incorrect | S02: 13:53–15:01; P0139,P0384–0399 | Reported Defect | [PR 03](#pr-03) | Verification Required |
| <a id="r008"></a>R008 | Start New Note should scroll/focus the note-type choices | S02: 23:31; P0486; screenshot P0146 | Confirmed Requirement | [PR 12](#pr-12) | Ready for Development |
| <a id="r009"></a>R009 | Keep intake review progress labels in one row | S02: 19:06; P0071,P0142,P0430; screenshot P0143 | Confirmed Requirement | [PR 09](#pr-09) | Ready for Development |
| <a id="r010"></a>R010 | Restrict full history/pain intake; later retain full evaluation/re-evaluation flow | S02: 20:15; 02:04:17–02:05:31; P0019,P0050,P0072,P0109–0110,P0441–0445,P1851–1869 | Confirmed Requirement | [PR 08](#pr-08) | Ready for Development |
| <a id="r011"></a>R011 | Patients answer outcome questionnaires and the system calculates scores | S02: 21:15–28:43; P0020,P0145,P0453,P0495–0499,P0569–0571 | Confirmed Requirement | [PR 24](#pr-24) | External Dependency |
| <a id="r012"></a>R012 | Client supplies outcome-measure PDFs/information (16 PDFs now received; approval tracked in R095) | S02: 26:15–27:23; 02:53:55; 03:00:35; P0073,P0125,P0525–0555,P2573,P2679–2682; supplied S04/S05 | External Dependency | [PR 23](#pr-23) | Process Action |
| <a id="r013"></a>R013 | Simplify/remove confusing patient intake instructions | S02: 29:49; P0021,P0074,P0147,P0579–0593 | Confirmed Requirement | [PR 09](#pr-09) | Ready for Development |
| <a id="r014"></a>R014 | Rename comorbidities to past medical history | S02: 30:46–34:52; P0022,P0075,P0598–0600,P0647 | Confirmed Requirement | [PR 10](#pr-10) | Ready for Development |
| <a id="r015"></a>R015 | Make medication and medical-history multi-selection explicit | S02: 31:49–34:52; P0022,P0075,P0620,P0628,P0633,P0645–0647 | Confirmed Requirement | [PR 10](#pr-10) | Ready for Development |
| <a id="r016"></a>R016 | Allow Other/free entry for missing medical-history options; medication Other is also in aligned wording | S02: 33:27–34:52; P0022,P0075,P0633,P0647–0651 | Confirmed Requirement | [PR 10](#pr-10) | Ready for Development |
| <a id="r017"></a>R017 | Choose sex/gender terminology and data use | S02: 35:59–39:33; P0013,P0150,P0663,P0697,P0715,P0735–0744 | Needs Clarification | [PR 34](#pr-34) | Needs Clarification |
| <a id="r018"></a>R018 | Make front/back body-map switching visibly actionable | S02: 30:46–31:49; P0076,P0608,P0613–0615 | Confirmed Requirement | [PR 09](#pr-09) | Ready for Development |
| <a id="r019"></a>R019 | Review Edit should navigate and focus the exact component | S02: 40:23–41:25; P0023,P0077,P0151,P0756–0771 | Confirmed Requirement | [PR 09](#pr-09) | Ready for Development |
| <a id="r020"></a>R020 | Prefill known demographics/insurance into created and sent intakes | S02: 43:01; P0024,P0078,P0152,P0784–0791 | Confirmed Requirement | [PR 07](#pr-07) | Ready for Development |
| <a id="r021"></a>R021 | Group PHI release consent with authorized contacts/additional details | S02: 44:01–45:03; P0025,P0079,P0153,P0800–0822 | Confirmed Requirement | [PR 11](#pr-11) | Ready for Development |
| <a id="r022"></a>R022 | Expand functional limitation groups initially | S02: 45:03–46:16; P0026,P0080,P0154,P0831–0842 | Confirmed Requirement | [PR 10](#pr-10) | Ready for Development |
| <a id="r023"></a>R023 | Replace provider approval/rejection with edit/delete | S02: 47:11–48:08; P0027,P0081,P0155,P0859–0872 | Confirmed Requirement | [PR 13](#pr-13) | Ready for Development |
| <a id="r024"></a>R024 | Make all existing basic patient fields editable, including address/phone/emergency contact | S02: 48:08–49:49; P0028,P0082–0083,P0157,P0873–0891 | Confirmed Requirement | [PR 12](#pr-12) | Ready for Development |
| <a id="r025"></a>R025 | Fax Center patient lookup should offer current-clinic patient choices | S02: 51:13; P0084,P0159,P0907 | Confirmed Requirement | [PR 17](#pr-17) | Ready for Development |
| <a id="r026"></a>R026 | Add care-team fax and carry selected provider details into intake | S02: 52:23–53:28; P0029,P0085,P0915–0919,P0930–0934 | Confirmed Requirement | [PR 13](#pr-13) | Ready for Development |
| <a id="r027"></a>R027 | Care-team email was proposed then explicitly declined | S02: 52:23; P0029 versus P0920–0921 | Context Only | — | Context Only |
| <a id="r028"></a>R028 | Expose Add Patient on the patient page | S02: 52:23; P0086,P0922–0924 | Confirmed Requirement | [PR 12](#pr-12) | Ready for Development |
| <a id="r029"></a>R029 | Remove authorization number from initial Add Patient | S02: 54:37; P0030,P0087,P0950–0953 | Confirmed Requirement | [PR 15](#pr-15) | Needs Clarification |
| <a id="r030"></a>R030 | Resolve removal versus later addition of Authorization Required in Add Patient/policy | S02: 54:37 versus 01:26:25–01:28:13; P0030,P0039,P0087,P0095,P0946–0953,P1323–1332 | Needs Clarification | [PR 15](#pr-15) | Needs Clarification |
| <a id="r031"></a>R031 | Carry applicable Add Patient/Add Appointment data into profile and primary/secondary policies | S02: 54:37; 01:26:25; P0086,P0953,P1319–1322 | Confirmed Requirement | [PR 07](#pr-07) | Ready for Development |
| <a id="r032"></a>R032 | Admin Today shows clinician columns; Week can select a clinician | S02: 55:44–01:04:20; P0014,P0162,P0971–0995,P1027,P1088–1089 | Requested Enhancement | [PR 16](#pr-16) | Ready for Development |
| <a id="r033"></a>R033 | Click empty appointment slot to prefill date/time/clinician | S02: 59:24–01:00:20; P0031,P0088,P0164,P1027–1036 | Confirmed Requirement | [PR 16](#pr-16) | Ready for Development |
| <a id="r034"></a>R034 | Restrict schedulable choices to PT and PTA | S02: 01:00:20; P0032,P0089,P1044–1047 | Confirmed Requirement | [PR 16](#pr-16) | Ready for Development |
| <a id="r035"></a>R035 | Show clinician/hour slots without existing bookings | S02: 01:01:48–01:02:32; P0033,P0090,P0166,P1060–1073 | Confirmed Requirement | [PR 16](#pr-16) | Ready for Development |
| <a id="r036"></a>R036 | Horizontal scroll across multiple clinician columns accepted | S02: 01:04:20; P0167,P1088–1089 | Context Only | — | Context Only |
| <a id="r037"></a>R037 | Configure additional visit types in Settings | S02: 01:04:20; P0168,P1089–1097; GitHub #153/#154 | Confirmed Requirement | [PR 04](#pr-04) | Verification Required |
| <a id="r038"></a>R038 | Fax a signed note from review using fax-center-equivalent composition | S02: 01:05:40–01:07:44; P0034–0035,P0091,P0169,P1108–1144 | Confirmed Requirement | [PR 17](#pr-17) | Ready for Development |
| <a id="r039"></a>R039 | Possibly select multiple notes for one fax | S02: 01:07:44–01:08:58; P1143–1151 | Possible Future Idea | — | Possible Future Idea |
| <a id="r040"></a>R040 | Patient portal schedule suffices; schedule faxing unnecessary | S02: 01:08:58; P0170,P1151–1154 | Context Only | — | Context Only |
| <a id="r041"></a>R041 | Support PTA/PT shared treatment completion and sign-off responsibilities | S02: 01:08:58–01:11:16; P0171,P1155–1182 | Needs Clarification | [PR 18](#pr-18) | Needs Clarification |
| <a id="r042"></a>R042 | Add Send to between PT/PTA and a notification for each forwarded note | S02: 01:12:54–01:14:26; P0036,P0092,P0172,P1191–1203 | Confirmed Requirement | [PR 18](#pr-18) | Needs Clarification |
| <a id="r043"></a>R043 | Clinician-authored patient Notifications tab with persistent note-header alerts | S02: 01:16:05–01:21:20; P0037,P0093,P0173,P1212–1262 | Confirmed Requirement | [PR 19](#pr-19) | Needs Clarification |
| <a id="r044"></a>R044 | Remove duplicate Insurance & Authorization entry below chart tabs | S02: 01:19:58; P0094,P1254 | Confirmed Requirement | [PR 14](#pr-14) | Ready for Development |
| <a id="r045"></a>R045 | Move visit limit/calendar or plan year into policy entry and remove duplicate adjuster fields | S02: 01:24:19–01:25:21; P0038,P0095,P0174,P1288–1311 | Confirmed Requirement | [PR 14](#pr-14) | Ready for Development |
| <a id="r046"></a>R046 | Combine authorization/PCP/referral settings, details and editable history; save once | S02: 01:28:13–01:33:47; P0040,P0096,P0175,P1332–1399 | Confirmed Requirement | [PR 14](#pr-14) | Ready for Development |
| <a id="r047"></a>R047 | Remove intake consent-revocation controls | S02: 01:33:47; P0041,P0097,P0176,P1400–1404 | Confirmed Requirement | [PR 11](#pr-11) | Ready for Development |
| <a id="r048"></a>R048 | Prevent inactivity timeout during active app use | S02: 01:35:34; P0042,P0098,P0177,P1413–1414 | Confirmed Requirement | [PR 01](#pr-01) | Verification Required |
| <a id="r049"></a>R049 | Use appropriate multi-select/dropdowns in daily subjective, retaining free-text additional information | S02: 01:36:54–01:40:56; P0043,P0099,P0178,P1429,P1468–1476 | Confirmed Requirement | [PR 20](#pr-20) | Ready for Development |
| <a id="r050"></a>R050 | Replace daily chief-complaint carry-forward with top-of-form pain details/body-region data | S02: 01:38:09–01:39:15; P0099–0100,P1452–1475 | Confirmed Requirement | [PR 20](#pr-20) | Ready for Development |
| <a id="r051"></a>R051 | Daily question set keeps 1,3,4,5,6,7,10; removes 2,8,9,11,12,13 | S02: 01:45:45–01:48:15; P0044,P0101,P0179,P1572–1621 | Confirmed Requirement | [PR 20](#pr-20) | Ready for Development |
| <a id="r052"></a>R052 | Earlier instruction to remove progress question 8 may be a note-type mix-up | S02: 01:41:51–01:43:35; P1503–1547 | Needs Clarification | [PR 30](#pr-30) | Needs Clarification |
| <a id="r053"></a>R053 | ROM shows selected-region movements and permits switching/additional regions | S02: 01:49:45–01:51:29; P0045,P0102,P0180,P1636–1666 | Confirmed Requirement | [PR 21](#pr-21) | Ready for Development |
| <a id="r054"></a>R054 | Show normal value before current value | S02: 01:51:29; P0103,P1668–1669 | Confirmed Requirement | [PR 21](#pr-21) | Ready for Development |
| <a id="r055"></a>R055 | Support left/right comparisons for paired regions, without artificial spine sides | S02: 01:51:29–01:54:55; P0046,P0104,P1670–1687,P1720 | Confirmed Requirement | [PR 21](#pr-21) | Ready for Development |
| <a id="r056"></a>R056 | Separate MMT and joint mobility, including their grading | S02: 01:53:46; 02:14:24; P0047,P0105,P0181,P1694–1699,P1996 | Confirmed Requirement | [PR 22](#pr-22) | Ready for Development |
| <a id="r057"></a>R057 | ROM has active/passive current, normal and prior values for each applicable side | S02: 01:53:46–01:54:55; P0048,P0106,P1702–1717 | Confirmed Requirement | [PR 21](#pr-21) | Ready for Development |
| <a id="r058"></a>R058 | Remove new functional outcome entry from daily objective | S02: 01:54:55; P0049,P0107,P0182,P1720–1723 | Confirmed Requirement | [PR 20](#pr-20) | Ready for Development |
| <a id="r059"></a>R059 | Evaluation subjective reportedly repeats frequency questions | S02: 01:57:11–01:58:17; P0184,P1755–1767 | Reported Defect | [PR 27](#pr-27) | Verification Required |
| <a id="r060"></a>R060 | Employment/imaging information initially thought missing was found or left uncertain | S02: 01:59:22–02:02:58; P1793–1797,P1834–1841 | Context Only | — | Context Only |
| <a id="r061"></a>R061 | New tab inherits browser sign-in without removing anti-piggyback/session protection | S02: 02:00:24; P0108,P0185,P1805–1809 | Confirmed Requirement | [PR 01](#pr-01) | Verification Required |
| <a id="r062"></a>R062 | Older intake/limitations controls appeared missing then reappeared | S02: 02:06:42–02:10:47; P0188,P1882–1946 | Reported Defect | [PR 08](#pr-08) | Verification Required |
| <a id="r063"></a>R063 | Use Difficulty with instead of Unable in app-authored functional limitations | S02: 02:09:50; P0051,P0111,P1919–1927 | Confirmed Requirement | [PR 10](#pr-10) | Ready for Development |
| <a id="r064"></a>R064 | Capture personal goal after limitations and carry into clinician-reviewed SMART-goal work | S02: 02:11:59–02:13:24; P0052,P0112,P0190,P1954–1975 | Confirmed Requirement | [PR 26](#pr-26) | Ready for Development |
| <a id="r065"></a>R065 | Reuse ROM-selected movements as MMT selections, not ROM numeric values | S02: 02:14:24; P0053,P0113,P0191,P1989–1996 | Confirmed Requirement | [PR 22](#pr-22) | Ready for Development |
| <a id="r066"></a>R066 | Carry calculated intake outcomes into evaluation/progress/discharge as read-only results | S02: 02:15:27–02:16:49; P0054,P0114,P0192,P2004–2020 | Confirmed Requirement | [PR 25](#pr-25) | External Dependency |
| <a id="r067"></a>R067 | Retain clinician entry for administered measures such as Berg/Tinetti | S02: 02:15:27; P0054,P0192,P2008–2015 | Confirmed Requirement | [PR 36](#pr-36) | External Dependency |
| <a id="r068"></a>R068 | Assessment allegedly shows previous rather than current data; client could not identify field | S02: 02:16:49–02:18:08; P2021–2028 | Needs Clarification | [PR 27](#pr-27) | Needs Clarification |
| <a id="r069"></a>R069 | Place clinical assessment narrative after its contributing evaluation inputs | S02: 02:18:08–02:19:35; P0055,P0115,P0193,P2030–2042 | Confirmed Requirement | [PR 27](#pr-27) | Ready for Development |
| <a id="r070"></a>R070 | AI goal suggestions regress in quality relative to initial goals | S02: 02:19:35; P0194,P2042–2048 | Reported Defect | [PR 29](#pr-29) | Verification Required |
| <a id="r071"></a>R071 | AI goals disappear during demonstration and yield no new candidates after eight accepted | S02: 02:20:46–02:21:55; P0194,P2059–2083 | Reported Defect | [PR 29](#pr-29) | Verification Required |
| <a id="r072"></a>R072 | Short/long-term goal distinction concern withdrawn after seeing weeks | S02: 02:20:46; P2069–2073 | Context Only | — | Context Only |
| <a id="r073"></a>R073 | Generate distinct S/O/A/P summary paragraphs from corresponding note sections | S02: 02:21:55–02:25:38; 02:30:51; 02:35:35; P0056,P0116,P0195,P2095–2131,P2228–2232,P2299 | Confirmed Requirement | [PR 28](#pr-28) | Ready for Development |
| <a id="r074"></a>R074 | Keep intake questionnaires aligned with destination note subjective questions | S02: 17:57; 02:25:38; P0421–0423,P2132–2133 | Confirmed Requirement | [PR 08](#pr-08) | Ready for Development |
| <a id="r075"></a>R075 | Progress additional-information field was already visible in meeting | S02: 02:25:38; P2134–2137 | Context Only | — | Context Only |
| <a id="r076"></a>R076 | Progress improved/same/worse choices come from recorded intake/evaluation activities | S02: 02:26:38–02:27:51; P0057,P0117,P0196,P2148–2166 | Confirmed Requirement | [PR 30](#pr-30) | Ready for Development |
| <a id="r077"></a>R077 | Newly difficult activities use full relevant catalog and persist for later visits | S02: 02:28:44–02:29:44; P0118–0119,P2175–2199 | Confirmed Requirement | [PR 30](#pr-30) | Ready for Development |
| <a id="r078"></a>R078 | Progress plan of care permits frequency adjustment; do not add it to daily | S02: 02:29:44–02:30:51; P0058,P0120,P0197,P2202–2221 | Confirmed Requirement | [PR 30](#pr-30) | Ready for Development |
| <a id="r079"></a>R079 | Add AI Suggest to progress Plan for Next Visit | S02: 02:30:51; P0121,P2222–2227 | Confirmed Requirement | [PR 32](#pr-32) | Ready for Development |
| <a id="r080"></a>R080 | Add percent-improved field to progress subjective | S02: 02:32:09; P0059,P0122,P0198,P2245–2250 | Confirmed Requirement | [PR 30](#pr-30) | Ready for Development |
| <a id="r081"></a>R081 | Discharge recommendations use multi-select/dropdown inputs | S02: 02:30:51–02:32:09; P0060,P0123,P0198,P2234–2243 | Confirmed Requirement | [PR 31](#pr-31) | External Dependency |
| <a id="r082"></a>R082 | Discharge subjective follows referenced document: goals, pain, remaining difficulty, improvement; reason is clinician input | S02: 02:33:32–02:34:30; P0061,P0124,P0198,P2275–2287 | Confirmed Requirement | [PR 31](#pr-31) | External Dependency |
| <a id="r083"></a>R083 | Discharge recommendation/follow-up generation and summary button need functioning behavior | S02: 02:35:35; P0199,P2295–2304 | Reported Defect | [PR 32](#pr-32) | Ready for Development |
| <a id="r084"></a>R084 | Individual paid subscribers should get immediate access, EMR-only, without adding staff | S02: 02:37:14–02:43:22; P0015,P0200,P2315–2396 | Proposed Feature | [PR 06](#pr-06) | Needs Clarification |
| <a id="r085"></a>R085 | Organization customers use demo/onboarding, then local owners add clinicians without platform-owner approval | S02: 02:41:02–02:45:29; P0200,P2368–2429 | Proposed Feature | [PR 05](#pr-05) | Needs Clarification |
| <a id="r086"></a>R086 | Client explores and confirms organization/signup alternatives | S02: 02:46:34; 02:52:58; P0202,P2443–2448,P2565 | External Dependency | [PR 06](#pr-06) | Process Action |
| <a id="r087"></a>R087 | Third-party EMR permits over-authorization scheduling and blocks signing | S02: 02:54:47; P0204,P2595 | Context Only | — | Context Only |
| <a id="r088"></a>R088 | Third-party billing AI duplicates copay tasks and delays balances | S02: 02:54:47–02:56:56; P0204–0205,P2597–2621 | Context Only | — | Context Only |
| <a id="r089"></a>R089 | PTDoc safeguard at zero authorized visits could default to progress documentation | S02: 02:57:56; P0206,P2623–2633 | Possible Future Idea | [PR 35](#pr-35) | Needs Clarification |
| <a id="r090"></a>R090 | Developer messages client after each app update | S02: 03:00:35; P0062,P0126,P0208,P2683–2699 | Confirmed Requirement | — | Process Action |
| <a id="r091"></a>R091 | Client captures screenshot/context if note-navigation error recurs | S02: 21:15–22:28; P0458–0471 | External Dependency | [PR 02](#pr-02) | Process Action |
| <a id="r092"></a>R092 | Fatigue, travel/work stories, personal conversation, AI ethics anecdotes, hiring story | S02: 01:29–03:27; 02:47:36–02:53:55; 02:58:56–03:00:35; P0131,P0207 and transcript | Out of Scope | — | Out of Scope |
| <a id="r093"></a>R093 | Brief visual comments about white bars/selected-body illumination/pain-severity position lack a clear requested change | S02: 01:26:25; P1323 | Needs Clarification | [PR 09](#pr-09) | Needs Clarification |
| <a id="r094"></a>R094 | Obtain exact discharge workflow revision referenced during meeting | S02: 02:33:32–02:34:30; P2269–2287 | External Dependency | [PR 31](#pr-31) | External Dependency |
| <a id="r095"></a>R095 | Approve instrument versions, assignment, missing-answer rules and anomalous supplied content | S04/S05: Supplied PDF set; see material inventory M01–M17; R011/R012 | Needs Clarification | [PR 23](#pr-23) | Needs Clarification |

## 7. Contradictions and Clarifications

Questions below are neutral decision prompts for a future client review; none was sent externally. Owners are proposed roles, not newly assigned commitments. “Proceed” means within the bounded confirmed scope only.

| ID | Requirements | Conflicting or incomplete evidence | Exact decision needed | Neutral question | Best-positioned owner | Affected work and independent progress |
| --- | --- | --- | --- | --- | --- | --- |
| C01 | R029–R030 | 54:37 removes authorization fields; 01:26:25–01:28:13 moves between new-policy and Add Patient language. | Field location and Unknown/Yes/No semantics. | Should Authorization Required appear at initial Add Patient, only in policy entry after verification, or both; should Unknown remain available? | January / benefits operations | 15; 14 proceeds without the disputed toggle. Number removal can be a separate approved slice. |
| C02 | R017 | 35:59–39:33 ends without a labeling/clinical-use decision. | Purpose, label, values, requiredness, visibility and migration for each field. | What information is clinically needed, what should each field be called, and how should existing values be handled? | January / clinical owner | 34; all unrelated demographic edits proceed. |
| C03 | R032–R035 | Early layout uncertainty is largely resolved by 55:44–01:04:20, but role defaults/off-duty roster and multi-role users are not explicit. | Remaining schedule visibility/default and eligibility edges. | Should unbooked off-duty PT/PTA staff appear, what is the default non-admin view, and is an admin who also has a PT/PTA role schedulable? | January / scheduling owner | 16; empty-slot and approved Admin layout work can proceed under existing roster rules. |
| C04 | R084,R086 | Immediate individual paid access is favored; 02:46:34 leaves signup alternatives for further exploration. | Approve commercial workflow, entitlements and failed-payment/legacy-user handling. | Is the proposed individual EMR-only immediate-access flow approved, and what event grants or removes access? | January / business owner | 06; UI polish and existing approved-user flows proceed. |
| C05 | R001,R085 | Developer proposes organization above clinic; client wants local staff onboarding without platform-owner approval. | Organization/clinic ownership, membership and clinical-access boundaries. | Who owns each organization and clinic, who may invite/approve clinicians, and which clinics’ patient records may each role access? | January with engineering/security | 05 and organization-dependent 06; current-clinic improvements proceed. |
| C06 | R004,R037 | Meeting permission issue versus current preview UI and open #153/#154 stack. | Approved role matrix and integrated build baseline. | Which roles should administrators configure, and can we validate the integrated Settings build against that matrix before adding new work? | Engineering owner; January approves capabilities | 04; prepare tests and independent UI work; avoid duplicate implementation. |
| C07 | R003,R005–R006,R048,R061,R091 | Local/beta discrepancies, intermittent freezes and anti-piggyback protection; source already contains recovery controls. | Exact release/session invariant and synthetic reproducer. | On which build and sequence does the failure occur, and what session protection must remain when a second tab opens? | Engineering; January supplies recurrence context | 01/02; diagnosis proceeds, guessed fixes do not. |
| C08 | R007 | Timeline display is reported wrong; source UTC-to-local conversion does not define desired display zone. | Clinic versus viewer timezone. | Should timeline times use the clinic’s timezone or the viewer’s timezone, including when those differ? | January / product owner | 03; conversion diagnosis proceeds; display acceptance waits. |
| C09 | R051–R052 | Earlier progress question-8 removal conflicts with later explicit daily-number list. | Whether progress question 8 is also removed. | Was the earlier question-8 deletion intended for progress notes, or only for the daily questionnaire discussed afterward? | January | 20 follows final daily list; 30 additions proceed, progress deletion waits. |
| C10 | R021,R047 | Intake revocation controls are removed; no broader consent-policy decision is recorded. | Keep historical revocation evidence and identify separate operational process. | Does this change remove only the intake controls, with existing revocations and the authorized revocation process preserved? | Clinic consent/policy owner | 11 layout/control removal proceeds under preserved-history rule; no broader policy change. |
| C11 | R023,R026 | Edit/delete approved, but directory lifecycle, pending-entry transition and maintainer roles are unstated. | Archive versus hard delete and existing authorized-maintainer behavior. | May Delete remove an entry from future selection while preserving past references, and who may maintain shared provider entries? | January / directory owner / security | 13 can implement existing authorized editing and safe removal design; hard deletion/new privileges wait. |
| C12 | R041–R042 | Shared-treatment sign-off discussion and Send to wording alternate between patient/file/note. | Handoff unit, eligible states/recipients, ownership and signing responsibilities. | Does Send to forward one note for collaboration, reassign it, or request review, and who completes/signs a shared PT/PTA visit? | January / clinical owner | 18 blocked on policy; signed-note fax 17 proceeds. |
| C13 | R043 | Alerts requested, but edit/retire permissions and relationship to signed-note views are absent. | Authoring lifecycle and display semantics. | Who may edit or retire another clinician’s alert, and should signed-note screens show current patient alerts separately from the signed record? | January / clinical owner | 19 needs clarification; prototype only. Other chart work proceeds. |
| C14 | R053–R057,R065 | Bilateral/active-passive requested; old measurements lack an explicit mode. Separate grade catalogs already exist. | Legacy unknown-mode display, reference-value provenance and any mapping exceptions. | Can old unclassified ROM values remain explicitly legacy/unknown, and are the existing normal-value and grade catalogs the approved references? | Clinical owner / engineering | 21/22 structural work proceeds; no invented values or legacy reinterpretation. |
| C15 | R011–R012,R066–R067,R095 | Forms differ in version, completeness, scoring and administration. See M01–M17. | Exact variants, assignment, unanswered/N/A/unable rules, clinical fixtures and permitted reproduction. | Which exact versions should be used, who completes each, when are they assigned, and which approved scoring rules/examples govern incomplete responses? | January / clinical owner; source-rights owner as needed | 23 contract ready; 24/36 activation blocked per instrument; 25 follows completed results. |
| C16 | R059,R068 | Evaluation frequency duplication reported; current-versus-prior issue could not be located at 02:16:49–02:18:08. | Exact field/route and expected-versus-actual example. | Which field is duplicated or shows an old value, and what should it show for a safe example encounter? | January with engineering | 27 ordering proceeds; no speculative data deletion/fix. |
| C17 | R070–R071,R073,R079,R083 | Quality/lost candidates reported; no literal eight-item limit found. Shared/stub AI paths are source verified. | Quality rubric, good/bad synthetic examples and exact goal state at failure. | What distinguishes an acceptable suggestion, and were the eight goals saved, accepted or only selected when new suggestions stopped? | January / clinical reviewer; engineering for reproduction | 28/32 contract wiring proceeds; 29 diagnosis and quality acceptance need evidence. |
| C18 | R081–R082,R094 | A specific discharge document is referenced but absent from supplied files. | Exact revision, questionnaire wording, recommendation choices and input ownership. | Please identify the discharge document/revision and approved recommendation choices, including which fields are patient versus clinician input. | January / clinical owner | 31 blocked; progress AI and existing-field wiring in 32 can proceed. |
| C19 | R060,R062 | Employment/imaging and older intake controls reappeared during the meeting. | Stale build/session versus reproducible missing functionality. | If these controls are still absent, which release, note type and saved record reproduce it? | Engineering; January supplies context if recurring | 08 type work proceeds; no duplicate employment/imaging fields. |
| C20 | R093 | P1323 contains a fragmented white-bars/body-illumination/pain-position aside. | Identify control and desired visual result. | Which screen/control should change, and what should it look like or do after the change? | January | 09 agreed navigation proceeds; this aside is not implemented by guesswork. |
| C21 | R089 | 02:57:56 explicitly links zero-visit progress documentation to PTDoc, but suggests rather than defines behavior. | Trigger, warning/block/suggestion, override and note-type behavior. | At zero remaining visits, should PTDoc warn, suggest a progress note or block an action, and who may override it? | January / clinical and benefits operations | 35 needs decision; 14 consolidation proceeds. |
| C22 | R020,R031 | Prefill/persistence covers created and sent intakes; legacy blanks may be intentional. | Safe precedence when saved profile data conflicts with existing answers or unknown blank provenance. | Should existing answers always win, with known profile data offered for confirmation when an old blank’s intent is unknown? | January / workflow owner; engineering for provenance | 07 can seed new/known untouched fields; no silent overwrite of ambiguous legacy answers. |
| C23 | R045–R046 | Editable history requested but correction versus destructive edit semantics unstated. | Allowed fields and audit/correction treatment for historical authorization/referral entries. | Which historical fields may be corrected in place, and which changes should create a new attributable entry? | January / benefits operations | 14 consolidation proceeds with existing integrity rules; new historical-edit powers wait. |
| C24 | R008,R028,R064 | Patient-page action and personal-goal placement are explicit; final wording/placement still needs routine acceptance. | Placement among existing chart/list actions and approved prompt wording. | Does the proposed patient-page Add Patient action and personal-goal prompt after limitations match the intended workflow? | January / product owner | 12/26 ready; this is acceptance review, not an unresolved core feature. |
| C25 | R002 | Scrollbar complaint predates current Settings stack. | Reproducer on integrated Settings UI and supported viewport. | Does the extra scrollbar remain in the integrated Settings build, and at which viewport/zoom? | Engineering; January validates | 33 verification required; independent UI work proceeds. |

### Conflicts reconciled by stronger/later evidence

| Requirements | Tension | Recorded disposition |
| --- | --- | --- |
| R010 | Earlier evaluation-only shorthand versus 02:04:17–02:05:31 full evaluation/re-evaluation | Later explicit rule includes re-evaluation; daily/progress/discharge differ; all retain review. |
| R011,R067 | Aligned P0020 says patients only; 02:15:27 names clinician exceptions | Patient self-report forms are patient answered; Berg/Tinetti and approved administered tests remain clinician entered. |
| R026,R027 | Aligned P0029 includes email; January declines it at P0920 | Fax proceeds; email is excluded. |
| R019 | Aligned P0023 says in-place; transcript and next step P0077 agree navigate/focus | Exact-component navigation/focus is required; true inline editing is optional, not promised. |
| R032,R036 | Early Today/Week uncertainty versus later acceptance | Admin Today clinician columns, Week clinician selection and horizontal scroll accepted; residual edge decisions are C03. |
| R051,R052 | Daily/progress screen switching in middle of questionnaire discussion | Final explicit numbered list governs daily only; progress question 8 remains C09. |
| R065 | Summary says ROM data persistence into MMT | Interpret as selected movements, not numeric ROM degrees converted to strength grades. |
| R072 | Goal short/long-term concern withdrawn on seeing weeks | Do not build a new short/long-term classification feature from the withdrawn concern. |
| R087–R090 | Third-party complaints and developer update commitment appear in product summary | No third-party billing integration or in-app release-notification feature; PTDoc safeguard remains C21 and update messages remain process. |
| R011,R095 | Transcript tentatively calls LEFS a 12-question measure; supplied file has 20 | Use the supplied 20-item form only after version approval, not tentative spoken count. |

## 8. External Dependencies and Client-Provided Materials

### Received outcome materials

**Received and inspected does not mean clinically approved for activation.** S04 contains 12 PDFs; S05 contains four PDFs. They contain 17 named instruments because the standard ODI file also contains Roland–Morris, plus a separate body diagram. The following facts describe the supplied files; they are not independent medical guidance or a claim that these versions are authoritative.

| ID | Exact archive member / location | Pages | Administration and content | Scoring/protocol evidenced by supplied file | Remaining gate before activation |
| --- | --- | --- | --- | --- | --- |
| M01 | S04: Pelvic Floor Impact Questionnaire (PFIQ-7).pdf | 2 | Patient; 7 rows × 3 domains | Each domain: mean answered response × 100/3; domains 0–100, sum 0–300. | Approve handling of an entirely unanswered domain, assignment and exact version. |
| M02 | S04: Oswestry-disability-questionnaire.pdf, pp.1–2 | 4 total | Patient; standard ODI, 10 sections | Answered total / (5 × answered sections) × 100; file gives an omitted-section example. | Confirm standard versus Modified ODI; metric distances and sex-life item differ from M04. Do not merge versions. |
| M03 | S04: Oswestry-disability-questionnaire.pdf, p.3; body diagram on p.4 | Same file | Additional Roland–Morris 24-statement instrument; p.4 is a body diagram | Distinct instrument discovered inside the ODI PDF, not another ODI page. | Confirm whether Roland–Morris is intended for PTDoc and supply approved scoring/completion rules. Body diagram is an asset, not an additional scored measure. |
| M04 | S04: Modified Oswestry.pdf | 2 | Patient; modified ODI, 10 sections | Answered-section denominator is described; pain medication and employment/homemaking content differs from M02. | Select exact intended variant(s) and version identifiers; never silently substitute for standard ODI. |
| M05 | S04: pelvic-floor-disability-index - 20.pdf | 2 | Patient; 20 items, groups of 6/8/6 | 0 for not present, distress responses 1–4; domain means × 25 and summed domains. | Title says Disability Index and second-page text has PFSI-20 typo; approve canonical identifier/wording and missing-domain rules. |
| M06 | S04: hoos.pdf | 3 | Patient; 40 items in five domains (5/10/17/4/4) | Item choices are supplied; no complete usable scoring/missing-answer specification was found. | Page 3 SP3 says injured knee despite hip instrument; obtain corrected approved source and domain scoring/missing rules. |
| M07 | S04: quick-dash.pdf | 1 | Patient; 11 core items | (sum of answered responses / n − 1) × 25; no more than one missing, so n ≥ 10. Formula visually verified. | Approve version/assignment. Optional work/sport modules were not supplied and are not included. |
| M08 | S04: Neck Disability Index.pdf | 1 | Patient; 10 six-choice sections | Form shows total / 50; exact incomplete-response behavior not specified. | Approve missing-answer rules and unusual printed section ordering/numbering; do not reorder solely from extracted text. |
| M09 | S04: pelvic-function-ppiq.pdf | 1 | Patient; PPIQ, 8 scored items plus 2 optional intimate items | 0–4 scored responses; optional intimate items explicitly excluded. 0–32 is arithmetically derived from eight items, not an independently approved scoring policy. | Confirm total/missing rules and exact version; preserve optionality and exclusion of the two extra items. |
| M10 | S04: FAAM.pdf | 2 | Patient; 21 ADL items and 7 visible sports rows | N/A choices supplied; complete scoring/missing-response rules absent. | Verify intended sports variant/completeness and approved scoring. Seven visible rows were visually confirmed; no replacement row is invented. |
| M11 | S04: Patient-Rated-Wrist-Evaluation.pdf | 1 | Patient; 5 pain + 10 function items, 0–10 | Form permits blanks for activities never done; full weighting/blank-handling rule is absent. | Obtain approved pain/function weighting and incomplete-response rules; do not assume simple sum. |
| M12 | S04: Lower Extremity Functional Scale.pdf | 1 | Patient; 20 items scored 0–4 | Maximum 80 follows item ranges and agrees with current aggregate metadata. | Confirm version and missing-answer rule. The tentative spoken 12-question count is not used. |
| M13 | S04: SPADI.pdf | 1 | Patient; 5 pain + 8 disability items, 0–10 | Printed pain /50, disability /80 and combined /130 × 100; extra visual lines are not part of that total. | Approve exact supplied version and missing-answer handling; do not mix extra lines into the score. |
| M14 | S05: Berg-Balance-Scale (1).pdf | 4 | Clinician; 14 tasks, 0–4, total 56 | Task instructions include conditional full sitting credit after standing two minutes. | Approve administration/completion rules and fixtures; no unsourced fall-risk interpretation labels. |
| M15 | S05: 5x sit to stand.pdf | 2; second blank | Clinician timed test | Supplied protocol uses a 16-inch chair and stops standing on the fifth repetition. | Confirm that exact endpoint/protocol, units and unable/not-performed handling; do not replace with a different familiar protocol. |
| M16 | S05: Tinetti.pdf | 2 | Clinician; balance 16 + gait 12 = 28 | Items have differing weights and side-specific observations. | Approve scoring fixtures, administration and missing/unable behavior; no generic equal-weight shortcut. |
| M17 | S05: timed-up-and-go---tug-test.pdf | 1 | Clinician timed test | 3 m; one practice and two counted trials; endpoint when back touches chair. | Specify how two trials combine into a reported result and missing/unable behavior; no assumed average/best trial. |

**Technical Recommendation:** maintain a per-instrument approval record with exact source hash/version, item wording/options, administration mode, assignment rule, answered/N/A/unable semantics, scorer version, approved fixtures and permission to reproduce the source. Only activate instruments whose record is complete. Repository aggregate metadata is useful compatibility evidence, not a substitute for client-approved item content. Avoid placing raw PHI examples or unreviewed external material in runtime catalogs.

### Dependency register

| Dependency | Current state | Owner/input required | Affected work | What can proceed |
| --- | --- | --- | --- | --- |
| D01 — Outcome source approval | PDF delivery satisfied; several content/scoring questions remain | January/clinical owner resolves M01–M17, chooses first cohort and supplies scoring fixtures; confirm reproduction rights for chosen assets | 23–25,36 | Contract/validation framework and source normalization; no unapproved instrument activation |
| D02 — Discharge workflow document | Not among supplied files | January identifies exact document/revision and recommendation choices; clinical owner approves ownership | 08 discharge slice,31,32 full discharge context | Other intake types, progress work and existing-field AI wiring |
| D03 — Settings integration baseline | Open #153 and draft #154; current metadata reviewed, code/tests not reviewed | Engineering resolves stack and supplies integrated SHA plus test/release evidence | 01 coordination,04,13 coordination,33 | Read-only comparison, test preparation, independent current-clinic features |
| D04 — Session/error/environment reproduction | No authenticated runtime exercised | Engineering provides controlled Web/API matching release and synthetic roles; January supplies recurrence steps without patient details | 01–03,29,33; stale-control checks in08/27 | Source diagnosis, reproduction design and fault-injection design |
| D05 — Organization/role/signature policy | Unapproved/incomplete | January/clinical owner plus engineering/security approve organization membership, local administration and PT/PTA handoff matrix | 05,06,18; role validation04 | Existing-clinic features under established policies |
| D06 — Alert lifecycle policy | Feature intent confirmed; mutation/history rules missing | January/clinical owner answers C13 | 19 | UI prototype and data-design options only |
| D07 — Fax sandbox and destination | Existing integration code; credentials/configuration not inspected | Integration owner provides approved sandbox access and synthetic destination through existing secret process | 17 | Mocked provider development; final delivery acceptance waits |
| D08 — AI quality and provider verification | Source paths inspected; provider not invoked | Clinical owner supplies synthetic input/output rubric; engineering supplies authorized enabled environment when testing is approved | 28,29,32;26 context | Contract/mocked tests and manual editing; clinical/provider acceptance later |
| D09 — Subscription/commercial policy | Exploratory proposal, no approved entitlement contract | Business owner selects workflow/provider and entitlement/recovery rules | 06 | Decision brief only; existing copay payment work is not subscription evidence |
| D10 — Authorization placement and zero-visit policy | Conflicting/exploratory | January/benefits operations answers C01/C21/C23 | 15,35;14 historical-edit extension | Approved consolidation and preservation of existing rules |
| D11 — Identity terminology | Unresolved | January/clinical owner approves field purposes and mappings | 34 | Unrelated contact editing |
| D12 — Clinical reference/legacy mapping | Existing catalogs available; active/passive legacy ambiguity remains | Clinical owner validates reference catalogs; engineering inventories legacy typed/JSON records | 21,22 | Structural implementation with explicit unknown legacy values and existing separate grade catalogs |

No external system credentials, patient screenshots or live fax destinations are embedded in this plan. Required test accounts should be synthetic and supplied through the repository’s existing secret/configuration process. File delivery follow-up should request only missing or corrected materials, not ask the client to resend all received PDFs.

## 9. Recommended Development Batches

| Batch | Objective | Included PRs | Prerequisites | Parallel work | Required regression coverage | Validation checkpoint |
| --- | --- | --- | --- | --- | --- | --- |
| B0 — Establish evidence and settle consequential decisions | Get reliable source/deployment and policy baselines before changing behavior. | 01–06,15,18,19,29,33–35 (diagnosis/decisions, not all implementation) | Exact source/release identifiers; synthetic accounts; client decision review; existing Settings stack ownership. | Independent source/asset review23 and confirmed UI work can proceed; no organization/signature/entitlement policy is guessed. | Existing auth, note-save, RBAC/Tenancy and Settings behavior; preserve baseline screenshots/log reason codes without PHI. | Recorded reproduction or explicit Unable to Verify; accepted decision matrix for each gated feature; no stale PR-body claims used as evidence. |
| B1 — Reliable patient and intake foundation | Retain known patient data and make existing forms usable. | 07–12,14;13 under existing maintainer policy;08 ships independent note types in phases | B0 session baseline; existing clinic isolation; stable seed/patch/consent contracts. | 09–11 copy/navigation can run alongside07/12/14; coordinate shared components to avoid conflicting edits. | Create/send/resume intake; contact/policy reload; consent historical packets; patient permissions; responsive focus; stale-write behavior. | Synthetic patient with two policies completes creation→sent intake→review→chart without data loss; untouched/cleared-field cases demonstrated. |
| B2 — Scheduling and signed-note communication | Make operational entry points work without new policy assumptions. | 16,17;04 only after stack integration | Eligible-role matrix, existing visit types, signed-document export and fax mocks/sandbox. | Scheduling and fax can run independently; provider fax propagation13 is shared input. Handoff18 and alerts19 wait for approved policies. | Empty schedule, PT/PTA/direct API eligibility, time zones, historical appointments, signed/unsigned fax, paging, retry/idempotency and tenant isolation. | Admin can create from an empty slot; authorized clinician can compose a synthetic signed-note fax and verify sandbox status. |
| B3 — Daily/evaluation and objective representation | Implement concise note-type content and preserve measurement meaning. | 20–22,26,27;08 shared questionnaire routing | Stable intake mapping; clinical reference review; metric schema/version design before migration. | Daily/goal/layout work can run alongside metric work;22 starts after21’s movement/dimension contract stabilizes. | All note types; side/mode/grade round-trip; typed/JSON/export/sync consistency; old drafts/signed notes; clinician/patient field ownership. | Clinical owner reviews synthetic daily/evaluation notes and bilateral/spine legacy/current metric examples; no automatic degree-to-grade copy. |
| B4 — Outcomes with approved instrument cohorts | Collect and score only approved forms, then project results with provenance. | 23,24,36,25 (contract→administration→projection) | Per-instrument approval from D01; encounter linkage; clinical fixtures. | Patient renderer and clinician renderer can run in parallel after contract23; independent approved instrument-content PRs follow the first cohort. | Item validation, missing/N/A/unable rules, all scoring boundaries, conditional tasks, timed trials, old aggregate compatibility, read-only signed associations and patient-link authorization. | For each activated instrument, approved answers produce independently checked results; unapproved forms remain unavailable. No blanket approval of all PDFs. |
| B5 — Progress/discharge and controlled AI suggestions | Complete progress changes and bounded, reviewed generation. | 28–32;31 only after source delivery;29 fix only after reproduction | Section-purpose contract28; approved synthetic quality rubric; discharge source for31/full32. | Progress30 and goal diagnosis29 can run alongside28; progress next-visit wiring32 can precede discharge content completion. | Activity history/new choices, percent bounds, frequency save, disabled/failed/stale AI, accept/reject/edit, signed-note integrity and old discharge payloads. | Clinical reviewer accepts distinct S/O/A/P outputs and next-visit/discharge suggestions against rubric; manual editing works when AI is unavailable. |
| B6 — Policy-dependent expansion and release acceptance | Implement only decisions that are now approved and validate cross-workflow release. | 05,06,15,18,19,34,35 as approved;04/33 residual fixes; integrated regression of completed items | Signed-off policy/specs, compatible migrations and stack integration; no requirement to approve every exploratory proposal. | Independent approved decisions may form separate short PRs; organization work must precede organization-dependent signup. | RBAC/Tenancy, migrations across providers, auth/session refresh, offline/sync where affected, clinical note lifecycle, external retries and release parity. | Current Web/API release identifiers match; client acceptance and real test evidence recorded; unresolved proposals remain explicitly deferred, not silently shipped. |

### Verification ownership

**Technical Recommendation:** use targeted unit/component/API tests for changed contracts and existing UI QA suites for user workflows. Use the repository’s CoreCi, RBAC, Tenancy, OfflineSync, Compliance, EndToEnd, DatabaseProvider and relevant observability/secret-policy coverage only where the change warrants it. Session changes need multi-tab/expiry tests; schema changes need provider migration and old-record fixtures; integration changes need mocked failure paths plus approved sandbox evidence. UI-only copy/layout changes need focused browser/accessibility evidence rather than tests that merely mirror markup.

The existing responsive, patient-document, audit-remediation and hosted-beta suites are starting points, not proof of coverage for these new requirements. Hosted testing needs a controlled fixture set and matching Web/API releases. Avoid using real patient records. The user runs heavy verification under AGENTS.md unless separately authorized; capture exact command, revision, environment and result before checking any PR-template test box or committing. Keep generated browser reports and secret files untracked. Production releases continue to follow CI.md’s main/short-lived release branch/immutable tag policy; no new deployment branch or workflow is proposed.

## 10. Non-Product and Process Actions

| Action | Source IDs | Action or disposition | Owner | Boundary / completion state |
| --- | --- | --- | --- | --- |
| P01 | R090 | Developer sends January a concise message after each actual app update, stating what changed, where to validate and known limitations. | Admin Black House Developers | Process commitment only. No in-app release notification feature, automation or external message is created by this planning task. |
| P02 | R012,R095 | Acknowledge receipt of the 16 PDFs; request only corrected/approved variants, missing scoring rules and clinical fixtures from M01–M17. | January / clinical owner; developer tracks receipt | File-delivery action is satisfied for supplied files; content approval remains open. |
| P03 | R094 | Obtain the exact discharge workflow document/revision and recommendation choices. | January | Blocks31 and full discharge-content integration, not unrelated work. |
| P04 | R086,R084–R085 | Review signup alternatives and record decisions for organization ownership, local clinician administration and individual access. | January / business owner with engineering | No subscription or organization implementation is approved by the discussion alone. |
| P05 | R091,R006 | If the stuck-note issue recurs, record route type, safe reproduction steps, approximate time and release; use sanitized evidence. | January supplies context; engineering reproduces with synthetic records | Do not paste patient identifiers or tokens into issues or chat. |
| P06 | R003,R004,R037,R060,R062 | Compare local and deployed Web/API release identifiers, then reconcile existing Settings and clinical-workspace PRs before duplicate work. | Engineering | Read-only investigation now; current runtime remains unverified. |
| P07 | R017,R030,R041–R043,R089,R095 | Run a focused decision review using section7 and record owner/date/decision/source. | Product/clinical owner with engineering | No neutral question in this artifact is a sent message or an assigned deadline. |
| P08 | R087–R088 | Retain third-party over-authorization/signing and copay-task stories as context. | Product owner if later prioritizing | No Mako/Makeo billing connector, balance repair or automated copay task project is approved. |
| P09 | R039,R040,R072,R075 | Keep multi-note fax as a future idea; no schedule fax is needed; withdrawn goal-type concern and already-visible additional information do not create duplicate features. | Product owner | Future proposals need explicit later approval. |
| P10 | R092 | Exclude personal conversation, fatigue/travel stories, hiring anecdotes and broad AI ethics discussion from product scope. | No software owner | Out of scope; not repeated as patient or personnel details. |
| P11 | All implementation briefs | When actual code changes are authorized, use the repo PR template, update CHANGELOG, request user-run heavy verification and honor commit restrictions. | Implementing developer | This planning-only artifact changes no repository files, so no changelog entry is needed. |

## 11. Coverage Check

- **Aligned decisions:** all **45/45** paragraph entries P0018–P0062 are mapped; stronger later transcript evidence is recorded where it changes their meaning.
- **Assigned next steps:** all **58/58** entries P0069–P0126 are mapped, including document delivery and the update-message process commitment.
- **Complete source review:** full meeting text/transcript and all 13 embedded screenshots were reviewed; all 16 PDFs were inspected, including the extra Roland–Morris form and non-measure body diagram.
- **Primary traceability:** **95 unique ledger entries**, each appearing once in section6, map to **36 briefs** or an explicit process/context/future/out-of-scope disposition. Repeated source statements are aliases, not duplicate PRs.
- **Evidence separation:** 10 reported-defect entries are distinguished from confirmed requirements, enhancements, proposals and technical work. Source inspection is not labeled runtime verification. No Already Addressed, Verified claim is made.
- **Decision protection:** seven brief-level clarification gates and five dependency gates prevent unresolved policies/materials from becoming approved implementations. Confirmed subitems inside a verification/decision brief do not bypass its gate.
- **Off-topic and third-party material:** excluded from product commitments. The explicitly PTDoc-linked zero-visit idea is preserved as a decision brief; surrounding third-party complaints remain context.
- **Uninterpretable/incomplete material:** authorization placement, sex/gender semantics, progress question8, unidentified current/prior data, fragmented visual comments, commercial/ownership/handoff/alert policies, instrument anomalies and missing discharge content remain explicit questions. No field, scorer, migration or root cause is invented to fill them.
- **Duplication control:** prior 21-item roadmap is crosswalked; open #153/#154 are dependencies, not newly recreated PRs. No actual GitHub work item was opened.
- **Artifact checks:** all 36 briefs include every requested section, feature-specific Given/When/Then criteria, dependencies, risks and unchecked Definition of Done. Repository links are pinned to the inspected commit and checked against existing local paths. Counts and aligned/next-step coverage were validated programmatically.
- **Execution boundary:** no repository edits, branches, commits, issues, PRs, deployments, messages, builds, test suites, fax sends or AI-provider calls were made. **No CHANGELOG entry is needed because the repository is unchanged.**

The companion requirement ledger records source sections, timestamps/locators, observed speaker labels, classification, product area, decision status, duplicate aliases, clarification links, dependencies and candidate work items for subsequent project import. It contains planning metadata rather than the raw patient-bearing meeting screenshots.
