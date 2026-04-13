# Changelog

All notable changes to PTDoc will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed - UX Overhaul Phase 1 review-feedback cleanup

#### OutcomeMeasurePanel score parsing and clinic reference docs housekeeping
- **`src/PTDoc.UI/Components/Notes/Workspace/OutcomeMeasurePanel.razor`** — Fixed `TryParseScore` to use `NumberStyles.Float | NumberStyles.AllowThousands` with `CultureInfo.InvariantCulture` first, falling back to `CultureInfo.CurrentCulture`, consistent with `NoteWorkspaceApiService` and `ProgressTrackingAggregationService`. Reason: scores serialised with invariant culture (e.g. the API) would fail to parse and fall through to the plain "Recorded" fallback label when the viewer's locale uses a different decimal separator.
- **`docs/clinicrefdata/`** — Renamed five reference files that carried accidental `(1)` duplicate-export suffixes to stable, lowercase-kebab slugs (`what-was-specifically-worked-on.md`, `what-generally-was-worked-on.md`, `app-list-of-body-parts.md`, `app-list-of-medications.md`, `app-pain-quality-descriptors-patient.md`). Reason: the `(1)` suffix is an OS artefact that makes filenames fragile and hard to reference.
- **`docs/clinicrefdata/List of commonly used Special test.md`** — Fixed truncated heading `###  **ervical Spine**` → `###  **Cervical Spine**`. Reason: leading character was missing from the section heading.

### Changed - patient role shell and route RBAC alignment

#### Patient access now respects the existing role matrix across shell navigation and clinician pages
- **`src/PTDoc.UI/Components/Layout/NavMenu.razor`**, **`src/PTDoc.UI/Pages/Appointments.razor`**, **`src/PTDoc.UI/Pages/Patients.razor`**, **`src/PTDoc.UI/Pages/PatientProfile.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Pages/Notes/NotesPage.razor`**, **`src/PTDoc.UI/Pages/ProgressTracking.razor`**, **`src/PTDoc.UI/Pages/Dashboard.razor`** — Aligned the main shell and clinician-facing routes with the repo’s existing named authorization policies so patient users no longer see or deep-link into clinician-only areas like Appointments, Patients, Notes, Progress Tracking, patient profiles, or note workspaces, and the dashboard now hides clinician quick actions that create patients or send intake invites when the signed-in role lacks those permissions. Reason: live QA confirmed that patient accounts were landing in the clinician shell and seeing operational navigation/actions that conflict with the established PTDoc role matrix, so the shell and page-level guards now follow the same policy boundaries already defined in `AuthorizationPolicies`.

### Changed - UX-overhaul auth, signup, approvals, and read-only clarity follow-up

#### Login trust hardening, signup confirmation reliability, approvals search feedback, and viewer-mode tab polish
- **`src/PTDoc.UI/Pages/Login.razor`**, **`src/PTDoc.UI/Pages/LoginBase.razor.cs`**, **`src/PTDoc.Web/Program.cs`**, **`src/PTDoc.Web/wwwroot/js/auth.js`**, **`tests/PTDoc.Tests/Integration/WebLoginEndpointIntegrationTests.cs`** — Removed the web-login fallback that treated the PIN as the username when the username was blank, changed login failure copy to a safe generic `Invalid credentials`, and added an explicit client-side login field reset path so stale usernames do not persist across logout/login cycles or concatenate into false credential failures. Reason: the UX-overhaul audit found that trust in the login flow was being undermined by stale input state and misleading auth errors, and this branch needs that fixed without redesigning RBAC or auth architecture.
- **`src/PTDoc.UI/Pages/Login.razor`**, **`src/PTDoc.UI/Pages/LoginBase.razor.cs`**, **`tests/PTDoc.Tests/UI/Pages/LoginBaseValidationTests.cs`** — Made the existing signup confirmation panel the canonical success outcome for both pending-approval and successful registration responses, added a clear return-to-login next step, preserved the plain date-input DOB flow with explicit typed-entry guidance, and removed the hidden PT/PTA license-length validator that was silently blocking `Owner`, `Front Desk`, `Billing`, and `Patient` signups before the submit handler could run. Reason: account creation can no longer appear silent or leave QA/test onboarding in an ambiguous state after `Create Account`, especially for non-licensed roles that do not expose license fields in the form.
- **`src/PTDoc.UI/Components/Settings/ApprovalsDashboard.razor`**, **`src/PTDoc.UI/Components/Settings/ApprovalsDashboard.razor.css`**, **`tests/PTDoc.Tests/UI/Settings/ApprovalsDashboardTests.cs`** — Added result-summary feedback, an explicit `No results found` empty state, clear-filter/reset actions, and expanded editable role options so the approvals dashboard remains usable now that QA can register `Billing` and `Patient` accounts. Reason: the audit showed that admin approval search felt unreliable and made new test-account verification unnecessarily brittle.
- **`src/PTDoc.UI/Components/Notes/SoapTabNav.razor`**, **`src/PTDoc.UI/Components/Notes/SoapTabNav.razor.css`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`tests/PTDoc.Tests/UI/Notes/SoapTabNavTests.cs`** — Added explicit read-only styling and `aria-readonly` semantics to the SOAP tab navigation while keeping section-to-section inspection available for viewer roles. Reason: the dashboard-driven read-only note experience is already the baseline standard for this branch, and the last remaining viewer-mode controls needed to stop looking like authoring affordances.

### Changed - PFPT sprint closeout QA unblockers

#### Role provisioning unblock and read-only workspace alignment
- **`src/PTDoc.UI/Pages/Login.razor`**, **`src/PTDoc.UI/Pages/LoginBase.razor.cs`**, **`src/PTDoc.Infrastructure/Services/UserRegistrationService.cs`**, **`tests/PTDoc.Tests/Identity/UserRegistrationServiceTests.cs`** — Replaced the signup DOB field with the same plain date-input pattern already used in intake so browser typing/picker behavior is more reliable in QA, and expanded the self-service registration role list to include the remaining QA coverage personas (`Billing`, `Patient`) alongside PT/PTA/Front Desk/Owner. Reason: unblock the role-based PFPT validation pass without introducing a separate admin user-management project.
- **`src/PTDoc.UI/Components/Notes/SoapStickyFooter.razor`**, **`src/PTDoc.UI/Components/Notes/SoapReviewPage.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/DryNeedling/DryNeedlingNoteView.razor`**, **`tests/PTDoc.Tests/UI/Notes/SoapStickyFooterTests.cs`** — Removed save/submit authoring affordances from pure read-only note views, changed the sticky footer state label to `Read-only` for viewer roles, and preserved submit-only behavior for the special read-only finalization paths that still allow PT completion. Reason: the note workspace must stop presenting admins/owners/billing viewers as if they can author drafts while keeping legitimate co-sign/finalization flows intact.

### Changed - PFPT deferred QA follow-up fixes

#### Subjective medication persistence, assessment motivation round-trip, and objective-signing alignment
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`src/PTDoc.UI/Components/Notes/Workspace/SubjectiveTab.razor`**, **`src/PTDoc.Infrastructure/Compliance/ClinicalRulesEngine.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/Compliance/ClinicalRulesEngineTests.cs`** — Finished the deferred QA follow-ups from the seeded Evaluation/carry-forward rollout by preserving the clinician’s explicit medication yes/no answer even when intake left medications unanswered, round-tripping Assessment `Patient Motivation & Goals` and support-additional-notes fields through the canonical workspace payload instead of dropping them on save/reload, and broadening the objective signing rule so it accepts structured objective findings already supported by the current UI such as gait observations, special tests, palpation, posture, clinical observation notes, recorded outcome scores, and objective metrics while still rejecting notes that only carry suggested outcome-measure recommendations. Reason: resolve the outstanding clinician workflow regressions without introducing parallel note models or weakening the server-authoritative signing rules.
- **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`** — Replaced a collection-expression-plus-`ToHashSet` assignment with an explicit `HashSet<string>` initializer so the deferred QA regression tests compile cleanly under the repo’s current .NET 8 toolchain. Reason: the previous test-only syntax required a target type the compiler could not infer and blocked the verification run before runtime behavior could be exercised.

### Changed - PFPT deferred QA follow-ups and billing contract wiring

#### Deferred QA follow-ups logged for the carry-forward/evaluation step
- **Tracked for the next PFPT follow-up slice** — Logged the outstanding QA items discovered after the seeded Evaluation workflow review so phase work can continue without losing the issues: 1) subjective medication selection becomes non-interactive when intake left medication usage unanswered, 2) Assessment `Patient Motivation & Goals` selections disappear instead of persisting, and 3) signing is blocked by the objective-measure rule even when the current UI does not yet expose the full required objective-measure authoring path. Reason: keep the running implementation TODO list explicit while unblocking the planned next phase slices.

#### Save-time override workflow now uses the existing workspace shell
- **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor.css`**, **`src/PTDoc.Api/Notes/NoteWorkspaceV2Endpoints.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Pages/NoteWorkspacePageTests.cs`** — Wired save-time override submissions and server-returned override requirements through the existing workspace service/page path, added an inline PT-only override justification panel inside the note workspace, and kept invalid override attempts in a structured 422 response so the workspace can stay on the same recoverable save path without inventing a parallel compliance flow. Reason: complete the remaining planned Phase 4 compliance-polish slice while preserving server-authoritative rule enforcement and the existing note workspace architecture.

#### Billing modifier metadata preserved through the workspace contract path
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Application/Compliance/IRulesEngine.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/WorkspaceReferenceCatalogService.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Components/Notes/Models/PlanVm.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`** — Extended the existing planned CPT and compliance DTO path to preserve selected modifiers plus source-backed modifier options/suggestions end to end, and seeded CPT lookup metadata from the exact document **`Commonly used CPT codes and modifiers.md`** without changing the current Plan UI yet. Reason: Phase 4 needs modifier-capable billing contracts before the searchable modifier picker and inline compliance UI can be added safely.
- **`src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor`**, **`tests/PTDoc.Tests/UI/Notes/PlanTabTests.cs`** — Enabled the existing Plan-tab CPT search/modifier UI to auto-apply source-backed suggested modifiers when a CPT is added, and added component coverage for CPT lookup suggestions, modifier-chip rendering, and billing advisories. Reason: clinicians now need the billing workflow to reduce manual modifier entry immediately once the contract metadata is available, while keeping the UI in the existing Plan shell.

#### Billing UI uses the existing CPT modifier metadata path
- **`src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor.css`**, **`tests/PTDoc.Tests/UI/Notes/PlanTabTests.cs`** — Reused the existing CPT lookup and modifier metadata already flowing through the workspace to add a searchable CPT picker, timed-minute entry, modifier chips, and inline billing advisories to the Plan tab without introducing a separate billing route or redundant component stack. Reason: complete the next clinician-facing Phase 4 slice by surfacing the existing structured billing data in the established note workspace shell.

### Changed - PFPT carry-forward UI seed context visibility

#### Durable seed provenance and prefilled context summary
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor.css`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Pages/NoteWorkspacePageTests.cs`** — Added lightweight seed provenance to the canonical v2 workspace payload for intake-prefill and signed-note carry-forward flows, and surfaced that persisted context in the note workspace as token-based source/state badges plus structured Subjective/Objective chips for eval, daily, progress, and discharge notes. Reason: clinicians need visible, durable carry-forward context with editable vs locked state cues instead of relying on one-time startup notices.

#### QA follow-up for seeded context visibility and ICD sign-off
- **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor.css`**, **`src/PTDoc.Infrastructure/Compliance/SignatureService.cs`**, **`tests/PTDoc.Tests/Compliance/SignatureServiceTests.cs`**, **`tests/PTDoc.Tests/Security/PfptRoleComplianceTests.cs`**, **`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`** — Made the seeded source/state copy explicitly label `Source:` and `State:` while broadening the page-level CSS selectors so the badges and chips render reliably in the live workspace, and tightened signature validation so notes can only be signed when the note payload itself contains ICD-10 diagnosis codes. Reason: QA found that editable state was easy to miss in the seeded context summary and clarified that reimbursement-critical ICD selection must be present on the note itself, not inferred from a separate patient chart diagnosis list.

### Changed - Agent workflow docs refresh

#### AGENTS.md CI repro commands and env vars
- **`AGENTS.md`** — Added the CI database smoke-test env vars (`DB_PROVIDER`, `CI_DB_MIGRATIONS_ALREADY_APPLIED`), the local secret-policy scan command, the targeted SecretPolicy/category test commands, and the pinned `dotnet tool restore` step used before SQL Server/Postgres EF CLI repro. Reason: keep agent guidance aligned with the current repo workflows without expanding the default heavy verification path.

### Changed - PFPT structured workflow plumbing and intake structured UX

#### Workspace v2 payload preservation and lookup wiring
- **`src/PTDoc.UI/Components/Notes/Models/NoteWorkspacePayload.cs`**, **`src/PTDoc.UI/Components/Notes/Models/SoapNoteVm.cs`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`** — Preserved the canonical note workspace v2 payload through the UI edit cycle, stopped rebuilding save payloads from lossy legacy strings, narrowed the legacy compatibility path to historical dry-needling notes, and exposed the existing body-region catalog and CPT lookup endpoints through the UI service surface. Reason: eval, progress, and discharge editing now need to retain structured workspace data end to end before the structured tab UX can safely be layered on top.

#### Intake structured-data round-trip alignment
- **`src/PTDoc.Application/Services/IntakeResponseDraft.cs`**, **`src/PTDoc.UI/Components/Intake/Models/IntakeWizardState.cs`**, **`src/PTDoc.UI/Services/IntakeApiService.cs`**, **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`**, **`src/PTDoc.Infrastructure/Services/IntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/InMemoryIntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/MockIntakeService.cs`**, **`tests/PTDoc.Tests/Intake/IntakeApiServiceTests.cs`** — Wired intake `StructuredData` through draft creation, update, ensure, state hydration, and infrastructure clone/copy paths, while keeping existing drafts tolerant of missing structured fields. Reason: intake structured selections must survive API round-trips before clinician-facing structured pickers are introduced.

#### Intake document-backed structured selections
- **`src/PTDoc.UI/Components/Intake/Models/IntakeSupplementalOptionCatalog.cs`**, **`src/PTDoc.UI/Components/Intake/Cards/MedicalHistoryCard.razor`**, **`src/PTDoc.UI/Components/Intake/Cards/MedicalHistoryCard.razor.css`**, **`src/PTDoc.UI/Components/Intake/Steps/DemographicsStep.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainAssessmentStep2.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainDetailsStep.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainDetailsStep.razor.css`**, **`src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor`**, **`src/PTDoc.Web/Program.cs`**, **`src/PTDoc.Maui/MauiProgram.cs`**, **`tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs`** — Replaced intake placeholder and free-text-adjacent flows with document-backed medication search, comorbidity/assistive-device/living-situation/house-layout selections, structured body-part and pain-descriptor capture, and auto-recommended outcome measures surfaced through the existing intake wizard and review step. Reason: reduce typing, improve consistency, and move intake collection onto structured values sourced from the repo clinical documents.

#### Intake workflow merge, live patient search, and pain severity capture
- **`src/PTDoc.Application/Configurations/Header/HeaderConfigurationService.cs`**, **`src/PTDoc.Application/Services/IntakeResponseDraft.cs`**, **`src/PTDoc.Infrastructure/Services/IntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/InMemoryIntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/MockIntakeService.cs`**, **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`**, **`src/PTDoc.UI/Components/Intake/ClinicianPatientSelector.razor`**, **`src/PTDoc.UI/Components/Intake/Models/IntakeWizardState.cs`**, **`src/PTDoc.UI/Components/Intake/Steps/DemographicsStep.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainAssessmentStep2.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainDetailsStep.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/PainDetailsStep.razor.css`**, **`src/PTDoc.UI/Components/Intake/Steps/PainProgressBar.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor`**, **`tests/PTDoc.Tests/Application/HeaderConfigurationServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Intake/ClinicianPatientSelectorTests.cs`**, **`tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs`** — Moved structured medical history capture into the `Medical History / Pain Assessment` intake step without changing the persisted step order, switched the clinician intake selector to the live patient search API used by the Patients page, added a typed `PainSeverityScore` slider with token-based light/dark styling, and hid intake outcome-measure recommendations from patient and front-desk views while preserving clinician/admin visibility. Reason: align intake workflow with the reviewed PFPT flow, fix newly created patient discoverability in intake, and add structured pain severity capture without breaking draft persistence or intake locking.
- **`src/PTDoc.UI/Components/Intake/Steps/PainDetailsStep.razor.css`** — Aligned the pain severity icon, selected tick, and numeric value color to the slider’s active token so the severity display now uses one consistent accent color. Reason: follow-up QA requested the severity readout and icon match the slider color instead of using a separate emphasis token.

#### Evaluation note intake bootstrap and outcome-measure suggestions
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Application/Notes/Workspace/WorkspaceServiceContracts.cs`**, **`src/PTDoc.Api/Notes/NoteWorkspaceV2Endpoints.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Pages/NoteWorkspacePageTests.cs`** — Added a dedicated Evaluation seed read path that resolves the latest applicable intake, maps compatible intake data into a one-time prefill payload for new Evaluation notes, auto-starts the new-note workspace in Evaluation mode when that intake seed is still applicable, and suppresses reseeding once a later Evaluation note already exists. Reason: new Evaluation notes now need visible structured intake context without flattening the workspace model, while ongoing note creation should stay on the normal non-Evaluation flow after the intake has already been consumed.
- **`src/PTDoc.UI/Components/Notes/Models/ObjectiveVm.cs`**, **`src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/OutcomeMeasurePanel.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/OutcomeMeasurePanel.razor.css`**, **`tests/PTDoc.Tests/UI/Notes/OutcomeMeasurePanelTests.cs`** — Added a distinct suggested outcome-measures lane in the Evaluation objective workflow so intake recommendations appear as clinician-facing suggestions and only become persisted scored results after a score is recorded. Reason: intake recommendations must carry forward into Evaluation without polluting outcome-result history or trend tracking.

