# PTDoc Acceptance Evidence Map — Sprints A–T

**Effective:** Sprint T  
**Status:** Release readiness validated  
**Purpose:** Maps every Sprint A–T acceptance criterion to its verification evidence
(automated test, CI gate, or documented manual verification step).

---

## How to Read This Document

Each row records:

| Column | Meaning |
|---|---|
| **Acceptance Criterion** | The acceptance item as stated in the sprint definition |
| **Evidence Type** | `Test` = automated test, `CI Gate` = CI workflow job, `Manual` = documented verification step |
| **Location / Reference** | Test file / CI workflow / documentation section |
| **Status** | ✅ Evidenced · ⚠️ Partial · ❌ Gap (remediation sprint assigned) |
| **Remediation Sprint** | Sprint O–T (if gap) |

---

## Sprint A — Secrets, Configuration, and Startup Validation

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| JWT signing key placeholder detected and fails startup | Test | `Security/ConfigurationValidationTests.cs` — `JwtKey_PlaceholderValues_FailValidation` | ✅ | — |
| JWT signing key too short detected and fails startup | Test | `Security/ConfigurationValidationTests.cs` — `JwtKey_ShortKey_FailsLengthValidation` | ✅ | — |
| JWT signing key null/empty detected and fails startup | Test | `Security/ConfigurationValidationTests.cs` — `JwtKey_NullOrEmpty_FailsValidation` | ✅ | — |
| IntakeInvite signing key placeholder detected | Test | `Security/ConfigurationValidationTests.cs` — `IntakeInviteKey_PlaceholderValues_FailValidation` | ✅ | — |
| Environment variable overrides appsettings value | Test | `Security/ConfigurationValidationTests.cs` — `JwtKey_EnvironmentVariable_OverridesAppsettingsValue` | ✅ | — |
| Tracked `appsettings.json` contains only placeholder JWT key | Test + CI Gate | `ConfigurationValidationTests.cs` — `ApiAppsettings_JwtSigningKey_MustBeAPlaceholderOrEmpty` **+ `ci-secret-policy.yml`** | ✅ | — |
| Tracked `appsettings.Development.json` contains only placeholder IntakeInvite key | Test + CI Gate | `ConfigurationValidationTests.cs` — `WebAppsettingsDevelopment_IntakeInviteSigningKey_MustBeAPlaceholderOrEmpty` **+ `ci-secret-policy.yml`** | ✅ | — |
| Developer workflow documented for real secrets | Manual | `docs/REMEDIATION_BASELINE.md §1.4`, `docs/SECURITY.md`, `setup-dev-secrets.sh` | ✅ | — |

---

## Sprint B — Domain Model and Multi-Provider Migrations

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| EF Core migrations exist for SQLite | CI Gate | `ci-db.yml` — `db-sqlite` job (`MigrateAsync()`) | ✅ | — |
| EF Core schema compatible with SQL Server | CI Gate | `ci-db.yml` — `db-sqlserver` job (`EnsureCreated()`) | ✅ | — |
| EF Core schema compatible with PostgreSQL | CI Gate | `ci-db.yml` — `db-postgres` job (`EnsureCreated()`) | ✅ | — |
| Provider-specific migration assemblies exist and are selectable | CI Gate | `ci-db.yml` — all provider jobs | ✅ | — |
| ObjectiveMetric entity exists and persists | Test | `Integration/SprintOIntegrationTests.cs` + `Integration/SprintQSmokeCrudTests.cs` — `[Category=DatabaseProvider]` | ✅ | — |
| IntakeResponse contract aligned to PFPT spec | Test | `Integration/SprintOIntegrationTests.cs` — IntakeForm/PainMapData/Consents fields verified | ✅ | — |

---

## Sprint C — Database Provider CI

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| SQLite migration + persistence tests pass in CI | CI Gate | `ci-db.yml` — `db-sqlite` job | ✅ | — |
| SQL Server schema + persistence tests pass in CI | CI Gate | `ci-db.yml` — `db-sqlserver` job | ✅ | — |
| PostgreSQL schema + persistence tests pass in CI | CI Gate | `ci-db.yml` — `db-postgres` job | ✅ | — |
| Provider environment variable selection works | Test | `Integration/DatabaseProviderMigrationTests.cs` — `[Category=DatabaseProvider]` tests | ✅ | — |
| Tests skip gracefully when provider not configured | Test | `DatabaseProviderMigrationTests.cs` — SkippableFact skip conditions | ✅ | — |

---

