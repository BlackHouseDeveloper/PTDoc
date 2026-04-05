# Changelog

All notable changes to PTDoc will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

### Fixed - Sprint 3: PR Review Feedback (Conflict Resolution Engine)

#### SyncEngine — Delete Conflict Detection (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `DetectConflict`** — Client delete against a missing server record now returns `null` (treated as already-applied/Accepted) instead of `DeletedConflict`, making idempotent deletes safe for retries. `DeletedConflict` is now only raised when the server record exists, is archived/deleted, and the client is attempting an update — not when both sides agree on deletion. Reason: prevent clients from getting stuck in a permanent conflict loop when retrying deletes for already-removed rows.

#### SyncEngine — Duplicate Audit Events (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ResolveUnknownConflictAsync`** — Renamed internal audit event from `CONFLICT_DETECTED` to `CONFLICT_MANUAL_REQUIRED` to eliminate duplicate audit log rows for unknown/manual-required conflicts, since `ResolveConflictAsync` already emits `CONFLICT_DETECTED` unconditionally before dispatching to each resolver. Reason: one conflict event per resolution path — no duplicate rows.

#### SyncEngine — Legacy Conflict Receipt Backward Compatibility (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `ParseConflictReceipt`** — Added a `TryParseLegacyConflictReceipt` / `LooksLikeLegacyConflictMessage` fallback so pre-JSON plain-text conflict messages (e.g. "Server version is newer", "immutable", "locked") stored before the JSON envelope format are parsed as conflict receipts rather than treated as non-conflict errors. Reason: prevent behavior change for existing devices upgrading through this release where old receipts were stored as plain text.

#### SyncEngine — `BuildConflictResult` Error Field Contract (`src/PTDoc.Infrastructure/Sync/SyncEngine.cs`)
- **`SyncEngine.cs` / `BuildConflictResult`** — `Error` field is now `null` when `ResolutionType == LocalWins` (result `Status == "Accepted"`), matching the `ClientSyncPushItemResult.Error` contract that reserves this field for `Error`/`Conflict` statuses only. Reason: avoid confusing clients that inspect `Error` for success path filtering.

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