#### Signed-note carry-forward seeding for new Progress, Daily, and Discharge notes
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Application/Notes/Workspace/WorkspaceServiceContracts.cs`**, **`src/PTDoc.Api/Notes/NoteWorkspaceV2Endpoints.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Pages/NoteWorkspacePageTests.cs`** — Added a typed workspace carry-forward seed path for new Progress, Daily Treatment, and Discharge notes that reuses the existing signed-note eligibility rules, auto-prefills new note drafts from the latest eligible signed note, and preserves structured context while clearing prior-visit objective results, scored outcomes, and CPT selections. Reason: note-based carry-forward now needs to flow through the canonical workspace payload instead of raw note JSON while keeping signed-note immutability and visit-specific data boundaries intact.

### Fixed - PR 93 third-round review comments

#### NotificationPreferencesEditor saving state applied before async gap
- **`src/PTDoc.UI/Components/Settings/NotificationPreferencesEditor.razor`** — Added `await InvokeAsync(StateHasChanged)` immediately after setting `isSaving = true` in `PersistPreferencesAsync`. Previously the component would not re-render until the entire async handler returned, leaving checkboxes enabled and the "Saving…" indicator invisible during the network request. Reason: Blazor does not automatically yield after synchronous state mutations inside async handlers; an explicit render is required to disable inputs and show the in-flight indicator before awaiting I/O.

#### ExportCenter empty AppointmentType guard
- **`src/PTDoc.UI/Pages/ExportCenter.razor`** — Activity item titles now fall back to "Appointment" when `appointment.AppointmentType` is null or whitespace, preventing display of " for PatientName" titles when the type field is absent. Reason: the DTO defaults `AppointmentType` to `string.Empty` so the field can legitimately be empty for records that predate type tagging.

#### Flaky date matchers in appointment usage tests
- **`tests/PTDoc.Tests/UI/Pages/PageScopedAppointmentUsageTests.cs`** — Captured `var today = DateTime.UtcNow.Date` once at the top of each test that uses date-range Moq matchers, replacing the previous inline `DateTime.UtcNow.Date` calls inside the `It.Is<DateTime>(...)` predicates. Reason: evaluating `DateTime.UtcNow.Date` inside the matcher closure at invocation time means a test that runs across midnight UTC can see a different date than the one used at setup, causing a spurious match failure.

### Fixed - PR 93 second-round review comments

#### PDF hierarchy endpoint signed-only gate
- **`src/PTDoc.Api/Pdf/PdfEndpoints.cs`** — `GET /api/v1/notes/{noteId}/export/hierarchy` now returns 422 with the same error message as `POST /export/pdf` when the note is not finalized (signed). Previously the hierarchy endpoint would return data for draft/pending-co-sign notes, bypassing export eligibility. Reason: the hierarchy preview is part of the export surface and must enforce the same signed-only rule to avoid exposing incomplete or uncertified clinical content.

#### BatchReadNotes O(n²) ordering fix
- **`src/PTDoc.Api/Notes/NoteEndpoints.cs`** — `BatchReadNotes` now builds a `{id → index}` dictionary once and uses a O(1) dictionary lookup in `OrderBy` instead of calling `List.IndexOf` (O(n)) per element. Reason: the previous implementation was O(n²) even with the 100-item cap; dictionary-based ordering is both faster and clearer.

### Fixed - PR 93 follow-up review comments

#### Scoped appointment reads for new UI integrations
- **`src/PTDoc.Application/Services/IAppointmentService.cs`**, **`src/PTDoc.Api/Appointments/AppointmentEndpoints.cs`**, **`src/PTDoc.UI/Services/AppointmentApiService.cs`**, **`src/PTDoc.UI/Pages/PatientProfile.razor`**, **`src/PTDoc.UI/Pages/ExportCenter.razor`** — Added patient-scoped appointment and clinician-directory read paths, moved Patient Profile timeline loading off the broad appointments overview payload, and changed Export Center provider loading to use clinicians plus a bounded recent activity window for appointment rows. Reason: address review feedback about over-fetching PHI and loading much larger appointment payloads than these UI surfaces actually need.

#### Review comment cleanup in nav and settings
- **`src/PTDoc.UI/Components/Layout/NavBarBrand.razor`**, **`src/PTDoc.UI/Pages/Settings.razor`** — Added the missing claims namespace import for auth-backed nav-bar user context and restored the settings card wrapper around the notifications section so the intended styling applies. Reason: resolve the remaining compile/styling issues called out in the PR review thread.

#### Regression coverage for scoped appointment usage
- **`tests/PTDoc.Tests/Appointments/AppointmentApiServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Pages/PageScopedAppointmentUsageTests.cs`** — Added client and page-level coverage for the new appointment endpoints, verified Patient Profile uses patient-scoped appointments, verified Export Center loads providers from the clinician directory, and asserted the settings notifications section renders with the expected card wrapper. Reason: lock the review-driven fixes in place with targeted tests.

### Fixed - Export correctness and progress-tracking consistency follow-up

#### Signed-only export targeting and preview resolution
- **`src/PTDoc.Application/DTOs/NoteDtos.cs`**, **`src/PTDoc.Application/Services/INoteService.cs`**, **`src/PTDoc.Api/Notes/NoteEndpoints.cs`**, **`src/PTDoc.Api/Pdf/PdfEndpoints.cs`**, **`src/PTDoc.UI/Services/NoteListApiService.cs`**, **`src/PTDoc.UI/Pages/ExportCenter.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ExportCenterModels.cs`**, **`src/PTDoc.UI/Components/ExportCenter/FiltersPanel.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ExportPreviewPanel.razor`**, **`src/PTDoc.UI/wwwroot/js/export-preview.js`** — Added bounded batch note reads and a server-resolved export preview target API, tightened list/export signed semantics to `NoteStatus == Signed`, blocked pending co-sign notes from PDF export even if a signature artifact exists, removed unsupported SOAP/PDF preview filters, populated recent activity timestamps from real backend timestamps, and switched download handling to a stream-backed Blob/ObjectURL flow with inline preview/download error states. Reason: preview selection and export eligibility must follow authoritative backend state instead of local cache heuristics or signature-field shortcuts.

#### PDF hierarchy sanitization and option semantics
- **`src/PTDoc.Infrastructure/Pdf/ClinicalDocumentHierarchyBuilder.cs`**, **`src/PTDoc.Infrastructure/Pdf/QuestPdfRenderer.cs`**, **`src/PTDoc.UI/Components/ExportCenter/ExportDocumentHierarchyNode.razor`** — Stripped clinician-facing TODO/render-hint scaffolding from hierarchy preview/PDF output and restored `IncludeSignatureBlock` and `IncludeMedicareCompliance` behavior by gating signature and charges/compliance sections in the hierarchy builder. Reason: exported documents must no longer expose internal mapping scaffolding, and existing PDF option flags must still control the generated content.

#### Progress tracking batching and recency normalization
- **`src/PTDoc.UI/Services/ProgressTrackingAggregationService.cs`**, **`src/PTDoc.UI/Components/Settings/NotificationPreferencesEditor.razor`** — Reworked progress tracking to batch-load latest note details and trend notes through the new batch note-read API, normalized patient recency to the newer of latest note activity or latest appointment activity for ordering/alerts/last-assessment display, and restored the last successful notification preference snapshot when an immediate-save attempt fails. Reason: eliminate N+1 note reads, stop stale note dates from overriding newer appointments, and prevent settings toggles from drifting into unsaved UI state after backend failures.

#### Regression coverage for export, progress, and settings
- **`tests/PTDoc.Tests/PTDoc.Tests.csproj`**, **`tests/PTDoc.Tests/Notes/NoteListApiServiceTests.cs`**, **`tests/PTDoc.Tests/UI/ExportCenter/ExportCenterComponentsTests.cs`**, **`tests/PTDoc.Tests/UI/ProgressTracking/ProgressTrackingAggregationServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Settings/NotificationPreferencesEditorTests.cs`**, **`tests/PTDoc.Tests/Integration/PdfIntegrationTests.cs`**, **`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`** — Added client, component, service, PDF-text, and end-to-end tests covering preview-target API calls, unsupported export-center filters, inline preview/download failures, batch note reads, appointment-over-note recency, preference rollback on save failure, missing PDF scaffolding text, disabled signature/compliance sections, and pending co-sign export rejection. Reason: the fixes above need direct regression coverage in the layers where the breakages were introduced.

### Fixed - Notes signing UX and pending co-sign state alignment

#### Pending co-sign status preservation from list to workspace
- **`src/PTDoc.Api/Notes/NoteEndpoints.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Preserved the backend `NoteStatus` in the notes list projection and in the legacy note workspace load path instead of collapsing unsigned notes to a generic draft state. Reason: pending co-sign notes were being loaded into the UI as drafts, which pushed PT finalization through the wrong save/submit path and could block signing.

#### Notes list clarity for PT finalization
- **`src/PTDoc.UI/Pages/Notes/NotesPage.razor`**, **`src/PTDoc.UI/Components/Notes/Models/NoteListItemVm.cs`**, **`src/PTDoc.UI/Components/Notes/NoteCard.razor`**, **`src/PTDoc.UI/Components/Notes/NotesNeedsAttentionBanner.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`** — Updated the Notes page to display `Pending Co-Sign` explicitly, route the attention-banner action for pending notes straight into the review section, and stop masking pending co-sign notes behind the same generic “Needs Attention” badge used for other issues. Reason: clinicians need to see which notes are awaiting PT finalization and reach the signing surface directly from the notes workflow.

### Changed - PDF export document hierarchy preview and QuestPDF alignment

#### Hierarchy contract and preview endpoint
- **`src/PTDoc.Application/Pdf/DocumentHierarchyModels.cs`**, **`src/PTDoc.Application/Pdf/IClinicalDocumentHierarchyBuilder.cs`**, **`src/PTDoc.Application/Pdf/PdfModels.cs`**, **`src/PTDoc.Api/Pdf/PdfEndpoints.cs`**, **`src/PTDoc.Api/Program.cs`** — Added a structured clinical-document hierarchy contract, expanded note export DTOs with the patient/note metadata needed to map PDF sections, registered a hierarchy builder service, and exposed `GET /api/v1/notes/{noteId}/export/hierarchy` so preview and download flows can share the same deterministic document tree. Reason: document preview and final PDF generation need a single source of truth for section order, table structure, and explicit TODO nodes when required mappings are missing.

#### Hierarchy-driven PDF composition
- **`src/PTDoc.Infrastructure/Pdf/ClinicalDocumentHierarchyBuilder.cs`**, **`src/PTDoc.Infrastructure/Pdf/QuestPdfRenderer.cs`** — Replaced the string-section PDF composition path with a hierarchy-driven QuestPDF renderer that builds Initial Evaluation, Progress Note, Daily Note, and Discharge Summary documents from patient data, current note payloads, and allowed note-history aggregation, while preserving table column groups and rendering missing-data TODO blocks where the source model is incomplete. Reason: the PDF output must mirror the preview-ready hierarchy spec from the sample documents instead of flattening content into generic SOAP sections.

### Fixed - PDF export hierarchy build compatibility

#### Export contract alignment
- **`src/PTDoc.Infrastructure/Pdf/ClinicalDocumentHierarchyBuilder.cs`** — Replaced discharge-section references to UI-only plan view-model properties with fields that exist on the current backend note contracts, and stopped assuming billing `CptCodeEntry` items carry a description field. Reason: restore compile compatibility for the new hierarchy builder against the actual export DTO and note content contracts.

#### QuestPDF column span typing
- **`src/PTDoc.Infrastructure/Pdf/QuestPdfRenderer.cs`** — Cast grouped table-header span values to `uint` before passing them to QuestPDF `ColumnSpan`. Reason: the installed QuestPDF API expects unsigned span values, and the previous `int` call site failed compilation.

#### PDF integration test constructor update
- **`tests/PTDoc.Tests/Integration/PdfIntegrationTests.cs`** — Updated the integration test renderer construction to pass `ClinicalDocumentHierarchyBuilder` into `QuestPdfRenderer`, and populated the note-type metadata that the hierarchy-based renderer now depends on. Reason: restore test compile compatibility after moving PDF composition to the new hierarchy service.

### Changed - Next sprint UI integration batch: patient context, progress tracking, and notifications settings

#### Patient context cleanup
- **`src/PTDoc.UI/Components/Layout/NavBarBrand.razor`** — Replaced the hardcoded clinician name and role badge with authentication-state-backed display name and role mapping. Reason: the shared shell should reflect the signed-in user instead of seeded identity text.
- **`src/PTDoc.UI/Pages/PatientProfile.razor`**, **`src/PTDoc.UI/Pages/PatientProfile.razor.css`**, **`src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor`** — Replaced the placeholder patient timeline with note, appointment, and intake-backed activity loading; added load, retry, and partial-error states for the patient profile activity surface. Reason: patient context should come from existing backend data instead of scaffolded timeline entries.

#### Progress tracking integration
- **`src/PTDoc.UI/Pages/ProgressTracking.razor`**, **`src/PTDoc.UI/Pages/ProgressTracking.razor.css`**, **`src/PTDoc.UI/Services/ProgressTrackingAggregationService.cs`**, **`src/PTDoc.UI/Components/ProgressTracking/Models/ProgressTrackingPatientVm.cs`**, **`src/PTDoc.UI/Components/ProgressTracking/Models/ProgressTrackingSnapshot.cs`**, **`src/PTDoc.UI/Components/ProgressTracking/ProgressTrackingFilter.razor`**, **`src/PTDoc.UI/Components/ProgressTracking/ProgressTrackingQuickLinks.razor`**, **`src/PTDoc.UI/Components/ProgressTracking/ClinicalIntelligencePanel.razor`**, **`src/PTDoc.UI/Components/ProgressTracking/ProgressTrackingTrendsPanel.razor`** — Replaced seeded progress-tracking data with UI-side aggregation over existing notes and appointments, added filter-driven reloads plus loading/error/empty states, and surfaced note-detail or trend-load failures visibly instead of silently degrading to partial data. Reason: progress tracking now needs to reflect backend activity truth and fail loudly when required data cannot be loaded.

#### Settings notifications integration
- **`src/PTDoc.UI/Pages/Settings.razor`**, **`src/PTDoc.UI/Pages/Settings.razor.css`**, **`src/PTDoc.UI/Components/Settings/NotificationPreferencesEditor.razor`**, **`src/PTDoc.UI/Components/Settings/NotificationPreferencesEditor.razor.css`**, **`src/PTDoc.UI/Components/Settings/DeferredSettingsSection.razor`**, **`src/PTDoc.UI/Components/Settings/DeferredSettingsSection.razor.css`**, **`src/PTDoc.UI/Components/NotificationsModal.razor`** — Implemented the notifications settings section against the existing notification-center service, reused the same editor from the notifications modal, and converted unsupported settings sections into explicit deferred-state UI instead of scaffold placeholders. Reason: the settings surface should expose real notification preferences without implying unsupported backend contracts for other sections.

### Fixed - Next sprint UI integration batch build compatibility

#### Progress tracking badge enum import
- **`src/PTDoc.UI/Services/ProgressTrackingAggregationService.cs`** — Added the missing `PTDoc.UI.Components` import so the new progress-tracking aggregation service resolves the shared `BadgeVariant` enum used for patient status badges. Reason: restore compile compatibility for the progress-tracking integration changes.