## Sprint D — MAUI Encrypted Local Database

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| LocalDbContext created with encryption enabled | Test | `LocalData/LocalDbContextTests.cs` | ✅ | — |
| LocalRepository CRUD operations work | Test | `LocalData/LocalRepositoryTests.cs` | ✅ | — |
| SecureStorage key provider used in MAUI | Manual | `docs/MOBILE_ARCHITECTURE.md`, `src/PTDoc.Maui/MauiProgram.cs` | ✅ | — |
| Encryption key is not hardcoded in source | Test | `Security/ConfigurationValidationTests.cs` + `ci-secret-policy.yml` scan | ✅ | — |

---

## Sprint E — Production Database Configuration

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `Database:AutoMigrate` defaults to `IsDevelopment()` | Test | `Integration/ProductionConfigurationTests.cs` — `[Category=ProductionConfig]` | ✅ | — |
| `Database__AutoMigrate` env var overrides config | Test | `Integration/ProductionConfigurationTests.cs` | ✅ | — |
| Production appsettings disables AutoMigrate | Manual | `src/PTDoc.Api/appsettings.Production.json` — `"AutoMigrate": false` | ✅ | — |
| MAUI platform secret storage used (no dev fallback in prod) | Manual | `docs/MOBILE_ARCHITECTURE.md`, `PTDoc.Maui/MauiProgram.cs` | ✅ | — |
| Fail-closed behavior: invalid/missing key throws at startup | Manual | `src/PTDoc.Api/Program.cs` startup validation | ✅ | — |

---

## Sprint F — Observability and Migration Health Checks

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `GET /health/live` returns 200 without DB check | Manual | `src/PTDoc.Api/Health/` — liveness endpoint | ✅ | — |
| `GET /health/ready` returns JSON with DB + migration checks | Test | `Integration/ObservabilityTests.cs` — `[Category=Observability]` | ✅ | — |
| `GET /diagnostics/db` requires Bearer token | Test | `Integration/ObservabilityTests.cs` | ✅ | — |
| No pending migrations after `MigrateAsync()` | Test + CI Gate | `ObservabilityTests.cs` + `ci-db.yml` — `db-migration-validate` job | ✅ | — |
| `has-pending-model-changes` exits 0 in CI | CI Gate | `ci-db.yml` — `db-migration-validate` job | ✅ | — |
| `CanConnectAsync()` returns true after migration | Test | `ObservabilityTests.cs` | ✅ | — |

---

## Sprint G — Audit Service and Auth Events

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| Auth events logged (login, logout, failure) | Test | `Security/AuthAuditTests.cs` — `[Category=Security]` | ✅ | — |
| Audit events contain no PHI | Test | `Integration/NoPHIIntegrationTests.cs` | ✅ | — |
| Audit trail persistent in database | Test | `AuthAuditTests.cs` + DB tests | ✅ | — |
| CRUD audit events (patient/note access) | Test | `Compliance/NoteComplianceIntegrationTests.cs` — `[Category=Compliance]` (note edit audit trail) | ✅ | — |

---

## Sprint H — Local Sync Orchestrator

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `ILocalSyncOrchestrator.PushAsync` pushes local entities to server | Test | `LocalData/LocalSyncOrchestratorTests.cs` | ✅ | — |
| `ILocalSyncOrchestrator.PullAsync` pulls server data to local | Test | `LocalData/LocalSyncOrchestratorTests.cs` | ✅ | — |
| Sync allowlist enforced (only approved entity types synced) | Test | `Sync/SyncClientProtocolTests.cs` — `GetClientDeltaAsync_DefaultTypes_IncludesAllAllowedEntities` `[Category=OfflineSync]` | ✅ | — |
| Signed note protected from sync overwrite | Test | `Sync/SyncClientProtocolTests.cs` — `ReceiveClientPushAsync_RejectsPush_WhenClinicalNoteIsSigned` `[Category=OfflineSync]` | ✅ | — |
| Locked intake submission protected from sync overwrite | Test | `Sync/SyncClientProtocolTests.cs` — `ReceiveClientPushAsync_RejectsPush_WhenIntakeFormIsLocked` `[Category=OfflineSync]` | ✅ | — |

---

