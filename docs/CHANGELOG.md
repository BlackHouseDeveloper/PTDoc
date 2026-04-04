# Changelog

All notable changes to PTDoc will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added - Sprint 2: Immutable Signed Notes and Linked Addendums

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
- **`SyncEngine.cs` merge conflict formatting** — Fixed improperly indented `CheckEntitySpecificConflictAsync` block and `ApplyEntityFromPayloadAsync` `ClinicalNote` creation block left over from the `Foundation-Enhancement/Fixes` merge; removed extra closing brace and corrected `noteTypeRaw` → `noteType` reference. Affects: `src/PTDoc.Infrastructure/Sync/SyncEngine.cs`.
- **`NoteWorkspaceV2Service.cs` merge conflict formatting** — Fixed improperly indented `if (note is not null)` guard block and removed extra closing brace in `SaveAsync` left over from the same merge. Affects: `src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs`.



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
- **Connection pre-open flow** - PRAGMA key set before EF uses connection (prevents silent encryption failure)
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

---

## Version History

_No releases yet - project in active development_

---

**Note for Contributors:** This CHANGELOG is manually maintained during development. Once CI/CD workflows are established, automated updates will be added for build outcomes and deployment status.