#### Progress tracking nullable score inference
- **`src/PTDoc.UI/Pages/ProgressTracking.razor`** — Changed the derived `averageScore` local to `int?` so the no-score path can return `null` without ambiguous conditional-expression typing. Reason: restore compile compatibility for the metric-card summary path when no patients have outcome scores.

### Changed - Next sprint PR4: Export Center contract-limited pass

#### Real export filters and activity sources
- **`src/PTDoc.UI/Pages/ExportCenter.razor`**, **`src/PTDoc.UI/Pages/ExportCenter.razor.css`**, **`src/PTDoc.UI/Components/ExportCenter/ExportCenterModels.cs`**, **`src/PTDoc.UI/Components/ExportCenter/FiltersPanel.razor`**, **`src/PTDoc.UI/Components/ExportCenter/PatientsDropdown.razor`**, **`src/PTDoc.UI/Components/ExportCenter/PatientsDropdown.razor.css`**, **`src/PTDoc.UI/Components/ExportCenter/ProvidersDropdown.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ProvidersDropdown.razor.css`**, **`src/PTDoc.UI/Components/ExportCenter/RecentActivityPanel.razor`**, **`src/PTDoc.UI/Components/ExportCenter/RecentActivityPanel.razor.css`** — Replaced sample patient/provider dropdown data with real patient and clinician options from existing services, switched export filter selections to real IDs, added page-level loading/error/empty states with retry, and changed the recent-activity card to render real note and appointment activity instead of fake export-history rows. Reason: remove remaining mock data from the Export Center without inventing new backend contracts.

#### SOAP note PDF preview wiring
- **`src/PTDoc.UI/Pages/ExportCenter.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ExportCenterModels.cs`**, **`src/PTDoc.UI/Components/ExportCenter/ExportPreviewPanel.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ExportPreviewPanel.razor.css`**, **`src/PTDoc.UI/Components/ExportCenter/ExportDocumentHierarchyNode.razor`**, **`src/PTDoc.UI/Components/ExportCenter/ExportDocumentHierarchyNode.razor.css`**, **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`src/PTDoc.UI/wwwroot/js/export-preview.js`** — Replaced the disabled Export Center preview card with a live SOAP-note PDF preview flow backed by `GET /api/v1/notes/{id}/export/hierarchy`, re-enabled real PDF download through the existing note export API, and rendered the returned document tree recursively inside the panel. The page now selects a real note preview target from the loaded note list and keeps unsupported cases explicit, including non-SOAP tabs, non-PDF formats, provider-filtered previews, and note-type filters that the current note list projection cannot distinguish. Reason: Export Center should now preview and download real note exports where the backend contract exists, without pretending unsupported filter combinations are fully mapped.

### Fixed - PR review: missing System import and SQLite connection leak

#### SecretPolicyScanTests compile fix
- **`tests/PTDoc.Tests/Security/SecretPolicyScanTests.cs`** — Added `using System;` so `Environment.NewLine` resolves at compile time. Reason: the file referenced `Environment.NewLine` without importing the `System` namespace, causing a build failure.

#### SQLite encrypted connection lifetime fix
- **`src/PTDoc.Api/Program.cs`** — Replaced the manually created/opened `SqliteConnection` passed to `UseSqlite(connection)` with a direct `UseSqlite(connectionString)` call using the `SqliteConnectionStringBuilder`-produced string (which already contains `Password`). EF Core now owns connection creation and disposal for each `DbContext` scope, eliminating the connection/file-handle leak that occurred with the pre-opened connection approach. Reason: connections that EF Core does not own are never disposed, leaking open handles per DI scope.

### Fixed - CI evidence alignment and SQLite startup validation

#### Evidence/docs reconciliation
- **`docs/CI.md`**, **`docs/RELEASE_READINESS_REPORT.md`**, **`docs/ACCEPTANCE_EVIDENCE_MAP.md`**, **`docs/ROLE_CAPABILITY_VERIFICATION_MAP.md`**, **`docs/MOBILE_ARCHITECTURE.md`**, **`docs/PHASE_8_DESIGN_SPECIFICATIONS.md`**, **`docs/PTDocs+ Branch-Specific Database Blueprint and Phased Plan for UI-Completiondeep-research-report.md`** — Reconciled workflow and release-evidence docs with the current CI gates, owner categories, provider-validation mechanics, and SQLCipher connection flow; removed stale references to deleted jobs/tests and obsolete `PRAGMA key` setup guidance. Reason: the prior documentation materially overstated what CI validated and pointed readers at superseded encryption patterns.

#### SQLite startup/test hardening
- **`src/PTDoc.Infrastructure/Data/SqliteProviderBootstrapper.cs`**, **`src/PTDoc.Api/Program.cs`**, **`tests/PTDoc.Tests/Integration/SqliteStartupInitializationTests.cs`**, **`tests/PTDoc.Tests/Integration/DatabaseProviderSmokeTests.cs`**, **`tests/PTDoc.Tests/Security/SecretPolicyScanner.cs`**, **`tests/PTDoc.Tests/Security/RbacRoleMatrixTests.cs`**, **`tests/PTDoc.Tests/SqliteProviderModuleInitializer.cs`** — Removed the test-assembly-wide SQLite provider bootstrap mask, added focused API startup coverage for plain and encrypted SQLite paths, made unsupported `DB_PROVIDER` values fail fast, broadened provider smoke queryability coverage, aligned the C# secret-policy scanner with CI’s non-string handling, expanded RBAC deny-path policy assertions, and changed the bootstrapper to mark itself initialized only after successful SQLCipher setup. Reason: restore meaningful startup validation, reduce false-green provider behavior, and close the review gaps in local-vs-CI enforcement and evidence accuracy.

### Changed - CI workflow rationalization and test ownership cleanup

#### Workflow consolidation and category ownership
- **`.github/workflows/ci-core.yml`**, **`.github/workflows/ci-db.yml`**, **`.github/workflows/ci-release-gate.yml`**, **`.github/workflows/ci-secret-policy.yml`**, **`.github/workflows/_dotnet-category-gate.yml`**, **`.github/scripts/scan_secret_policy.py`** — Removed merged-PR reruns, added workflow concurrency cancellation, standardized .NET setup on `global.json`, introduced NuGet package caching, consolidated repeated gate job logic into a reusable workflow, and moved the secret-policy scan into a shared helper script. `ci-core` now runs only `[Category=CoreCi]`; release/database workflows now target single owner categories without cross-gate overlap. Reason: reduce CI duplication, keep check names stable, and make workflow behavior cheaper and easier to maintain.
- **`tests/PTDoc.Tests/**`** — Introduced the `CoreCi` owner category, added `CiCategoryConventionsTests` to enforce one CI owner category per test file, split secret-policy coverage into `SecretPolicyScanTests`, added `DbKeyProviderTests`, `RbacHttpSmokeTests`, `DatabaseProviderSmokeTests`, and `SqlCipherAccessTests`, consolidated provider/encryption/offline-sync coverage into owner suites, and retired legacy duplicate suites and obsolete workflow-specific test wrappers. Reason: align the test inventory with the workflow rationalization so CI gates execute only their intended suites.
- **`tests/PTDoc.Tests/Compliance/SignatureServiceTests.cs`**, **`tests/PTDoc.Tests/Security/PfptRoleComplianceTests.cs`** — Replaced null diagnosis-code fixtures with whitespace fixtures in signature validation tests so they still exercise the “missing diagnosis” branch without violating the `Patient.DiagnosisCodesJson` required-column constraint enforced by EF Core. Reason: the prior null fixture represented an impossible persisted state and failed before the signature service logic ran.
- **`.github/workflows/ci-release-gate.yml`**, **`.github/workflows/ci-db.yml`**, **`.github/scripts/secret_policy_rules.json`**, **`tests/PTDoc.Tests/Security/SecretPolicyScanner.cs`**, **`tests/PTDoc.Tests/Security/SecretPolicyScanTests.cs`** — Removed the redundant `push` trigger from the heavy release gate workflow, restored SQL Server/PostgreSQL `dotnet-ef` coverage in database CI, and replaced the Python-spawned secret-policy test path with a native scanner that enumerates the exact same tracked files and rules manifest as the workflow helper. Reason: remove leftover duplicate CI work without dropping design-time provider validation or allowing local/CI secret-policy drift.
- **`tests/PTDoc.Tests/Security/CiCategoryConventionsTests.cs`** — Expanded the owner-category regex to recognize qualified xUnit trait attributes such as `[Xunit.Trait(...)]` in addition to bare `[Trait(...)]`, while still matching only real attribute lines. Reason: the stricter cleanup regex missed valid owner tags in sync suites and caused a false convention-test failure.
- **`tests/PTDoc.Tests/Integration/SqlCipherAccessTests.cs`**, **`docs/CI.md`**, **`docs/RELEASE_READINESS_REPORT.md`**, **`docs/PHASE_8_DESIGN_SPECIFICATIONS.md`** — Replaced the prior in-memory SQLite bootstrap test with file-backed SQLCipher access tests that require the correct key to reopen encrypted data, fail explicitly when the environment behaves like plain SQLite, and update the CI/docs references to the renamed suite. Reason: make the encryption coverage truthful instead of implying SQLCipher enforcement without actually proving it.
- **`src/PTDoc.Api/Program.cs`**, **`src/PTDoc.Maui/MauiProgram.cs`**, **`tests/PTDoc.Tests/Integration/SqlCipherAccessTests.cs`** — Changed SQLCipher key application from an ad hoc PRAGMA command to `SqliteConnectionStringBuilder.Password`, which applies encryption at connection-open time and surfaces a clear error if the loaded native SQLite library does not support encryption. Reason: the prior PRAGMA-based path first failed with `near "$key": syntax error`, and even after fixing that syntax it still left room for plaintext database creation in the SQLCipher verification path.
- **`src/PTDoc.Infrastructure/Data/SqliteProviderBootstrapper.cs`**, **`tests/PTDoc.Tests/SqliteProviderModuleInitializer.cs`**, **`src/PTDoc.Api/Program.cs`**, **`src/PTDoc.Maui/MauiProgram.cs`**, **`tests/PTDoc.Tests/Integration/SqlCipherAccessTests.cs`** — Moved SQLCipher bundle initialization to a shared bootstrapper and invoked it before encrypted SQLite connections can freeze the global provider choice; API and MAUI now bootstrap explicitly in their real startup paths instead of relying on a test-assembly initializer. Reason: the SQLCipher tests proved the process was still loading plain `e_sqlite3`, which made `PRAGMA key` a no-op and left encrypted reopen checks ineffective.

### Fixed - Admin approval service robustness and ICD-10 search debouncing

#### AdminApprovalApiService non-JSON error handling
- **`src/PTDoc.UI/Services/AdminApprovalApiService.cs`** — `GetPendingDetailAsync` now uses `ApiErrorReader` instead of `EnsureSuccessStatusCode()` so non-success responses produce an actionable `HttpRequestException` the modal can display. `UpdateAsync` and `SubmitActionAsync` now read the failure response body as a string once, attempt JSON deserialization in a try/catch, and fall back to `ApiErrorReader.ReadMessage` — preventing `JsonException` from escaping when the API returns HTML, plain text, or gateway error pages. Reason: proxy or server errors returning non-JSON bodies would throw an unhandled exception and hide the real error from the user.

#### ApprovalsDashboard filter state validation
- **`src/PTDoc.UI/Components/Settings/ApprovalsDashboard.razor`** — After loading pending registrations, `roleFilter` and `clinicFilter` are re-validated against `AvailableRoleFilters`/`AvailableClinicFilters`; values not present in the populated dropdown are reset to their defaults (`DefaultRoleFilter`/`DefaultClinicFilter`). Reason: an invalid `role=` or `clinic=` querystring value would be silently accepted and sent to the API as a filter, causing the selected dropdown option to differ from what was actually available.

#### ICD-10 search debounce and cancellation
- **`src/PTDoc.UI/Components/Notes/Workspace/DailyTreatment/AssessmentSection.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/AssessmentWorkspaceSection.razor`** — `HandleIcdQueryChangedAsync` and `HandleIcdSearchChangedAsync` now cancel any previous in-flight search and wait 300 ms before issuing a new request. Both components implement `IDisposable` to cancel and dispose the `CancellationTokenSource` when torn down. Reason: every keystroke was dispatching an independent API call, which could cause race conditions (stale results overwriting newer ones) and unnecessary backend load.

### Fixed - Admin approval registration data integrity

#### Pending registration detail, editing, and approval validation
- **`src/PTDoc.Infrastructure/Services/UserRegistrationService.cs`**, **`src/PTDoc.Application/Identity/RegistrationModels.cs`**, **`src/PTDoc.Application/Identity/IUserRegistrationService.cs`**, **`src/PTDoc.Api/Identity/AdminRegistrationEndpoints.cs`**, **`src/PTDoc.UI/Services/AdminApprovalApiService.cs`** — Added pending-registration detail and update flows, moved completeness evaluation to backend truth, persisted signup `DateOfBirth` onto pending `User` records, enforced server-side approval validation for required profile and PT/PTA license fields, and recorded admin edit audits using changed field names only. Reason: the approvals flow was blocking valid approvals because the dashboard relied on placeholder credential data and the server was not validating or exposing the full pending registration record.
- **`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/20260405130000_AddUserDateOfBirth.cs`**, **`src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/20260405130000_AddUserDateOfBirth.cs`**, **`src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/20260405130000_AddUserDateOfBirth.cs`**, **`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/ApplicationDbContextModelSnapshot.cs`**, **`src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/ApplicationDbContextModelSnapshot.cs`**, **`src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/ApplicationDbContextModelSnapshot.cs`** — Added the missing provider migrations and snapshot updates for `Users.DateOfBirth`. Reason: the runtime model already included the field, but fresh test databases were still creating `Users` without the column, which caused broad SQLite-backed test failures on user inserts.
- **`src/PTDoc.UI/Components/Settings/ApprovalsDashboard.razor`** — Replaced placeholder approval-row mapping with real credential completeness data, loaded detail from the backend when the review modal opens, added admin edit/save controls for name, DOB, email, role, license number, and license state, kept clinic read-only, and disabled approve while edits are in progress or required data is still missing. Reason: admins need to review and correct pending signup data directly in the approvals modal before approving new users.
- **`src/PTDoc.UI/Components/Settings/ApprovalsDashboard.razor`**, **`src/PTDoc.Application/Identity/RegistrationModels.cs`** — Added the generated username to pending-registration detail and surfaced it as a read-only field in the admin review modal. Reason: approved users need an inspectable login identifier in review flows even though usernames are still generated from the email local-part.
- **`src/PTDoc.Api/Identity/RegistrationEndpoints.cs`**, **`src/PTDoc.UI/Pages/LoginBase.razor.cs`**, **`src/PTDoc.Web/Auth/SignupApiClient.cs`**, **`src/PTDoc.Web/Auth/WebUserService.cs`**, **`src/PTDoc.Application/Auth/IUserService.cs`**, **`src/PTDoc.Maui/Auth/MauiUserService.cs`** — Removed the stale separate registration `LicenseType` contract path and aligned signup/validation handling around `RoleKey` as the source of truth for PT/PTA license requirements. Reason: the separate license-type input was redundant and had drifted from what was actually persisted for pending users.
- **`src/PTDoc.Infrastructure/Identity/AuthService.cs`**, **`src/PTDoc.Api/Identity/AuthEndpoints.cs`**, **`src/PTDoc.Api/Identity/AuthModels.cs`**, **`src/PTDoc.UI/Pages/Login.razor`**, **`src/PTDoc.UI/Pages/LoginBase.razor.cs`** — Updated PIN login so the existing `Username` request field is treated as “username or email”, kept successful responses returning the canonical username, changed the login UI copy to “Username or Email”, and removed the prior fallback that sent the PIN as the username when the identifier field was blank. Reason: approved self-service users were blocked from signing in because the generated username was hidden and email-based sign-in was not accepted.
- **`tests/PTDoc.Tests/Identity/UserRegistrationServiceTests.cs`**, **`tests/PTDoc.Tests/Integration/AdminRegistrationIntegrationTests.cs`** — Added focused coverage for pending-registration completeness, admin review/update flows, and approval rejection/success behavior around missing and corrected credential data. Reason: the broken approvals path needs direct regression coverage at both service and HTTP API levels.
- **`tests/PTDoc.Tests/Identity/AuthServiceTests.cs`** — Added coverage for email-based PIN login on active and pending accounts. Reason: the login contract now accepts either username or email without changing the wire shape.

### Fixed - Dashboard API runtime regressions