## Sprint I — Background Job Infrastructure

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `SyncRetryBackgroundService` registered as `IHostedService` | Manual | `src/PTDoc.Api/Program.cs` — `AddHostedService<SyncRetryBackgroundService>()` | ✅ | — |
| `SessionCleanupBackgroundService` registered as `IHostedService` | Manual | `src/PTDoc.Api/Program.cs` — `AddHostedService<SessionCleanupBackgroundService>()` | ✅ | — |
| Sync retry skips items at max retry count | Test | `BackgroundJobs/BackgroundJobTests.cs` — `SyncRetryJob_SkipsItems_AtMaxRetries` | ✅ | — |
| Sync retry respects MinRetryDelay | Test | `BackgroundJobs/BackgroundJobTests.cs` — `SyncRetryJob_SkipsItems_TooRecentlyFailed` | ✅ | — |
| Session cleanup delegates to IAuthService | Test | `BackgroundJobs/BackgroundJobTests.cs` — `SessionCleanupJob_CallsCleanupExpiredSessionsAsync` | ✅ | — |
| Single execution failure does not kill the service loop | Manual | `SyncRetryBackgroundService.cs` — try/catch around `ExecuteJobAsync` | ✅ | — |
| No PHI in background job logs | Manual | Logging only counts/statuses — verified by code review | ✅ | — |
| Multi-node concurrency safety (distributed lock) | Manual | ⚠️ Not yet implemented — safe in single-node only | ⚠️ Partial | Sprint T (out of scope per constraints) |

---

## Sprint J — Tenant Isolation (Clinic Scoping)

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `ITenantContextAccessor` reads `clinic_id` from JWT/session claim | Test | `Tenancy/TenantIsolationTests.cs` — `[Category=Tenancy]` | ✅ | — |
| EF global query filters scope Patient/Appointment/ClinicalNote/IntakeForm by ClinicId | Test | `TenantIsolationTests.cs` | ✅ | — |
| Cross-tenant data not visible under non-admin role | Test | `Tenancy/TenantIsolationTests.cs` — `[Category=Tenancy]` — comprehensive cross-tenant isolation tests | ✅ | — |
| ClinicId == null access path strictly gated | Test | `Tenancy/TenantIsolationTests.cs` — null-ClinicId system context tests added Sprint S | ✅ | — |
| Default dev clinic seeded and usable for local testing | Manual | `DatabaseSeeder.cs` — clinic ID `00000000-0000-0000-0000-000000000100` | ✅ | — |

---

## Summary — Gap Count by Sprint

| Sprint | Total Criteria | Evidenced | Partial | Gap |
|---|---|---|---|---|
| A — Secrets/Config | 8 | 8 | 0 | 0 |
| B — Domain/Migrations | 6 | 6 | 0 | 0 |
| C — DB Provider CI | 5 | 5 | 0 | 0 |
| D — MAUI Local DB | 4 | 4 | 0 | 0 |
| E — Production Config | 5 | 5 | 0 | 0 |
| F — Observability | 6 | 6 | 0 | 0 |
| G — Audit Service | 4 | 4 | 0 | 0 |
| H — Local Sync | 5 | 5 | 0 | 0 |
| I — Background Jobs | 8 | 7 | 1 | 0 |
| J — Tenant Isolation | 5 | 5 | 0 | 0 |
| K — Secret Policy CI | 3 | 3 | 0 | 0 |
| O — Domain Completion | 4 | 4 | 0 | 0 |
| P — RBAC Enforcement | 4 | 4 | 0 | 0 |
| Q — Migration CI Parity | 3 | 3 | 0 | 0 |
| R — Sync Completeness | 3 | 3 | 0 | 0 |
| S — Tenant Gating | 3 | 3 | 0 | 0 |
| T — Release Readiness | 5 | 5 | 0 | 0 |
| **Total** | **86** | **85** | **1** | **0** |

---

## Sprint K — Secret Policy CI Enforcement

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| CI scans tracked config files for real signing keys | CI Gate | `ci-secret-policy.yml` — `secret-policy-scan` job | ✅ | — |
| `[Category=SecretPolicy]` tests enforce same invariant | Test + CI Gate | `Security/ConfigurationValidationTests.cs` + `ci-secret-policy.yml` | ✅ | — |
| Policy documented and developer workflow established | Manual | `docs/REMEDIATION_BASELINE.md §1`, `setup-dev-secrets.sh` | ✅ | — |

---

## Sprint O — Domain Model Completion

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| ObjectiveMetric entity, FK, and cascade delete exist | Test | `Integration/SprintOIntegrationTests.cs` | ✅ | — |
| IntakeForm PainMapData and Consents JSON fields exist | Test | `Integration/SprintOIntegrationTests.cs` | ✅ | — |
| Patient, IntakeForm, ClinicalNote, ObjectiveMetric CRUD via API | Test | `Integration/SprintQSmokeCrudTests.cs` — `[Category=DatabaseProvider]` | ✅ | — |
| CRUD audit events emitted for note creation/editing | Test | `Compliance/NoteComplianceIntegrationTests.cs` — `[Category=Compliance]` | ✅ | — |

---

