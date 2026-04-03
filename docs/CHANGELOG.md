# Changelog

All notable changes to PTDoc will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added - Sprint 2: Legal eSignature Workflow and Document Hashing

#### Legal eSignature Backend
- **`IHashService` / `HashService`** - Added deterministic SHA-256 uppercase hex hashing over canonical note state, including persisted note fields, canonicalized content JSON, sorted CPT payloads, and sorted objective metrics, with malformed-JSON fallback behavior.
- **`SignatureService` legal signature flow** - Extended the existing signature pipeline to require explicit consent and intent, bind signatures to authenticated PT/PTA users, persist one `Signature` row per legal signing event, and distinguish PTA `PendingCoSign` workflow from final PT signature completion.
- **Tamper verification** - Added latest-signature verification by recomputing the note hash and returning structured valid/tampered outcomes instead of a bare boolean.
- **Default attestation text** - Stored the default legal attestation on each persisted signature record without introducing new schema.
- **Audit hooks for signatures** - Added `SIGN` and `VERIFY` audit events with note/user/timestamp metadata only and no PHI-bearing payloads.

#### API and Workflow Updates
- **`POST /api/v1/notes/{noteId}/sign`** - Now requires `{ consentAccepted, intentConfirmed }`, derives the signer role from the authenticated principal, captures IP/user-agent metadata when present, and returns explicit note status plus co-sign requirement.
- **`POST /api/v1/notes/{noteId}/co-sign`** - Kept as the PT-only compatibility alias while requiring the same consent/intent request contract and finalizing PTA daily notes through the shared legal signature flow.
- **`GET /api/v1/notes/{noteId}/verify`** - Added canonical verification endpoint returning `{ isValid, message }` while preserving `GET /verify-signature` as a compatibility alias.
- **Workspace submit compatibility** - Updated the note workspace API client to send the new consent/intent payload required by the legal signature endpoint.

#### Changed
- **Objective metric mutability** - Objective metrics are now editable until the note is truly finalized so PTA-signed `PendingCoSign` notes remain modifiable before the final PT signature, matching the deferred immutability scope of the next PR.

#### Verification
- **Compliance test coverage** - Added deterministic hash tests, legal signature service tests, verification/tamper detection tests, endpoint authorization inventory coverage for `GET /api/v1/notes/{noteId:guid}/verify`, workspace submit payload coverage, and end-to-end sign/verify route coverage.
- **Integration auth harness** - Seeded deterministic internal users for the role-based integration auth handler so stricter signer-to-user binding remains active in end-to-end tests.

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