#### Notifications + intake endpoint recovery
- **`src/PTDoc.Infrastructure/Services/UserNotificationService.cs`**, **`tests/PTDoc.Tests/Integration/DashboardApiIntegrationTests.cs`** — Moved notification timestamp ordering to in-memory sorting after query materialization and added an API integration test that verifies `/api/v1/notifications` returns `200 OK` with notifications sorted newest-first under SQLite. Reason: SQLite cannot translate `ORDER BY` over `DateTimeOffset`, which was causing dashboard notification loads to fail with `500 Internal Server Error`.
- **`src/PTDoc.Api/Program.cs`**, **`tests/PTDoc.Tests/Integration/DashboardApiIntegrationTests.cs`** — Registered `IIntakeService` in the API DI container and added an API integration test covering `/api/v1/intake/patients/eligible`. Reason: the endpoint was resolving an unregistered service at runtime, causing the dashboard send-intake patient list to fail with `500 Internal Server Error`.

### Changed - Sprint 4 PR 1: Intake, SOAP, and Dashboard API integration

#### SOAP workspace integration
- **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`**, **`src/PTDoc.UI/Components/Notes/SoapStickyFooter.razor`**, **`src/PTDoc.UI/Components/Notes/SoapReviewPage.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/DryNeedling/DryNeedlingNoteView.razor`** — Replaced the seeded SOAP workspace state with API-backed patient/note data, wired `DraftAutosaveService` into page save flow, added load/error/read-only handling, refreshed note state after save/sign, and enforced role-aware edit/sign/export behavior for PT, PTA, Admin, Owner, and Billing users. Reason: the clinician workspace now needs to follow backend truth for autosave, validation, signing, and RBAC instead of local placeholder data.
- **`src/PTDoc.UI/Services/INoteWorkspaceService.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Extended note submit results to carry structured validation failures and parsed sign response status values, so the workspace can distinguish blocking validation failures from successful `PendingCoSign`/`Signed` transitions without changing backend contracts. Reason: note signing UI must surface backend validation and signature state accurately.
- **`src/PTDoc.UI/Components/Notes/Models/PatientGoalItem.cs`**, **`src/PTDoc.UI/Components/Notes/Workspace/PatientGoalsSidebar.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor.css`** — Removed the seeded sidebar goal cards and switched the objective sidebar to render persisted goal descriptions, timeframe, status, and completion state from the note payload, with new workspace loading/error styling. Reason: the objective sidebar should reflect real patient goals rather than mock progress cards.

#### Dashboard integration
- **`src/PTDoc.UI/Pages/Dashboard.razor`**, **`src/PTDoc.UI/Components/Dashboard/RecentNotesCard.razor`**, **`src/PTDoc.UI/Pages/Dashboard.razor.css`**, **`src/PTDoc.UI/wwwroot/css/components/dashboard-cards.css`** — Added page-level dashboard loading/error/empty states, retry handling, recent-note status enrichment via note detail fetches, and real `Draft`/`Pending Co-Sign`/`Signed` badges in the recent-notes card. Reason: dashboard widgets now need to reflect live API state and recover cleanly from API failures.
- **`src/PTDoc.Web/Program.cs`** — Removed the web host registration of `MockDashboardService`. Reason: dashboard runtime behavior should no longer be wired to mock DI when the UI is aggregating real API data.

#### Intake workflow integration
- **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`**, **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor.css`** — Added explicit loading/saving/submitting flags, disabled-state handling, read-only notices for non-writer roles, and guarded draft creation so read-only roles no longer auto-create intake drafts. Reason: intake pages now need predictable state handling and UI enforcement that matches backend intake RBAC.
- **`src/PTDoc.UI/Services/IntakeApiService.cs`** — Replaced remaining `EnsureSuccessStatusCode()` paths with detailed `HttpRequestException` messages derived from API error payloads. Reason: intake pages need inline backend validation and network error text instead of generic status-code failures.

### Fixed - Sprint 4 PR 1 build compatibility

#### UI service compile fix
- **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Fixed `??` fallback expressions in note submit validation parsing and ICD-10 lookup results by coalescing through `IReadOnlyList<T>` instead of mixing `List<T>` and array operands directly. Reason: restore compile compatibility with the current deserialization types used by the workspace API service.

### Fixed - CI: Whitespace Formatting in SyncEngine

#### Formatting Fix
- **`SyncEngine.cs`** — Fixed over-indented `if (patientId == Guid.Empty)` guard block (22 spaces → 16) and removed trailing spaces on multi-line `??` expressions in the `ClinicalNote` push path. Affects: `src/PTDoc.Infrastructure/Sync/SyncEngine.cs`. Reason: `dotnet format --verify-no-changes` was exiting with code 2 on these lines, blocking Core CI.

### Fixed - CI: Whitespace Formatting in ILocalSyncOrchestrator

#### Formatting Fix
- **`ILocalSyncOrchestrator.cs`** — Fixed extra leading space before `///` on the `PushPendingAsync` doc-comment (5 spaces → 4 spaces). Affects: `src/PTDoc.Application/LocalData/ILocalSyncOrchestrator.cs`. Reason: `dotnet format --verify-no-changes` in Core CI was exiting with code 2 due to this single whitespace violation, blocking the build.

### Fixed - Review Feedback: Sync + Compliance + Notes API Corrections

#### Co-Sign Response Flag
- **`ComplianceEndpoints.cs` / `CoSignNote`** — Fixed `requiresCoSign` in the success response from `true` to `false`. Affects: `src/PTDoc.Api/Compliance/ComplianceEndpoints.cs`. Reason: after a successful PT co-sign the note no longer requires co-signature; returning `true` was misleading clients and could keep notes stuck in a "pending co-sign" UX state.

#### Sync Queue Inspection — Item Identifier
- **`SyncEndpoints.cs` / `GetSyncQueue` + `GetDeadLetters`** — Added `id = item.Id` to the projection returned by both `/api/v1/sync/queue` and `/api/v1/sync/dead-letters`. Affects: `src/PTDoc.Api/Sync/SyncEndpoints.cs`. Reason: without the record identifier operators cannot correlate an API row to a specific persisted `SyncQueueItem` for support or debugging, especially when multiple items share the same `entityId` across operations.

#### Dead-Letters Response — Removed Duplicate Field
- **`SyncEndpoints.cs` / `GetDeadLetters`** — Removed the duplicate `finalFailureReason` field that was set to the same value as `errorMessage`. Affects: `src/PTDoc.Api/Sync/SyncEndpoints.cs`. Reason: identical fields increase payload noise and create ambiguity about which field clients should read.

#### Addendum ParentNoteId — Nullable Type
- **`NoteDtos.cs` / `NoteAddendumResponse`** — Changed `ParentNoteId` from `Guid` to `Guid?`. Affects: `src/PTDoc.Application/DTOs/NoteDtos.cs`. Reason: the previous `Guid` type required a `?? Guid.Empty` fallback that produced a misleading valid-looking identifier for addendums whose parent link is absent.
- **`NoteEndpoints.cs` / `MapLinkedAddendum`** — Removed the `?? Guid.Empty` fallback and assigns `note.ParentNoteId` directly. Affects: `src/PTDoc.Api/Notes/NoteEndpoints.cs`. Reason: `Guid.Empty` silently obscured missing-parent data; nullable propagation makes the absence explicit and easier to diagnose downstream.

### Fixed - Sync Addendum Runtime + Test Regression

#### Addendum Service DI Cycle + Queue Enqueue
- **`AddendumService.cs` / `CreateAddendumAsync`** — Removed the direct `ISyncEngine` dependency from `AddendumService` and switched to direct enqueue writes on `SyncQueueItems` after addendum note persistence. Affects: `src/PTDoc.Infrastructure/Compliance/AddendumService.cs`. Reason: break the runtime circular dependency path (`ISyncEngine -> ISignatureService -> IAddendumService -> ISyncEngine`) that surfaced as broad `500 InternalServerError` failures in integration tests.

#### Local Offline Push Payload Completeness
- **`LocalSyncOrchestrator.cs` / pending clinical-note payload generation** — Restored addendum metadata fields (`CreatedUtc`, `ParentNoteId`, `IsAddendum`) in the serialized clinical-note push payload generated from local pending drafts. Affects: `src/PTDoc.Infrastructure/LocalData/LocalSyncOrchestrator.cs`. Reason: fix missing-key failures in local sync protocol tests expecting addendum metadata propagation.
- **`LocalSyncOrchestratorTests.cs` / `PushPendingAsync_IncludesAddendumMetadata_ForClinicalNotes`** — Relaxed `createdUtc` assertion to compare parsed round-trip `DateTime` values instead of exact string formatting, so semantically equal ISO-8601 timestamps with trimmed trailing fractional zeros are accepted. Affects: `tests/PTDoc.Tests/LocalData/LocalSyncOrchestratorTests.cs`. Reason: prevent brittle failures caused by equivalent serializer formatting differences (`.3274820Z` vs `.327482Z`).

#### Sync Test Harness Compatibility With New Addendum Flow
- **`SyncClientProtocolTests.cs` / `SyncEpsilonTests.cs`** — Updated signature-service test helpers to use a real `AddendumService` instead of a bare `IAddendumService` mock; updated addendum assertions to validate addendum clinical notes (`ClinicalNote.IsAddendum`) instead of the legacy `Addendums` table. Affects: `tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`, `tests/PTDoc.Tests/Sync/SyncEpsilonTests.cs`. Reason: `SignatureService.CreateAddendumAsync` now delegates to `IAddendumService`; default mocks returned non-usable results and caused signed-conflict paths to fail.
- **`SignatureServiceTests.cs` / constructor + addendum queue assertion** — Updated compliance tests to use the 2-argument `AddendumService(ApplicationDbContext, IAuditService)` constructor and replaced `ISyncEngine.EnqueueAsync` mock verification with direct `SyncQueueItems` persistence assertions. Affects: `tests/PTDoc.Tests/Compliance/SignatureServiceTests.cs`. Reason: `AddendumService` now enqueues directly via `ApplicationDbContext` and no longer depends on `ISyncEngine`.

### Fixed - Test Compilation Compatibility

#### SignatureService Constructor Alignment in Sync Tests
- **`SyncEpsilonTests.cs` / `CreateSignatureService`** — Updated test wiring to use the current `SignatureService(ApplicationDbContext, IAuditService, IClinicalRulesEngine, IHashService, IAddendumService)` signature by passing `HashService` and a mocked `IAddendumService` instead of the removed identity accessor argument. Affects: `tests/PTDoc.Tests/Sync/SyncEpsilonTests.cs`. Reason: restore compile compatibility after signature service dependency expansion.
- **`SyncClientProtocolTests.cs` / `CreateSignatureService`** — Updated test helper to construct `SignatureService` with `HashService` and mocked `IAddendumService`, and removed the obsolete identity accessor dependency import. Affects: `tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`. Reason: fix CS7036 constructor-argument failures in sync protocol test builds.

### Added - Sprint 3: Sync Hardening + Observability + Reliability

#### Server Sync Runtime Status + Overlap Prevention (`src/PTDoc.Application/Sync/ISyncEngine.cs`, `src/PTDoc.Application/Sync/ISyncRuntimeStateStore.cs`, `src/PTDoc.Infrastructure/Sync/SyncRuntimeStateStore.cs`, `src/PTDoc.Infrastructure/Sync/SyncEngine.cs`, `src/PTDoc.Api/Program.cs`)
- **`ISyncRuntimeStateStore` / `SyncRuntimeStateStore`** — Added a singleton in-memory runtime tracker for server-side sync execution state, including `IsRunning`, start/end timestamps, last success/failure timestamps, queue counters, and the last sanitized error. Reason: the previous scoped `SyncEngine` timestamp state was not durable across requests, so sync status was not operationally reliable.
- **`SyncEngine.cs` / `PushAsync` / `SyncNowAsync`** — Added shared run-lock behavior so overlapping manual/background sync cycles return `Skipped` instead of double-processing the queue. Reason: prevent concurrent sync runs from racing the same queue and creating unreliable operational state.

#### Queue Hardening: Batching, Retry Visibility, Failure Classification, Dead Letters (`src/PTDoc.Core/Models/SyncQueueItem.cs`, `src/PTDoc.Application/Sync/ISyncEngine.cs`, `src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncQueueItem.cs`** — Added persisted nullable `FailureType` plus `SyncQueueStatus.DeadLetter` and `SyncFailureType` (`NetworkError`, `ValidationError`, `ConflictError`, `ServerError`). Reason: queue failures and terminal items must be queryable and restart-safe without introducing a second ledger table.
- **`SyncEngine.cs` / queue processing path** — Reworked server queue processing around repeated ordered batches of 10 items, 15-second per-item timeouts, normalized `MaxRetries = 5`, structured per-item outcomes, and explicit dead-letter transitions. Reason: improve throughput and resilience while preserving same-entity ordering and avoiding silent retry loops.
- **`SyncEngine.cs` / audit + telemetry metadata** — Added `ITEM_PROCESSED`, `ITEM_FAILED`, and `DEAD_LETTER_CREATED` observability events plus non-PHI metadata such as `OperationType`, `RetryCount`, `BatchSize`, `FailureType`, and duration. Reason: sync activity must be diagnosable in production without adding PHI to logs.

#### Crash Recovery + Background Queue Driving (`src/PTDoc.Infrastructure/BackgroundJobs/SyncRetryBackgroundService.cs`, `src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncRetryBackgroundService.cs`** — Expanded the existing retry hosted service into the operational queue driver: it now recovers stale `Processing` rows, delegates queue draining back to `ISyncEngine.PushAsync()`, logs cycle summaries including dead-letter counts, and respects the shared overlap lock. Reason: background sync processing should use the same hardened server path as manual sync runs instead of maintaining separate retry semantics.
- **`SyncEngine.cs` / `RecoverInterruptedQueueItemsAsync`** — Added startup-cycle recovery for interrupted `Processing` rows, moving them back into visible failed state with sanitized server-error classification. Reason: queue state must survive app/API restarts without data loss or invisible stuck rows.

#### Sync API Status, Queue, Dead-Letter, and Health Endpoints (`src/PTDoc.Api/Sync/SyncEndpoints.cs`, `src/PTDoc.Application/Sync/ISyncEngine.cs`)
- **`SyncEndpoints.cs` / `/api/v1/sync/status`** — Kept `lastSyncAt` for backward compatibility while adding `isRunning`, `pending`, `failed`, `lastSync`, `lastError`, and `deadLetterCount`. Reason: existing web callers keep working while operators gain real-time sync visibility.
- **`SyncEndpoints.cs` / `/api/v1/sync/queue` / `/api/v1/sync/dead-letters` / `/api/v1/sync/health`** — Added new ClinicalStaff-protected sync inspection endpoints returning sanitized queue state, retry visibility, dead-letter visibility, and an operational health summary. Reason: production support needs API-level observability without introducing UI dashboards in this sprint.
- **`ISyncEngine.cs`** — Added sync read models for queue items, dead letters, health, richer queue status, and skip-aware push/full-sync results. Reason: the hardened API surface requires explicit contracts instead of anonymous ad hoc state.

#### Schema + Regression Coverage (`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/20260404120000_AddSyncQueueFailureType.cs`, `src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/20260404120000_AddSyncQueueFailureType.cs`, `src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/20260404120000_AddSyncQueueFailureType.cs`, `tests/PTDoc.Tests/Sync/SyncConflictResolutionTests.cs`, `tests/PTDoc.Tests/BackgroundJobs/BackgroundJobTests.cs`, `tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`, `tests/PTDoc.Tests/Security/AuthorizationCoverageTests.cs`)
- **Provider migrations** — Added the single schema change for Sprint 3: nullable `FailureType` on `SyncQueueItems` across SQLite, Postgres, and SQL Server migration projects. Reason: failure classification must survive restarts and support queue/dead-letter inspection APIs.
- **Tests** — Added coverage for dead-letter promotion on terminal validation failures, shared runtime status across scoped engine instances, background recovery of interrupted items, background skip behavior during overlapping runs, and RBAC coverage for the new sync queue/health endpoints. Reason: the hardened queue state machine and observability surface are release-critical and must remain regression-protected.

### Fixed - Sprint 3: PR Review Feedback (Sync Pipeline Hardening)

#### SyncRetryBackgroundService — Skip Recovery When Run Is Active (`src/PTDoc.Infrastructure/BackgroundJobs/SyncRetryBackgroundService.cs`)
- **`SyncRetryBackgroundService.cs`** — Injected `ISyncRuntimeStateStore` into the constructor; `RecoverInterruptedQueueItemsAsync` is now skipped when a sync run is already active. Reason: items in `Processing` state may be legitimately held by a running cycle, and promoting them to `Failed` mid-run corrupts the pipeline state.

