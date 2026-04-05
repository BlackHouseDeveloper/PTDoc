# Changelog

All notable changes to PTDoc will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