## Sprint P — RBAC Policy Enforcement

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| RBAC policy constants defined (PatientRead/Write, NoteRead/Write, etc.) | Test | `Security/RbacRoleMatrixTests.cs` — `[Category=RBAC]` | ✅ | — |
| PTA restricted from signing Eval/ProgressNote/Discharge | Test | `Security/RbacRoleMatrixTests.cs` — PTA domain guard tests | ✅ | — |
| Authorization policies registered in DI | Manual | `src/PTDoc.Api/Program.cs` — `AddPTDocAuthorizationPolicies()` | ✅ | — |
| Role matrix matches PFPT specification | Test | `Security/RbacRoleMatrixTests.cs` — full role/policy matrix coverage | ✅ | — |

---

## Sprint Q — Migration Assembly CI Parity

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| SQL Server `InitialCreate` migration applied via EF CLI in CI | CI Gate | `ci-db.yml` — `db-sqlserver` job (`dotnet ef database update`) | ✅ | — |
| PostgreSQL `InitialCreate` migration applied via EF CLI in CI | CI Gate | `ci-db.yml` — `db-postgres` job (`dotnet ef database update`) | ✅ | — |
| CRUD smoke tests pass on all three providers | Test + CI Gate | `Integration/SprintQSmokeCrudTests.cs` — `[Category=DatabaseProvider]` | ✅ | — |

---

## Sprint R — Sync Completeness

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| Entity allowlist expanded to all clinical types | Test | `Sync/SyncClientProtocolTests.cs` — `GetClientDeltaAsync_DefaultTypes_IncludesAllAllowedEntities` `[Category=OfflineSync]` | ✅ | — |
| Signed note immutability enforced by sync engine | Test | `Sync/SyncClientProtocolTests.cs` — `ReceiveClientPushAsync_RejectsPush_WhenClinicalNoteIsSigned` `[Category=OfflineSync]` | ✅ | — |
| Locked intake form protected from sync overwrite | Test | `Sync/SyncClientProtocolTests.cs` — `ReceiveClientPushAsync_RejectsPush_WhenIntakeFormIsLocked` `[Category=OfflineSync]` | ✅ | — |

---

## Sprint S — Tenant Gating

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| Cross-tenant data invisible under non-admin scope | Test | `Tenancy/TenantIsolationTests.cs` — `[Category=Tenancy]` | ✅ | — |
| ClinicId == null system-context path strictly gated | Test | `Tenancy/TenantIsolationTests.cs` — null-ClinicId system context tests | ✅ | — |
| NoteEdited audit event logged on update (no PHI in metadata) | Test | `Compliance/NoteComplianceIntegrationTests.cs` — `[Category=Compliance]` | ✅ | — |

---

## Sprint T — Release Readiness Validation

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| RBAC gate runs in CI on every PR to main | CI Gate | `ci-release-gate.yml` — `rbac-gate` job (`[Category=RBAC]`) | ✅ | — |
| Tenant isolation gate runs in CI on every PR to main | CI Gate | `ci-release-gate.yml` — `tenant-gate` job (`[Category=Tenancy]`) | ✅ | — |
| Offline sync gate runs in CI on every PR to main | CI Gate | `ci-release-gate.yml` — `offline-sync-gate` job (`[Category=OfflineSync]`) | ✅ | — |
| Compliance rules gate runs in CI on every PR to main | CI Gate | `ci-release-gate.yml` — `compliance-gate` job (`[Category=Compliance]`) | ✅ | — |
| Release readiness checklist generated as CI artifact | CI Gate | `ci-release-gate.yml` — `release-summary` job (artifact `release-readiness-checklist-*`) | ✅ | — |
| Migration gates validated (all providers) | CI Gate | `ci-db.yml` — all provider jobs + `db-migration-validate` | ✅ | — |
| Secret policy gates validated | CI Gate | `ci-secret-policy.yml` — `secret-policy-scan` + SecretPolicy tests | ✅ | — |
| Release readiness report documented | Manual | `docs/RELEASE_READINESS_REPORT.md` | ✅ | — |
| Acceptance matrix updated through Sprint T | Manual | `docs/ACCEPTANCE_EVIDENCE_MAP.md` (this document) | ✅ | — |
| Multi-node distributed lock (background jobs) | Manual | ⚠️ Out of scope per Sprint T constraints — single-node only; no additional dev scope permitted | ⚠️ Partial | — |

---

## Open Gap Assignments

All remediable gaps from Sprints A–S have been resolved. One known partial item remains
out of scope per Sprint T constraints:

| Gap | Status | Note |
|---|---|---|
| Multi-node background job concurrency safety (distributed lock) | ⚠️ Partial | Not in Sprint T scope — safe in single-node deployments; distributed lock would require new dev scope which Sprint T prohibits |

---

*Last updated: Sprint T — March 2026*  
*Sprint T closes all remediable evidence gaps. The acceptance matrix is complete through Sprint T.*