#### SyncEngine — Honour Configured `MinRetryDelay` (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`, `src/PTDoc.Application/BackgroundJobs/IBackgroundJobService.cs`)
- **`SyncEngine.cs`** — The previously hardcoded `RetryDelay = 60s` constant is replaced by an instance field `_retryDelay` sourced from `IOptions<SyncRetryOptions>.MinRetryDelay`. Reason: the configured value was documented but silently ignored by `GetNextBatchAsync`; configuration now controls actual retry-window behaviour.

#### SyncEndpoints — Restrict Inspection Endpoints to `AdminOnly` (`src/PTDoc.Api/Sync/SyncEndpoints.cs`)
- **`SyncEndpoints.cs` / `/api/v1/sync/queue`, `/api/v1/sync/dead-letters`, `/api/v1/sync/health`** — Override authorization from the group `ClinicalStaff` policy to `AdminOnly`. Reason: these endpoints expose raw entity IDs and error details that can leak cross-clinic operational metadata in a multi-tenant deployment; administrator-only access is the appropriate scope.



#### SyncEngine — Delete Conflict Detection (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `DetectConflict`** — Client delete against a missing server record now returns `null` (treated as already-applied/Accepted) instead of `DeletedConflict`, making idempotent deletes safe for retries. `DeletedConflict` is now only raised when the server record exists, is archived/deleted, and the client is attempting an update — not when both sides agree on deletion. Reason: prevent clients from getting stuck in a permanent conflict loop when retrying deletes for already-removed rows.

#### SyncEngine — Duplicate Audit Events (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ResolveUnknownConflictAsync`** — Renamed internal audit event from `CONFLICT_DETECTED` to `CONFLICT_MANUAL_REQUIRED` to eliminate duplicate audit log rows for unknown/manual-required conflicts, since `ResolveConflictAsync` already emits `CONFLICT_DETECTED` unconditionally before dispatching to each resolver. Reason: one conflict event per resolution path — no duplicate rows.

#### SyncEngine — Legacy Conflict Receipt Backward Compatibility (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ParseConflictReceipt`** — Added a `TryParseLegacyConflictReceipt` / `LooksLikeLegacyConflictMessage` fallback so pre-JSON plain-text conflict messages (e.g. "Server version is newer", "immutable", "locked") stored before the JSON envelope format are parsed as conflict receipts rather than treated as non-conflict errors. Reason: prevent behavior change for existing devices upgrading through this release where old receipts were stored as plain text.

#### SyncEngine — `BuildConflictResult` Error Field Contract (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `BuildConflictResult`** — `Error` field is now `null` when `ResolutionType == LocalWins` (result `Status == "Accepted"`), matching the `ClientSyncPushItemResult.Error` contract that reserves this field for `Error`/`Conflict` statuses only. Reason: avoid confusing clients that inspect `Error` for success path filtering.

#### SyncEngine — Replay Result `Error` Field Contract (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `BuildReplayResult`** — `Error` is now unconditionally `null` for `Status == "Accepted"` replay results, even when a conflict receipt is attached. Conflict details are available exclusively via the `Conflict` object. Reason: setting `Error` for an `Accepted` replay violated the push-result contract and could cause clients to treat a successful replay as an error.

#### SyncEngine — Missing `OperationId` Validation (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`, `tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`, `tests/PTDoc.Tests/Sync/SyncEpsilonTests.cs`)
- **`SyncEngine.cs` / `ReceiveClientPushAsync`** — Replaced silent `Guid.NewGuid()` generation for `OperationId == Guid.Empty` with an `ArgumentException`, forcing clients to supply an idempotency key so retries remain safe. Reason: generating a new GUID for missing keys made every retry a distinct operation, breaking the idempotency guarantee for Create operations where `ServerId` may also be empty.
- **`SyncClientProtocolTests.cs` / `SyncEpsilonTests.cs`** — Added `OperationId = Guid.NewGuid()` to all `ClientSyncPushItem` test instances that omitted it; added `ReceiveClientPushAsync_ThrowsArgumentException_WhenOperationIdIsEmpty` test to assert the new validation behavior. Reason: tests must comply with the now-required idempotency key contract.

#### Integration Test Factory — Suppress Background Services (`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`)
- **`PtDocApiFactory.ConfigureTestServices`** — Removed all `IHostedService` registrations from the test DI container to prevent `SyncRetryBackgroundService` and `SessionCleanupBackgroundService` from racing with HTTP-request scopes on the shared in-memory SQLite connection. Reason: both services execute queries that leave SQLite prepared-statement caches active; when a new `SqliteRelationalConnection` was created by an HTTP handler, EF Core's function-registration call (`CreateFunctionCore`) failed with `SQLite Error 5: unable to delete/modify user-function due to active statements`. The `/api/v1/sync/run` endpoint exercises the full push path without requiring the background scheduler.

#### Test Coverage — Delete Semantics (`tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`)
- **`SyncClientProtocolTests.cs`** — Added three delete-semantics tests: delete existing patient (accepted, entity archived), idempotent delete of missing entity (accepted, no conflict), and update of archived/server-deleted patient (DeletedConflict). Reason: ensure deterministic pipeline does not regress delete behavior.



#### Deterministic Sync Conflict Resolution (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ReceiveClientPushAsync`** — Added a single server-side conflict pipeline that loads a normalized server snapshot, detects deterministic conflict types, resolves conflicts before payload mutation, persists replayable conflict receipts, and archives the losing side in `SyncConflictArchives`. Reason: enforce one authoritative sync conflict path without duplicating logic across services.
- **`SyncEngine.cs` / signed-note conflict handling** — Signed clinical note conflicts now fail safe by preserving the original note and creating an addendum through the existing signature/addendum flow, with deterministic JSON payload capture and non-PHI audit events. Reason: signed clinical documentation must remain immutable while still preserving offline edits.
- **`SyncEngine.cs` / draft, intake, and delete conflict handling** — Draft conflicts now resolve via deterministic last-write-wins using `LastModifiedUtc`, locked intake conflicts reject overwrite attempts, and supported patient delete conflicts keep server deletion or mark manual resolution while preserving data. Reason: provide predictable conflict outcomes with no silent data loss.

#### Shared Sync Conflict Contracts (`src/PTDoc.Application/Sync/ClientSyncProtocol.cs`, `src/PTDoc.Application/Sync/ISyncEngine.cs`)
- **`ClientSyncProtocol.cs`** — Added structured sync conflict contracts: `ConflictType`, `ConflictResult`, and nullable `ClientSyncPushItemResult.Conflict` metadata while preserving existing response status semantics. Reason: return explicit API conflict outcomes without breaking existing sync clients.
- **`ISyncEngine.cs`** — Extended `ConflictResolution` with `AddendumCreated`. Reason: represent signed-note conflict fallback without introducing a parallel resolution model.

#### Test Coverage (`tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`, `tests/PTDoc.Tests/Sync/SyncEpsilonTests.cs`)
- **Sync tests** — Added and updated coverage for draft local-wins/server-wins behavior, signed-note addendum creation, duplicate `OperationId` replay, archive preservation, and audit metadata safety. Reason: the new sync conflict rules are acceptance-critical and must stay deterministic.

### Fixed - Sprint 3: PR Review Feedback (Sync Queue + Idempotency)

#### SyncEngine — Processing Placeholder Before Entity Write (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ReceiveClientPushAsync`** — Inserts a `SyncQueueStatus.Processing` receipt placeholder **before** calling `ApplyEntityFromPayloadAsync`, then promotes it to `Completed`/`Failed` in place. Concurrent requests arriving with the same `OperationId` hit a PK unique constraint on the placeholder insert and fall through to the existing replay path instead of both writing the entity. Conflict paths now update the placeholder rather than inserting a new receipt. Reason: close the race window where two concurrent retries could both pass the `existingReceipt` check and duplicate-write a create.

#### LocalSyncOrchestrator — Coalesce Non-Completed Items (`src/PTDoc.Infrastructure/LocalData/LocalSyncOrchestrator.cs`)
- **`LocalSyncOrchestrator.cs` / `EnqueueChangeAsync`** — Coalescing now supersedes **any** non-`Completed` queue row for the same `(EntityType, LocalEntityId)` pair — including `Failed` and `Processing` rows — not only unattempted `Pending` rows. The superseded row is reset to `Pending` with cleared retry state so it gets processed as fresh. Reason: prevent stale Failed payloads from being retried ahead of a newer update for the same entity, preserving last-write-wins semantics.

#### MauiNoteDraftLocalPersistenceService — Best-Effort Enqueue (`src/PTDoc.Maui/Services/MauiNoteDraftLocalPersistenceService.cs`)
- **`MauiNoteDraftLocalPersistenceService.cs` / `SaveDraftAsync`** — Wrapped `EnqueueChangeAsync` in `try/catch` so a queue DB error does not bubble up and undo the already-committed `UpsertAsync` result. The draft remains persisted locally and marked `Pending`; the periodic sync scan (`EnsureQueueItemsForPendingEntitiesAsync`) will recover missing queue items. Reason: preserve offline-first UX — local save must succeed even when the sync queue DB is temporarily unavailable.

#### LocalSyncCoordinator — Deterministic Loop Shutdown (`src/PTDoc.Maui/Services/LocalSyncCoordinator.cs`)
- **`LocalSyncCoordinator.cs` / `RunLoopAsync` / `DisposeAsync`** — Added a `CancellationTokenSource` (`_loopCts`) that is created at `StartAsync` time. `WaitForNextTickAsync` now receives the loop cancellation token, and `DisposeAsync` cancels the source before awaiting `_loopTask`. `OperationCanceledException` surfaced from the cancelled tick is swallowed as normal shutdown. Reason: prevent a faulted or unobserved-exception `_loopTask` when the timer is disposed mid-await; shutdown is now deterministic and the loop exits cleanly on cancellation.

### Added - Sprint 3: Offline Sync Queue Foundation

#### MAUI Local Sync Queue + Background Processing
- **`src/PTDoc.Application/LocalData/Entities/LocalSyncQueueItem.cs`**, **`src/PTDoc.Infrastructure/LocalData/LocalDbContext.cs`**, **`src/PTDoc.Infrastructure/LocalData/LocalDbInitializer.cs`** — Added a durable MAUI-side outbound sync queue persisted in local SQLite with `OperationId`, retry state, timestamps, payload JSON, and status indexes. `LocalDbInitializer` now performs idempotent schema creation for the queue table and indexes on existing device databases. Reason: offline changes must survive app restarts and be retried safely.
- **`src/PTDoc.Infrastructure/LocalData/LocalSyncOrchestrator.cs`**, **`src/PTDoc.Application/LocalData/ILocalSyncOrchestrator.cs`**, **`src/PTDoc.Maui/Services/MauiNoteDraftLocalPersistenceService.cs`** — Reworked the local sync orchestrator around ordered queue execution instead of scanning all pending entities, added `EnqueueChangeAsync`, crash recovery for interrupted `Processing` rows, bounded retry/backoff, and queue-driven note draft enqueueing. Reason: provide a real offline-first sync pipeline foundation without introducing a second sync system.

#### MAUI Sync Runtime Wiring
- **`src/PTDoc.Maui/Services/LocalSyncCoordinator.cs`**, **`src/PTDoc.Maui/Services/MauiConnectivityService.cs`**, **`src/PTDoc.Maui/MauiProgram.cs`**, **`src/PTDoc.Maui/App.xaml.cs`** — Added an app-lifetime MAUI sync coordinator with a 15-second background loop, MAUI-native connectivity detection, singleton sync state sharing, and startup wiring that begins background sync after local DB initialization. Reason: automatic sync should run continuously when the device is online without blocking the UI thread.

#### Server Idempotency + Sync Audit Events
- **`src/PTDoc.Application/Sync/ClientSyncProtocol.cs`**, **`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`**, **`src/PTDoc.Application/Compliance/IAuditService.cs`**, **`src/PTDoc.Infrastructure/Compliance/AuditService.cs`** — Added `OperationId` to the client push protocol, implemented duplicate-operation replay on the server receipt path using the existing `SyncQueueItem` ledger, and introduced `SYNC_START` / `SYNC_SUCCESS` / `SYNC_FAILURE` audit events with non-PHI metadata only. Reason: retries must not create duplicate records and sync activity must be observable without leaking PHI.

#### Test Coverage
- **`tests/PTDoc.Tests/LocalData/LocalSyncOrchestratorTests.cs`**, **`tests/PTDoc.Tests/Sync/SyncClientProtocolTests.cs`**, **`tests/PTDoc.Tests/Sync/SyncEpsilonTests.cs`** — Added coverage for local queue coalescing, retry backoff gating, interrupted-processing recovery, duplicate `OperationId` replay, and sync audit-event PHI safety. Reason: sync queue state transitions and idempotent receipt behavior are acceptance-critical.
### Fixed - CI test failures from Sprint II security hardening

- **`tests/PTDoc.Tests/Compliance/HashServiceTests.cs`** — Replaced `GenerateHash_ContentOrTimestampChange_ReturnsDifferentHash` with two precise tests: `GenerateHash_ContentChange_ReturnsDifferentHash` (content changes produce a different hash) and `GenerateHash_MetadataOnlyChange_ReturnsSameHash` (changing only `LastModifiedUtc` now correctly produces the *same* hash, documenting the intentional exclusion of sync metadata from the canonical signature document). Reason: after `LastModifiedUtc` was removed from the canonical hash the original test, which asserted both content and timestamp changes produce different hashes, began failing.
- **`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`** — Replaced the `Assert.Contains("\"reason\":..."` assertion in `PT_Creates_DailyNote_WithOverride_PersistsOverrideLogAndAudit` with `Assert.DoesNotContain("\"reason\":"...)`. Reason: the preceding Sprint II hardening removed the free-text justification from `OVERRIDE_APPLIED` audit metadata; the E2E test was still asserting the old (PHI-unsafe) shape.



- **`src/PTDoc.Infrastructure/Compliance/HashService.cs`** — Removed `LastModifiedUtc` from the canonical document used for SHA-256 signature hashing. Reason: any sync-metadata or save-path update that touches `LastModifiedUtc` without changing clinical content would invalidate the hash and break signature verification.
- **`src/PTDoc.Application/Compliance/IAuditService.cs`** — Removed the free-text override `reason` from the `OverrideApplied` audit metadata (now logs only `ruleType` and `timestamp`). The justification is still persisted in `RuleOverride.Justification` which is access-controlled. Reason: free-text justification may include PHI that must not appear in audit log metadata.
- **`src/PTDoc.Infrastructure/Compliance/OverrideService.cs`** — `RuleOverride.TimestampUtc` is now always set to `DateTime.UtcNow` on the server; the caller-supplied `request.Timestamp` is no longer trusted for the persisted timestamp. Reason: allowing client-provided timestamps enables backdating/forward-dating override audit records.
- **`src/PTDoc.Infrastructure/Compliance/OverrideService.cs`** — Override eligibility is now driven by `ComplianceSettings.AllowOverrideTypes` (parsed from DB) instead of a hard-coded `EightMinuteRule` check; `MinJustificationLength` from settings is also enforced. Falls back to `{EightMinuteRule}` when settings are absent or unpopulated. Reason: policy was split between runtime code and the `ComplianceSettings` schema; keeping it in one place prevents drift.
- **`src/PTDoc.Core/Models/ComplianceSettings.cs`** — Added `DefaultMinJustificationLength = 20` constant. Reason: used as fallback in `OverrideService` when no settings row exists in the database.
- **`src/PTDoc.Api/Notes/NoteEndpoints.cs`** — `TotalTimedMinutes` passed to compliance validation is now computed by summing `Minutes` from timed CPT entries (`IsTimed == true`) rather than copying `ClinicalNote.TotalTreatmentMinutes`. Reason: total treatment minutes includes untimed codes, so using it caused 8-minute rule evaluation with an inflated minute total.
- **`src/PTDoc.Api/Notes/NoteEndpoints.cs`** — Added an explicit null guard after note creation: if `result.IsValid` is `true` but `result.Note` is `null` the endpoint returns `500 InternalServerError` instead of throwing a `NullReferenceException`. Reason: the nullability contract was not enforced by the type system; a defensive check prevents silent runtime crashes.
- **`src/PTDoc.Application/DTOs/DailyNoteDtos.cs`** — `CptCodeEntryDto.Units` now defaults to `1` instead of `0`. Reason: `0` is an invalid billing-unit count; older clients that don't send a `units` field would silently produce invalid CPT entries.
- **`src/PTDoc.Infrastructure/Compliance/AddendumService.cs`** — `ContentIsEmpty` now treats empty JSON objects (`{}`) and empty JSON arrays (`[]`) as empty content, matching the intent of rejecting addendums with no meaningful payload. Reason: the previous check only handled `null`/`undefined`/blank strings, so `{}` or `[]` passed validation with no clinical content.

### Added - Sprint 2: Medicare override and compliance audit integration

- **`src/PTDoc.Application/Compliance/OverrideContracts.cs`**, **`src/PTDoc.Application/Compliance/NoteSaveValidation.cs`**, **`src/PTDoc.Application/Compliance/IRulesEngine.cs`** — Added typed compliance override contracts (`ComplianceRuleType`, `OverrideSubmission`, `OverrideRequest`, `OverrideRequirement`, `IOverrideService`) and extended validation/rule-result envelopes with structured rule metadata (`ruleType`, `isOverridable`, `overrideRequirements`) while preserving the legacy warning/error/`requiresOverride` shape. Reason: note save flows now need explicit, auditable override semantics instead of ad hoc warning strings.
- **`src/PTDoc.Infrastructure/Compliance/OverrideService.cs`**, **`src/PTDoc.Infrastructure/Compliance/OverrideWorkflow.cs`**, **`src/PTDoc.Infrastructure/Compliance/RulesEngine.cs`**, **`src/PTDoc.Infrastructure/Compliance/NoteSaveValidationService.cs`**, **`src/PTDoc.Application/Compliance/IAuditService.cs`**, **`src/PTDoc.Infrastructure/Compliance/AuditService.cs`** — Implemented PT-only override application, attestation resolution from `ComplianceSettings`, `OVERRIDE_APPLIED` / `HARD_STOP_TRIGGERED` audit helpers, and updated Medicare rule evaluation so 8-minute violations are explicitly overridable while Progress Note requirements remain hard stops. Reason: Medicare compliance requires explicit attestation, non-bypassable hard stops, and PHI-safe audit trails.
- **`src/PTDoc.Infrastructure/Services/NoteWriteService.cs`**, **`src/PTDoc.Infrastructure/Services/DailyNoteService.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.Application/DTOs/NoteDtos.cs`**, **`src/PTDoc.Application/DTOs/DailyNoteDtos.cs`**, **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`** — Integrated override enforcement into all server-side note save paths, including candidate note IDs for first-save audit events and explicit `422` responses when an overridable warning is active but no matching override payload is supplied. Reason: overrides must be explicit at save time and must not persist silently.
- **`src/PTDoc.Api/Notes/NoteEndpoints.cs`**, **`src/PTDoc.Api/Notes/DailyNoteEndpoints.cs`**, **`src/PTDoc.Api/Notes/NoteWorkspaceV2Endpoints.cs`**, **`src/PTDoc.Api/Program.cs`** — Added `POST /api/v1/notes/{noteId}/override`, mapped override-specific `403` / `422` API behavior, and registered `IOverrideService` in DI without changing existing note-write authorization policy boundaries. Reason: clients need an explicit compliance override API while keeping PT enforcement on the server.
- **`src/PTDoc.Core/Models/RuleOverride.cs`**, **`src/PTDoc.Core/Models/ComplianceSettings.cs`**, **`src/PTDoc.Infrastructure/Data/ApplicationDbContext.cs`**, **`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/20260404020000_AddComplianceOverrideNoteLink.cs`**, **`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/20260404020000_AddComplianceOverrideNoteLink.Designer.cs`**, **`src/PTDoc.Infrastructure.Migrations.Sqlite/Migrations/ApplicationDbContextModelSnapshot.cs`**, **`src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/20260404020010_AddComplianceOverrideNoteLink.cs`**, **`src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/20260404020010_AddComplianceOverrideNoteLink.Designer.cs`**, **`src/PTDoc.Infrastructure.Migrations.SqlServer/Migrations/ApplicationDbContextModelSnapshot.cs`**, **`src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/20260404020020_AddComplianceOverrideNoteLink.cs`**, **`src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/20260404020020_AddComplianceOverrideNoteLink.Designer.cs`**, **`src/PTDoc.Infrastructure.Migrations.Postgres/Migrations/ApplicationDbContextModelSnapshot.cs`** — Linked `RuleOverride` records to `ClinicalNote` via `NoteId`, updated tenant scoping to flow through the linked note’s clinic, and added provider-specific migrations/snapshots for the new FK and filtered index. Reason: override history must be tied to specific notes and remain tenant-safe.
- **`tests/PTDoc.Tests/Compliance/RulesEngineTests.cs`**, **`tests/PTDoc.Tests/Compliance/NoteComplianceIntegrationTests.cs`**, **`tests/PTDoc.Tests/Compliance/OverrideServiceTests.cs`**, **`tests/PTDoc.Tests/Security/AuthAuditTests.cs`**, **`tests/PTDoc.Tests/Tenancy/TenantIsolationTests.cs`**, **`tests/PTDoc.Tests/Integration/DatabaseProviderMigrationTests.cs`**, **`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`**, **`tests/PTDoc.Tests/Notes/DailyNoteServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`** — Updated compliance tests for typed rule metadata and `422` override enforcement, added override-service and audit-event coverage, extended tenancy/migration checks for `RuleOverrides.NoteId`, and added end-to-end coverage for save-time override success plus hard-stop override audit logging. Reason: the override/audit contract changes note-save behavior and requires dedicated coverage across service, API, tenancy, and persistence layers.

### Fixed - PR review: OverrideService and RuleOverride tenant filter corrections

- **`src/PTDoc.Infrastructure/Compliance/OverrideService.cs`** — Split the null-user check from the role check in `ApplyOverrideAsync`: a missing attesting user now throws `KeyNotFoundException` (not `UnauthorizedAccessException`), reserving the unauthorized exception for the wrong-role case. Reason: the previous check made a missing user indistinguishable from an incorrect role and produced misleading 403 responses.
- **`src/PTDoc.Infrastructure/Data/ApplicationDbContext.cs`** — Updated the `RuleOverride` global query filter to fall back to `User.ClinicId` when `NoteId` is null, so legacy rows (without a linked note) remain visible in tenant-scoped queries within their clinic instead of becoming invisible. Reason: the previous filter scoped exclusively via `Note.ClinicId`, silently hiding all pre-migration override records.

### Fixed - Send intake modal Razor parse regression

- **`src/PTDoc.UI/Components/SendIntakeModal.razor`** — Restored the missing `HandlePatientChanged` closing structure, added the `finally` busy-state cleanup, and reintroduced the `GenerateLinkAsync` method declaration so the component parses correctly again. Reason: a malformed `@code` block caused Razor `RZ1006` errors and cascading compile failures for injected services and `IAsyncDisposable`.
- **`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`** — Fixed client-push handling for clinical notes by rejecting `PendingCoSign` notes as read-only, parsing `noteType` from either enum-name strings or numeric values during note creation, and denormalizing `ClinicId` from the owning patient when creating a new note from sync payloads. Reason: sync tests showed pending co-sign notes were being accepted, `"ProgressNote"` payloads were defaulting to the wrong enum, and synced notes were losing tenant scoping metadata.
- **`src/PTDoc.Infrastructure/LocalData/LocalSyncOrchestrator.cs`** — Serialized pushed clinical-note `createdUtc` values in round-trip `"O"` format. Reason: sync payload tests require full-precision addendum metadata to survive push serialization without trimming fractional-second digits.

### Fixed - Sprint II: NoteStatus alignment (CI build fix)

#### Sprint-I / Sprint-II Merge: NoteStatus Status model aligned across workspace service layer
- **`INoteWorkspaceService.cs`** — `NoteWorkspaceSaveResult`, `NoteWorkspaceLoadResult`, and `NoteWorkspaceSubmitResult` now carry `NoteStatus Status` (Foundation Sprint-I pattern) and computed `bool IsSubmitted => Status != NoteStatus.Draft` instead of a settable `bool IsSubmitted`. `NoteWorkspaceSaveResult` retains Sprint-II fields (`Errors`, `Warnings`, `RequiresOverride`, `ComplianceWarning`). `NoteWorkspaceDraft` gains `int? LocalDraftId` (Sprint-I). Reason: `SoapNoteVm.IsSubmitted` was made read-only (computed) in Sprint-I, so Sprint-II code that tried to assign it directly caused a CS0200 compile error.
- **`NoteWorkspaceApiService.cs`** — All `NoteWorkspaceLoadResult`/`NoteWorkspaceSaveResult`/`NoteWorkspaceSubmitResult` constructors updated to set `Status = ...` (from `workspace.NoteStatus`, `note.SignedUtc.HasValue ? Signed : Draft`, `RequiresCoSign ? PendingCoSign : Signed`) instead of `IsSubmitted = ...`. Reason: DTOs now use Status-based model.
- **`NoteWorkspacePage.razor`** — Replaced all three `_note.IsSubmitted = x` assignments with `_note.Status = result.Status / saveResult.Status / submitResult.Status`. Removed `IsSubmitted = false` from the `SoapNoteVm` object initializer (default is `Status = NoteStatus.Draft` which computes to `IsSubmitted = false`). Reason: `SoapNoteVm.IsSubmitted` is a read-only computed property since Sprint-I.

#### SendIntakeModal.razor: Fixed missing method signature and unclosed braces
- **`SendIntakeModal.razor`** (`src/PTDoc.UI/Components/SendIntakeModal.razor`) — Fixed a pre-existing malformed `@code` block: `HandleGenerateLinkAsync`'s method body was present without its signature, and `HandlePatientChanged` was missing its `finally { _isBusy = false; }` block, its `if`-block closing `}`, and its method-closing `}`. Added the missing `finally` block, the two `}` closers, and the `private async Task HandleGenerateLinkAsync()` signature. Reason: The Razor source generator reported `RZ1006: The code block is missing a closing "}"` blocking the entire `PTDoc.UI` compilation and causing cascade errors in `SendIntakeModal.razor` and other files.

- **`ComplianceWarning`** (`src/PTDoc.Application/DTOs/NoteDtos.cs`) — Advisory compliance warning class surfaced alongside note operations when a rule fires at Warning severity (e.g. 8-minute rule). Non-null value is informational and does not block the operation.
- **`NoteOperationResponse.ComplianceWarning`** — Property added to the unified note operation response envelope so API callers can surface advisory warnings without treating them as errors.
- **`NoteWorkspaceSaveResult.ComplianceWarning`** (`src/PTDoc.UI/Services/INoteWorkspaceService.cs`) — Property added to the UI-layer save result DTO so workspace components can display advisory compliance warnings.
- **`NoteWorkspaceApiService.SaveLegacyDraftAsync`** — Maps `operation.ComplianceWarning` into `NoteWorkspaceSaveResult`.

#### Clinical Note Immutability and Addendums
- **Linked addendum note model** - Added `ClinicalNote.CreatedUtc`, `ClinicalNote.ParentNoteId`, and `ClinicalNote.IsAddendum` so all new addendums are stored as linked `ClinicalNote` rows and reuse the existing hash/signature pipeline instead of extending the legacy standalone `Addendum` write path.
- **`IAddendumService` / `AddendumService`** - Added a dedicated addendum creation service that only allows addendums from finalized signed primary notes, rejects addendum-of-addendum nesting, preserves the original note unchanged, and enqueues the new draft note for sync.
- **Note detail response** - Added `GET /api/v1/notes/{id}` returning the primary note plus ordered linked addendums, while still exposing legacy standalone `Addendum` rows read-only for compatibility.
- **Addendum request flexibility** - Updated `POST /api/v1/notes/{noteId}/addendum` to accept raw JSON content so clients can submit either structured SOAP payloads or plain-text addendum content.

#### Enforcement and Sync
- **Final-signature edit blocking** - Standardized signed-note immutability across note updates, workspace saves, objective metric mutation endpoints, daily-note same-day upsert, AI suggestion acceptance, and sync push conflict handling, all with the clinician-facing message `Signed notes cannot be modified. Create addendum.`
- **Audit trail events** - Added non-PHI `ADDENDUM_CREATE` and `EDIT_BLOCKED_SIGNED_NOTE` audit events for traceable addendum creation and blocked post-signature edits.
- **Primary-note query scoping** - Excluded `IsAddendum` rows from primary-note workflows including note lists, patient note history, daily-note lookup/taxonomy queries, carry-forward source selection, and Medicare progress-note frequency counting.
- **Offline note metadata** - Extended sync pull/push payloads and MAUI local note storage to preserve `CreatedUtc`, `ParentNoteId`, and `IsAddendum` for linked addendum notes without syncing legacy standalone addendum rows.

#### EF Core Migrations
- **Provider migrations** - Added `AddClinicalNoteLinkedAddendums` migrations and updated snapshots for SQLite, PostgreSQL, and SQL Server, including the self-referencing foreign key and backfill of existing `ClinicalNotes.CreatedUtc` values from `LastModifiedUtc`.

### Fixed - Sprint 2: PR Review Feedback

- **Standardized immutability message** - Aligned `NoteWriteService` exception message to the canonical clinician-facing string `"Signed notes cannot be modified. Create addendum."` used across all other endpoints.
- **`GET /api/v1/notes/{id}` addendum resolution** - Requesting an addendum note ID now resolves to its primary note and returns that note's full detail response; requesting a non-existent parent returns `404`.
- **Linked-addendum query scoping** - The linked-addendum LINQ query on `GET /api/v1/notes/{id}` now explicitly filters `IsAddendum == true` so only addendum rows are returned even if `ParentNoteId` is populated for other purposes.
- **`UpdateNote` standardized error** - Replaced the rules-engine message with the canonical `"Signed notes cannot be modified. Create addendum."` in the `PATCH` update endpoint's immutability response; the detailed rules-engine result is still logged via `LogRuleEvaluationAsync`.
- **`IAddendumService` required dependency** - `SignatureService` now requires `IAddendumService` as a constructor-injected required dependency instead of an optional parameter, so misconfiguration fails at DI startup rather than returning a user-visible error at runtime.



#### Legal eSignature Backend
- **`IHashService` / `HashService`** - Added deterministic SHA-256 uppercase hex hashing over canonical note state, including persisted note fields, canonicalized content JSON, sorted CPT payloads, and sorted objective metrics, with malformed-JSON fallback behavior for both `JsonException` and `ArgumentException`.
- **`SignatureService` legal signature flow** - Extended the existing signature pipeline to require explicit consent and intent, bind signatures to authenticated PT/PTA users, persist one `Signature` row per legal signing event, and distinguish PTA `PendingCoSign` workflow from final PT signature completion.
- **Tamper verification with legacy fallback** - Added latest-signature verification by recomputing the note hash; when no `Signature` rows exist, falls back to the legacy `ClinicalNotes.SignatureHash` field so pre-upgrade notes remain verifiable.
- **Default attestation text** - Stored the default legal attestation on each persisted signature record without introducing new schema.
- **Audit hooks for signatures** - Added `SIGN` and `VERIFY` audit events with note/user/timestamp metadata only and no PHI-bearing payloads.

#### API and Workflow Updates
- **`POST /api/v1/notes/{noteId}/sign`** - Now requires `{ consentAccepted, intentConfirmed }`, derives the signer role from the authenticated principal, captures IP/user-agent metadata when present, and returns explicit note status plus co-sign requirement.
- **`POST /api/v1/notes/{noteId}/co-sign`** - Kept as the PT-only compatibility alias while requiring the same consent/intent request contract and finalizing PTA daily notes through the shared legal signature flow.
- **`GET /api/v1/notes/{noteId}/verify`** - Added canonical verification endpoint returning `{ isValid, message }` while preserving `GET /verify-signature` as a compatibility alias.
- **Explicit consent/intent on signature** - `SubmitAsync` in `NoteWorkspaceApiService` now accepts `consentAccepted` and `intentConfirmed` as parameters instead of hardcoding `true`, and `NoteWorkspacePage` presents a dedicated consent/intent dialog that requires the clinician to explicitly check both boxes before the API call is made.

#### Changed
- **Objective metric mutability** - Objective metrics are now editable until the note is truly finalized so PTA-signed `PendingCoSign` notes remain modifiable before the final PT signature, matching the deferred immutability scope of the next PR.

#### Verification
- **Compliance test coverage** - Added deterministic hash tests, legal signature service tests, verification/tamper detection tests, endpoint authorization inventory coverage for `GET /api/v1/notes/{noteId:guid}/verify`, workspace submit payload coverage, and end-to-end sign/verify route coverage.
- **Integration auth harness** - Seeded deterministic internal users for the role-based integration auth handler so stricter signer-to-user binding remains active in end-to-end tests.
### Added - Sprint 2: Rules Engine Enforcement

#### Centralized Compliance Validation
- **`ValidationResult` / `ValidatedOperationResponse`** - Added a shared compliance validation envelope with `isValid`, `errors`, `warnings`, and `requiresOverride` for note save and evaluation flows.
- **`INoteSaveValidationService`** - Added a service-layer validation orchestrator that merges Progress Note and 8-minute rule results before persistence.
- **`INoteWriteService` / `NoteWriteService`** - Added a dedicated note write service so `/api/v1/notes` create/update enforcement runs outside controllers/endpoints.

#### Rules Engine Enforcement
- **`IRulesEngine.CheckProgressNoteDueAsync`** - Added Medicare-specific Progress Note due evaluation with warning thresholds at 8 visits / 25 days and hard stops at 10 visits / 30 days.
- **`IRulesEngine.ValidateTimedUnitsAsync`** - Added timed CPT enforcement for missing CPT data, mixed timed/untimed entries, `<5` minute hard stops, `5-7` minute warning+override cases, and overbilled timed-unit warning+override cases.
- **Progress Note reset behavior** - Signed Evaluation and signed Progress Note records now reset PN visit/day counters for server-side enforcement.
- **Shared timed-unit calculation path** - `CalculateCptTime` now uses the same aggregate timed-unit helper as save validation to avoid drift between helper output and enforced billing logic.

#### Note Save and API Behavior
- **Daily note draft saves** - `DailyNoteService.SaveDraftAsync` now validates compliance before `SaveChangesAsync` and returns a typed save envelope instead of a tuple result.
- **Workspace saves** - `NoteWorkspaceV2Service.SaveAsync` now returns a typed validation envelope and surfaces warnings/override flags for timed CPT rule checks.
- **Structured save responses** - Note create/update, daily note saves, and workspace saves now return top-level `isValid`, `errors`, `warnings`, and `requiresOverride`, with `422 Unprocessable Entity` for compliance hard stops.
- **Compliance evaluate endpoints** - `/api/v1/compliance/evaluate/pn-frequency/*` and `/api/v1/compliance/evaluate/8-minute-rule` now return `ValidationResult`.
- **DTO contract updates** - Daily note and workspace CPT models now carry `Units` and `Minutes` so timed-unit enforcement can run server-side from note payloads.

#### Client and Test Coverage
- **Workspace client handling** - The note workspace API client now preserves structured compliance errors, warnings, and override requirements from `422` save responses.
- **Compliance and integration tests** - Updated rules-engine, note-save, workspace, and end-to-end coverage for PN warnings/hard stops, 8-minute rule blocks/warnings, combined validation results, and structured save envelopes.

### Added - Sprint 2: Compliance Schema Foundation

#### Legal eSignature and Compliance Data Model
- **`Signature` entity** - New legal-grade signature table with note/user foreign keys, signer role, timestamp, signature hash, attestation text, consent/intent flags, and optional IP/device metadata.
- **`RuleOverride` entity** - New compliance override table capturing rule name, clinician justification, attestation text, timestamp, and required user relationship.
- **`ComplianceSettings` entity** - New future-facing compliance configuration table with default override attestation text, minimum justification length, and allowed override types payload.

#### Existing Schema Extensions
- **`Addendum`** - Extended with optional `SignatureHash` and explicit `CreatedByUserId -> Users` foreign key while preserving the existing append-only note linkage.
- **`AuditLog`** - Added standalone `EntityId` index support while retaining the existing richer audit structure (`EventType`, `CorrelationId`, `MetadataJson`, severity, success state).

#### EF Core Migrations
- **`ApplicationDbContext`** - Added `DbSet<Signature>`, `DbSet<RuleOverride>`, and `DbSet<ComplianceSettings>` plus explicit relationship, index, and default-value configuration for the new compliance foundation tables.
- **Provider migrations** - Added `AddComplianceSignatureEntities` migrations for SQLite, SQL Server, and Postgres.
- **Postgres migration cleanup** - Normalized `ClinicId` filtered index definitions in the model so the Postgres provider migration no longer includes unrelated index-filter churn.

#### Verification
- **Schema smoke coverage** - Updated `DatabaseProviderMigrationTests` to query `Signatures`, `RuleOverrides`, and `ComplianceSettings`.
- **SQLite migration apply** - Applied the new SQLite migration locally and verified `dotnet ef migrations has-pending-model-changes` returns clean.

### Added - Sprint I: Mandatory Changelog Enforcement Rule (AGENT-CHANGELOG-001)

#### Agent Behavioral Contract: `.github/agent.md`
- **`.github/agent.md`** — New agent behavioral contract file defining **AGENT-CHANGELOG-001**, the mandatory changelog enforcement rule for all contributors (human, AI, and automated agents). Specifies session-end update requirements, retroactive catch-up obligations, the full definition of what constitutes a "change", required entry format (description + affected files + reason), bypass conditions, and CI enforcement. Reason: establish a single, authoritative, enforceable rule that works across agent.md and GitHub Copilot instruction frameworks.

#### Copilot Instructions Update: `.github/copilot-instructions.md`
- **`.github/copilot-instructions.md`** — Added `### Mandatory Changelog Rule` section under *AI Development Behavior* referencing AGENT-CHANGELOG-001. Includes change category table, required entry format, catch-up requirement, and bypass policy. Updated Release Quality Gate checklist item for `docs/CHANGELOG.md` to mark it as mandatory for every session and to use the correct path. Reason: align AI coding agent behavior with AGENT-CHANGELOG-001 and make the enforcement rule visible in the primary instruction file.

### Fixed - Sprint I: Reviewer Feedback (PR Review Thread)

#### Intake EnsureDraft — Override Fields Applied on Creation (`src/PTDoc.Api/Intake/IntakeEndpoints.cs`)
- **`IntakeEndpoints.cs` / `EnsureDraft`** — When a new intake draft is created (`IntakeEnsureDraftStatus.Created`), the endpoint now applies `PainMapData`, `Consents` (normalized), `StructuredData`, and `TemplateVersion` from `EnsureIntakeDraftRequest` directly to the newly created `IntakeForm` before returning. Previously these request fields were discarded; the contract was misleading to callers. Affects: `EnsureDraft` handler, `IIntakeReferenceDataCatalogService` (injected). Reason: align behavior with DTO contract so callers can seed all intake fields on draft creation.

#### Intake SaveDraftAsync — Silent Drop Eliminated (`src/PTDoc.UI/Services/IntakeApiService.cs`)
- **`IntakeApiService.cs` / `SaveDraftAsync`** — When `existing` draft is null and a standalone access token is present, `SaveDraftAsync` now throws `HttpRequestException(HttpStatusCode.NotFound)` instead of silently returning. Prevents user edits from being silently discarded in standalone patient mode when the intake form is unavailable (expired link, deleted form, etc.). Reason: surface intake-unavailable state to the UI so it can show the correct error instead of appearing to succeed.

#### Changelog Path Fix (`docs/CHANGELOG.md`, `.github/copilot-instructions.md`)
- **`copilot-instructions.md`** — Updated Release Quality Gate checklist to reference `docs/CHANGELOG.md` instead of `CHANGELOG.md`, matching the actual file location and the canonical path used throughout the rule documentation.

### Fixed - Sprint I: Reviewer Feedback (Second PR Review Thread)

#### Intake EnsureDraft — Upfront Validation of Override Fields (`src/PTDoc.Api/Intake/IntakeEndpoints.cs`)
- **`IntakeEndpoints.cs` / `EnsureDraft`** — Override fields (`StructuredData`, `PainMapData`, `Consents`) from `EnsureIntakeDraftRequest` are now validated via `TryResolveStructuredData` and `TryNormalizeConsents` **before** `EnsureDraftAsync` is called. If validation fails, a 400 `ValidationProblem` is returned without creating a draft, consistent with `CreateIntake`/`UpdateIntake` behavior. Previously, validation failures were silently ignored and callers received a draft with partial/empty seeded fields. Reason: prevent misleading 201 responses when override data is invalid.

#### Intake SaveDraftAsync — Authenticated Silent Drop Eliminated (`src/PTDoc.UI/Services/IntakeApiService.cs`)
- **`IntakeApiService.cs` / `SaveDraftAsync`** — When `EnsureDraftAsync` returns `Existing` status but the subsequent `GetIntakeByPatientAsync` still returns null (race condition, tenant mismatch, etc.), `SaveDraftAsync` now throws `HttpRequestException(HttpStatusCode.NotFound)` instead of silently returning. Previously user edits could be silently dropped in authenticated mode too. Reason: surface intake-unavailable state uniformly across both standalone and authenticated modes.

#### SendIntakeModal — Neutral User-Facing Error Messages (`src/PTDoc.UI/Components/SendIntakeModal.razor`)
- **`SendIntakeModal.razor`** — Replaced `ex.Message` in all four `catch` blocks (`EnsureDraft`, `GenerateLink`, `CopyLink`, `Submit`) with neutral user-facing strings (e.g. "Unable to prepare the intake draft. Please try again."). Raw exception messages from HTTP/JSON/JS operations can expose implementation details or be confusing to users. Exception details are now logged to `Console.WriteLine` at the component level. Reason: HIPAA-safe user feedback that avoids leaking implementation details or internal URLs.

#### Dashboard — Structured Logging Replaces Console.WriteLine (`src/PTDoc.UI/Pages/Dashboard.razor`)
- **`Dashboard.razor`** — Injected `ILogger<Dashboard>` and replaced all five `Console.WriteLine($"... {ex.Message}")` calls with `Logger.LogWarning(ex, ...)`. Affects: `LoadPatientListAsync`, `LoadDashboardSnapshotAsync`, `LoadRecentActivityAsync`, `CreatePatientAsync`, and the intake state refresh handler. Reason: structured logging is diagnostics-safe, avoids writing raw exception messages to stdout, and integrates with the application's observability pipeline per HIPAA-conscious logging requirements.

#### SyncEngine — Orphan-Prevention Guard for IntakeForm/ClinicalNote Push (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ApplyEntityFromPayloadAsync`** — When creating a new `IntakeForm` or `ClinicalNote` from a client push payload, the engine now throws `InvalidOperationException` if `patientId == Guid.Empty` or `ResolvePatientClinicIdAsync` returns `null`. Previously, new records could be created with `ClinicId = null` and `PatientId = Guid.Empty`, breaking tenant scoping/query filters and creating orphaned entities. The outer push loop catches this as an "Error" status returned to the client. Reason: prevent malformed or unresolvable push payloads from creating orphaned, tenant-invisible records in the database.

### Added - Option C: CHANGELOG Enforcement Gate

#### CI Workflow: `changelog-required.yml`
- **`changelog-required.yml`** — New PR gate that fails if `docs/CHANGELOG.md` is not modified in the pull request. Passes when the `no-changelog` label is present (explicit bypass). Runs on `pull_request` events (opened, synchronize, reopened, ready_for_review, labeled, unlabeled) with minimal `pull-requests: read` permissions.

#### CI Workflow: `update-docs-on-merge.yml` (disabled push)
- **Removed `git push origin main` step** from the "Update CHANGELOG and Documentation" workflow. The step was causing `GH013` failures because repository rules require changes through a pull request. The step is replaced by a notice. CHANGELOG entries must now be included in the PR itself (see above gate).

### Added - Sprint K: Audit Closure and Release Blocker Triage

#### Secret Management Policy (CI Enforcement)
- **`ci-secret-policy.yml`** — New CI workflow that blocks PRs when tracked config files contain real (non-placeholder) signing keys. Runs on every pull request against `main`.
- **`[Category=SecretPolicy]` test trait** — Added `Trait("Category", "SecretPolicy")` to `ConfigurationValidationTests` tests that assert tracked config files contain only approved placeholder values. Enables targeted filtering in the new secret policy CI gate.

#### Governance Documentation
- **`docs/REMEDIATION_BASELINE.md`** — Sprint K remediation baseline: resolves the secret management policy contradiction (placeholders allowed; real secrets forbidden), confirms background service existence and wiring, documents CI governance baseline, and assigns all Sprint A–J gaps to remediation sprints O–T.
- **`docs/ACCEPTANCE_EVIDENCE_MAP.md`** — Sprint A–J acceptance evidence map: maps every acceptance criterion to its automated test, CI gate, or documented manual verification step. Identifies 6 open gaps assigned to Sprints O–T.

#### CI.md Update
- **Secret Policy CI (Sprint K)** section added to `docs/CI.md` documenting the new `ci-secret-policy.yml` workflow, failure conditions, approved placeholder values, and local reproduction commands.

### Added - Phase 8: Platform Integration & Completion

#### SQLCipher End-to-End Encryption
- **Database.Encryption.Enabled** - Toggleable encryption via appsettings (default: false)
- **Connection pre-open flow** - SQLCipher password applied via `SqliteConnectionStringBuilder` before EF uses connection (prevents silent encryption failure)
- **Microsoft.Data.Sqlite.Core** - SQLCipher support package
- **SQLitePCLRaw.bundle_e_sqlcipher** - SQLCipher encryption bundle
- **SecureStorageDbKeyProvider** - MAUI platform-specific key provider with fail-closed behavior
- **Key validation** - 32+ character minimum enforced at startup
- **Backwards compatibility** - Plain SQLite still works when encryption disabled

#### QuestPDF Production PDF Renderer
- **QuestPdfRenderer** - Production PDF renderer replacing MockPdfRenderer
- **Signature blocks** - Electronic signature footer with hash for signed notes
- **Unsigned watermark** - "UNSIGNED DRAFT" watermark for unsigned notes  
- **Medicare compliance** - CPT summary and billing unit footer
- **QuestPDF Community license** - Configured for open-source healthcare use

#### Integration Test Coverage
- **EncryptionIntegrationTests** - 3 tests validating encrypted/plain DB modes
- **NoPHIIntegrationTests** - 3 tests ensuring NO PHI in telemetry/audit/sync queue
- **PdfIntegrationTests** - 6 tests for PDF export (signed/unsigned, watermarks, immutability)
- **SyncIntegrationTests** - 5 tests for encrypted DB queue persistence

#### Design Documentation
- **docs/PHASE_8_DESIGN_SPECIFICATIONS.md** - Pre-implementation guardrails and technical design
- **6 Guardrails documented:**
  1. Encryption must be toggleable
  2. Connection must be pre-opened before EF
  3. MAUI SecureStorage must fail-closed
  4. QuestPDF renderer implemented with DbContext injection for data loading
  5. Integration tests (5 categories)
  6. Platform validation (CI-automatable)

### Changed
- **PR #18**: UI implementation patient intake by @BlackHouseDeveloper

- **PTDoc.Infrastructure.csproj** - Added SQLCipher and QuestPDF packages
- **PTDoc.Api/Program.cs** - Updated DbContext configuration with encryption toggle logic
- **PTDoc.Api/appsettings.Development.json** - Added Database.Encryption config section
- **DI Registration** - Replaced MockPdfRenderer with QuestPdfRenderer

### Fixed

- **iOS CI Build** - Switched MAUI iOS CI target from `iossimulator-arm64` to `ios-arm64` to resolve `actool` simulator runtime errors on `macos-15` + Xcode 16.2 runners.

### Security

- **Fail-closed encryption** - Invalid/missing keys throw at startup (no silent failures)
- **MAUI SecureStorage** - Platform-native secure key storage (no dev key fallback in production)
- **NO PHI in logs** - Integration tests validate telemetry contains only entity IDs
- **HIPAA-conscious design** - Audit trails, secure sessions, strict auth checks maintained

### Testing

- **74 tests passing** (59 baseline + 15 new integration tests)
- **Coverage areas:** Encryption, PDF export, NO PHI validation, Sync queue persistence
- **Zero regressions** - All Phase 1-7 tests continue passing

---

### Added - Phase 1: Patients Page UI Implementation

#### Patients List Page Components
- **PatientListItemVm.cs** - View model for patient list items with basic patient info
- **PatientPageHeader.razor** - Navy header component with title, subtitle, and action slots
- **PatientSearchInput.razor** - Search input component with icon (client-side filtering)
- **PatientCard.razor** - Individual patient card with hover states and click navigation
- **PatientCardSection.razor** - Responsive grid layout (3-column/2-column/1-column breakpoints)
- **Patients.razor** - Main patients list page at `/patients` route with sample data

#### Patient Profile Page Components
- **PatientProfileVm.cs** - View model for patient profile data structure
- **PatientProfileHeader.razor** - Profile header with back navigation button
- **PatientDemographicsCardEditable.razor** - Editable demographics card for patient details
- **PatientClinicalInfoCardEditable.razor** - Tabbed interface (Timeline/Notes/Documents)
- **PatientPrimaryActionButton.razor** - "Start New Note" primary action button
- **PatientProfile.razor** - Main patient profile page at `/patient/{id}` route

#### Design & Accessibility Features
- Full design token usage from `tokens.css` (no hardcoded colors/spacing)
- Light and dark theme support via CSS custom properties
- WCAG 2.1 AA compliant accessibility (keyboard navigation, ARIA labels, semantic HTML)
- Responsive breakpoints: Desktop (≥1200px), Tablet (768-1199px), Mobile (≤767px)
- `data-testid` attributes on all components for automated testing

### Changed

- **PTDoc.UI/_Imports.razor** - Added namespaces for new patient component hierarchy:
  - `@using PTDoc.UI.Components.Patients`
  - `@using PTDoc.UI.Components.Patients.Models`
  - `@using PTDoc.UI.Components.Patients.Profile`
  - `@using PTDoc.UI.Components.Patients.Profile.Models`
- **StatusBadge.razor.css** - Added border to success variant to match Figma v5 design specifications

### TODO - Phase 2: Backend Integration (Future Work)

#### Service Layer Integration
- [ ] Create `IPatientService` interface in PTDoc.Application with GetAll/GetById/Search methods
- [ ] Implement `PatientService` in PTDoc.Infrastructure with EF Core data access
- [ ] Add API endpoints in PTDoc.Api for patient CRUD operations
- [ ] Create Patient domain entity in PTDoc.Core.Models
- [ ] Add patient database migration with EF Core

#### Component Enhancement
- [ ] Replace sample data with real `IPatientService` calls in Patients.razor
- [ ] Replace sample data with real `IPatientService.GetById` in PatientProfile.razor
- [ ] Implement debounced search with backend API instead of client-side filtering
- [ ] Add LoadingSkeleton component for async loading states
- [ ] Implement save functionality for editable demographics/clinical info fields
- [ ] Complete Notes tab implementation in patient profile
- [ ] Complete Documents tab implementation in patient profile
- [ ] Implement "Start New Note" routing and workflow

#### Testing & Quality
- [ ] Add unit tests for patient view models
- [ ] Add integration tests for patient components
- [ ] Add E2E tests for patient workflows (list → profile → edit → save)
- [ ] Performance testing with 1000+ patient records

#### Infrastructure & CI/CD
- [ ] Set up CI/CD workflows (build, test, deploy for all platforms)
- [ ] Configure StyleCop and Roslynator checks in CI pipeline
- [ ] Add automated cross-platform build validation (Android, iOS, Web)
- [ ] Set up MCP workflows for database/PDF diagnostics
- [ ] Configure automated CHANGELOG updates via CI

### Added - Phase 2 Foundation: Canonical intake contracts and asset-backed provenance

- **`src/PTDoc.Application/Intake/IntakeStructuredDataModels.cs`**, **`src/PTDoc.Application/Intake/IntakeStructuredDataJson.cs`**, **`src/PTDoc.Application/ReferenceData/IIntakeReferenceDataCatalogService.cs`**, **`src/PTDoc.Application/ReferenceData/IntakeReferenceCatalogModels.cs`** — Expanded canonical intake structured data to include comorbidities, assistive devices, living situations, and house-layout selections, and added provenance-aware reference catalog models so future PRs can move intake off UI-local lists without inventing another payload shape. Reason: Branch 1 freezes the target intake contract before service and UI unification.
- **`src/PTDoc.Application/DTOs/IntakeDtos.cs`**, **`src/PTDoc.Api/Intake/IntakeEndpoints.cs`**, **`src/PTDoc.Api/Intake/IntakeAccessEndpoints.cs`** — Added optional typed `IntakeConsentPacket` request/response support while preserving the existing JSON field, and normalized create/update/ensure flows to prefer the canonical typed packet when present. Reason: introduce the canonical consent contract now without forcing the intake UI rewrite into the same PR.
- **`src/PTDoc.Application/Data/IntakeSupplementalReferenceData.json`**, **`src/PTDoc.Application/ReferenceData/ReferenceDataProvenance.cs`**, **`src/PTDoc.Infrastructure/ReferenceData/EmbeddedJsonResourceLoader.cs`**, **`src/PTDoc.Infrastructure/ReferenceData/IntakeSupplementalReferenceAsset.cs`**, **`src/PTDoc.Infrastructure/ReferenceData/IntakeReferenceDataCatalogService.cs`** — Landed the first embedded reference-data asset pipeline and moved the supplemental intake option sets onto document-backed assets with current `docs/clinicrefdata/...` provenance. Existing body-part/medication/pain-descriptor catalogs remain stable on this branch while later PRs expand the same pipeline to the broader datasets. Reason: establish the asset/provenance mechanism on a narrow, reviewable slice.
- **`src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`** — Added canonical workspace placeholders for exercise rows and general interventions, plus provenance-capable lookup/catalog contracts and evaluation seeding that prefers canonical structured intake supplemental selections when present. Reason: Branch 1 defines the target v2 model shape before structured editor work lands.
- **`tests/PTDoc.Tests/Application/IntakeStructuredDataJsonTests.cs`**, **`tests/PTDoc.Tests/ReferenceData/IntakeReferenceDataCatalogServiceTests.cs`** — Added coverage for supplemental structured intake normalization and asset-backed catalog provenance. Reason: protect the new Branch 1 contract surface without running broad integration work in the same slice.

### Added - Phase 2 Backend Unification: canonical lookup datasets and structured intake persistence

- **`src/PTDoc.Application/Data/WorkspaceLookupReferenceData.json`**, **`src/PTDoc.Application/ReferenceData/WorkspaceLookupReferenceDataAsset.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/WorkspaceReferenceCatalogService.cs`**, **`src/PTDoc.Infrastructure/Services/WorkspaceCatalogIcd10Service.cs`**, **`src/PTDoc.Api/ReferenceData/Icd10Endpoints.cs`**, **`src/PTDoc.Api/Program.cs`** — Moved the runtime ICD and CPT lookup truth onto a shared embedded asset with current `docs/clinicrefdata/...` provenance, expanded CPT coverage beyond the bootstrap four-code list, and aligned the generic ICD endpoint with the same canonical workspace catalog instead of the separate bundled service. Reason: Branch 2 needs one backend lookup dataset per domain before UI work can safely bind to it.
- **`src/PTDoc.Application/Data/OutcomeMeasureReferenceData.json`**, **`src/PTDoc.Application/Outcomes/OutcomeMeasureCatalogAsset.cs`**, **`src/PTDoc.Application/Outcomes/IOutcomeMeasureRegistry.cs`**, **`src/PTDoc.Infrastructure/Outcomes/OutcomeMeasureRegistry.cs`** — Replaced the hardcoded typed outcome-measure registry with an embedded document-backed asset and added provenance on registry definitions. Reason: keep outcome recommendations and scoring metadata on the same source-identifiable backend path as the other reference domains.
- **`src/PTDoc.Infrastructure/Services/IntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/InMemoryIntakeService.cs`**, **`src/PTDoc.Infrastructure/Services/MockIntakeService.cs`** — Persisted and rehydrated canonical `StructuredDataJson` across create/save/submit flows, deep-cloned structured intake payloads in the non-database services, and made pain-map projection prefer the canonical structured model when it exists. Reason: Branch 2 closes the service-layer gap where canonical intake structure existed at the API boundary but was still dropped or flattened during service persistence.
- **`tests/PTDoc.Tests/Intake/IntakeServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/WorkspaceReferenceCatalogServiceTests.cs`**, **`tests/PTDoc.Tests/Outcomes/OutcomeMeasureRegistryTests.cs`** — Added focused coverage for canonical structured-data persistence, current-source CPT/ICD lookup provenance, modifier coverage, and outcome-registry provenance. Reason: protect the new Branch 2 backend unification slice without pulling in frontend/editor work.

### Added - Phase 2 Intake UI Canonicalization: structured intake selections and consent packet binding

- **`src/PTDoc.UI/Components/Intake/Models/IntakeWizardState.cs`**, **`src/PTDoc.Application/Services/IntakeResponseDraft.cs`**, **`src/PTDoc.UI/Services/IntakeApiService.cs`**, **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`** — Promoted `IntakeConsentPacket` into the intake draft and page state, made create/ensure/update requests send the typed canonical consent packet alongside structured intake data, and normalized the intake page save/submit path to build readiness from canonical consent completeness instead of the legacy ad hoc booleans alone. Reason: Branch 3 needs the intake UI to write through the same canonical contracts that Branches 1-2 established at the API and persistence layers.
- **`src/PTDoc.UI/Components/Intake/Cards/MedicalHistoryCard.razor`** — Rebound comorbidities, assistive devices, living situations, and home-layout selections to the server-backed intake reference catalog and canonical structured intake ids, while still synchronizing the legacy label sets for draft compatibility. Reason: remove the remaining UI-local supplemental intake lists from the active intake authoring path.
- **`src/PTDoc.UI/Components/Intake/Steps/DemographicsStep.razor`**, **`src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor`** — Bound HIPAA/treatment consent, communication preferences, specialty authorizations, PHI release, billing authorization, media consent, authorized contacts, and review completion state to `IntakeConsentPacket`, and switched the review summary to read supplemental medical-history selections from canonical structured intake ids. Reason: make the intake review UX consume and edit the same canonical consent and structured-data payload that the backend now persists.
- **`tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs`**, **`tests/PTDoc.Tests/Intake/IntakeApiServiceTests.cs`** — Updated intake UI coverage to seed canonical supplemental intake ids and added a focused request-contract test for typed consent-packet submission. Reason: protect the Branch 3 canonical intake UI slice without mixing in workspace structured-editor coverage.

### Added - Phase 2 Workspace Structured Editors: catalog-backed Subjective, Objective, Plan, and review alignment

- **`src/PTDoc.UI/Components/Notes/Models/SubjectiveVm.cs`**, **`src/PTDoc.UI/Components/Notes/Models/ObjectiveVm.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Expanded the shared note-workspace UI models to carry structured functional limitations, objective metrics, special tests, tender muscles, exercise rows, treatment focuses, and general interventions, then rewired the v2 mapper to round-trip those structures without collapsing them back into flat legacy text fields. Reason: Branch 4 has to stop the UI mapper from dropping canonical workspace data before the editors can be trusted.
- **`src/PTDoc.UI/Components/Notes/Workspace/SubjectiveTab.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/ObjectiveTab.razor`**, **`src/PTDoc.UI/Components/Notes/Workspace/PlanTab.razor`**, **`src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor`** — Replaced the static subjective functional-limitation checklist with `BodyRegionCatalog` data, added catalog-backed objective editors for ROM, MMT/joint mobility, special tests, tender muscles, posture, and exercises, added plan authoring for treatment focuses and general interventions, and synchronized the selected structured body part across the workspace tabs. Reason: put the existing backend catalog/reference work onto the actual clinician authoring path instead of leaving it stranded behind placeholders.
- **`src/PTDoc.UI/Components/Notes/SoapReviewPage.razor`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`**, **`tests/PTDoc.Tests/UI/Notes/StructuredWorkspaceEditorsTests.cs`**, **`tests/PTDoc.Tests/UI/Notes/SoapReviewPageTests.cs`** — Updated review rendering to read structured metrics, special tests, observations, treatment focuses, and interventions directly, and added focused mapper/editor/review coverage for the new Branch 4 surfaces. Reason: align review output and regression coverage with the structured fields now being authored in the workspace UI.
- **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Changed objective-metric save mapping to merge visible rows with the preserved structured payload by index so sparse legacy metric entries loaded from v2 are not dropped on the next save just because the UI row omitted a display name. Reason: Branch 4 must preserve existing structured metrics during round-trip saves while newer catalog-backed editors are still normalizing older payload shapes.
- **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceApiServiceTests.cs`** — Extended the same preservation behavior to special-test save mapping and aligned the focused round-trip fixture with the canonical named-metric shape expected by the current editor path. Reason: Branch 4 should preserve structured objective findings during save/load even when older payloads are only partially normalized.

### Added - Phase 2 Compliance, Backfill, and Legacy Removal: first backend enforcement slice

- **`src/PTDoc.Application/Intake/IntakeConsentPacket.cs`**, **`src/PTDoc.Api/Intake/IntakeEndpoints.cs`**, **`tests/PTDoc.Tests/Application/IntakeConsentJsonTests.cs`**, **`tests/PTDoc.Tests/Security/PfptRoleComplianceTests.cs`**, **`tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`** — Tightened server-side intake submission validation so canonical consent completeness now requires both active HIPAA acknowledgement and active treatment consent, not just a HIPAA flag. Success-path submit fixtures were updated to carry canonical treatment consent, and a focused submit test now verifies that drafts remain unlocked when treatment consent is missing. Reason: Branch 5 starts by moving required-consent enforcement fully onto the canonical server contract instead of relying on UI-only readiness checks.
- **`src/PTDoc.Application/Compliance/NoteSaveValidation.cs`**, **`src/PTDoc.Infrastructure/Compliance/NoteSaveValidationService.cs`**, **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`tests/PTDoc.Tests/Compliance/NoteComplianceIntegrationTests.cs`**, **`tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs`**, **`tests/PTDoc.Tests/Notes/DailyNoteServiceTests.cs`** — Expanded note save validation to accept canonical diagnosis-code context, enforced the existing four-diagnosis backend cap for workspace saves, and added server-side CPT modifier validation against the canonical workspace catalog when a CPT code resolves to a source-backed entry. Reason: Branch 5 must start enforcing the same billing and diagnosis integrity rules on the backend that the structured workspace UI already assumes.
- **`src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`**, **`src/PTDoc.UI/Services/NoteWorkspaceApiService.cs`** — Stopped minting new synthetic `"Legacy"` limitation categories during legacy-to-v2 translation and flat-limitations fallback merge; these entries now round-trip without fabricated category labels. Reason: Branch 5 cleanup should remove low-risk legacy artifacts from active v2 payload creation before larger fallback-path deletion work begins.

### Fixed - canonical consent packet null-safety

#### Normalize `RevokedConsentKeys` and `AuthorizedContacts` before first use
- **`src/PTDoc.UI/Services/IntakeApiService.cs`** — Normalized `draft.ConsentPacket.RevokedConsentKeys` and `AuthorizedContacts` to non-null immediately after assigning the packet from the server response, preventing `NullReferenceException` on `.Contains(...)` calls when a persisted draft carries `revokedConsentKeys: null` or `authorizedContacts: null`. Also applied the same `??=` normalization in `CloneConsentPacket` before calling `IntakeConsentJson.Serialize`, which accesses both lists without null guards. Reason: `JsonSerializer` can deserialize list properties as `null` even when the class initializer sets them to `new()`, so any path that reads or serializes a server-sourced packet must normalize first.
- **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`** — Applied the same `??=` normalization in the page-local `CloneConsentPacket` helper before calling `IntakeConsentJson.Serialize`, for the same reason. Reason: the page rehydrates draft state from persisted JSON on navigation, so the same null-list scenario applies during state clone operations.

### Fixed - CloneConsentPacket source mutation and ReviewStep consent logic

#### Non-mutating clone and correct revocation/contact-info propagation
- **`src/PTDoc.UI/Services/IntakeApiService.cs`** — Rewrote `CloneConsentPacket` to serialize the source packet with plain `JsonSerializer.Serialize` (non-mutating) and deserialize into a fresh instance, then normalize the clone's lists. Reason: `IntakeConsentJson.Serialize` mutates its argument (trims `AuthorizedContacts`, normalizes/sorts `RevokedConsentKeys`), so previous implementation had a silent side-effect on the caller's `Draft.ConsentPacket`.
- **`src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor`** — Applied the same non-mutating clone approach in the page-local `CloneConsentPacket` helper. Reason: page state clones during navigation could silently modify `State.ConsentPacket` in place, causing unpredictable revocation / contact list behavior on subsequent saves.
- **`src/PTDoc.UI/Components/Intake/Steps/ReviewStep.razor`** — Changed `MarketingCommunicationsRevoked` from an AND (`&&`) to an OR (`||`) across the three communication revocation keys so the checkbox reflects the correct state when any single channel is revoked rather than requiring all three. Also guarded `UpdateStateAsync` contact-info propagation so `ConsentPacket.CommunicationPhoneNumber` / `CommunicationEmail` are only overwritten when the corresponding state value is non-empty, preventing silent erasure of pre-populated consent contact details in legacy or partially-migrated drafts. Reason: aligns with the pattern already used in `IntakeApiService.BuildConsentPacket` and avoids server-side validation failures for communication consents.

---

## Version History

_No releases yet - project in active development_

---

**Note for Contributors:** This CHANGELOG is manually maintained during development. Once CI/CD workflows are established, automated updates will be added for build outcomes and deployment status.
