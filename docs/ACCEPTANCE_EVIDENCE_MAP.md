# PTDoc Acceptance Evidence Map — Sprints A–J

**Effective:** Sprint K  
**Status:** Baseline established  
**Purpose:** Maps every Sprint A–J acceptance criterion to its verification evidence
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
| ObjectiveMetric entity exists and persists | Test | *(missing — entity not yet implemented)* | ❌ | Sprint O |
| IntakeResponse contract aligned to PFPT spec | Test | *(IntakeForm used instead; contract drift not reconciled)* | ❌ | Sprint O |

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
| CRUD audit events (patient/note access) | Test | *(CRUD endpoints not yet fully implemented)* | ❌ | Sprint O / Sprint S |

---

## Sprint H — Local Sync Orchestrator

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `ILocalSyncOrchestrator.PushAsync` pushes local entities to server | Test | `LocalData/LocalSyncOrchestratorTests.cs` | ✅ | — |
| `ILocalSyncOrchestrator.PullAsync` pulls server data to local | Test | `LocalData/LocalSyncOrchestratorTests.cs` | ✅ | — |
| Sync allowlist enforced (only approved entity types synced) | Test | `Sync/SyncConflictResolutionTests.cs` | ⚠️ Partial | Sprint R |
| Signed note protected from sync overwrite | Test | *(not yet fully tested end-to-end)* | ❌ | Sprint R |
| Locked intake submission protected from sync overwrite | Test | *(not yet fully tested end-to-end)* | ❌ | Sprint R |

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
| Multi-node concurrency safety (distributed lock) | Manual | ⚠️ Not yet implemented — safe in single-node only | ⚠️ Partial | Sprint T |

---

## Sprint J — Tenant Isolation (Clinic Scoping)

| Acceptance Criterion | Evidence Type | Location / Reference | Status | Remediation Sprint |
|---|---|---|---|---|
| `ITenantContextAccessor` reads `clinic_id` from JWT/session claim | Test | `Tenancy/TenantIsolationTests.cs` — `[Category=Tenancy]` | ✅ | — |
| EF global query filters scope Patient/Appointment/ClinicalNote/IntakeForm by ClinicId | Test | `TenantIsolationTests.cs` | ✅ | — |
| Cross-tenant data not visible under non-admin role | Test | `TenantIsolationTests.cs` | ⚠️ Partial | Sprint S |
| ClinicId == null access path strictly gated | Test | *(null-ClinicId path permits cross-tenant visibility — needs strict gating)* | ❌ | Sprint S |
| Default dev clinic seeded and usable for local testing | Manual | `DatabaseSeeder.cs` — clinic ID `00000000-0000-0000-0000-000000000100` | ✅ | — |

---

## Summary — Gap Count by Sprint

| Sprint | Total Criteria | Evidenced | Partial | Gap |
|---|---|---|---|---|
| A — Secrets/Config | 8 | 8 | 0 | 0 |
| B — Domain/Migrations | 6 | 4 | 0 | 2 |
| C — DB Provider CI | 5 | 5 | 0 | 0 |
| D — MAUI Local DB | 4 | 4 | 0 | 0 |
| E — Production Config | 5 | 5 | 0 | 0 |
| F — Observability | 6 | 6 | 0 | 0 |
| G — Audit Service | 4 | 3 | 0 | 1 |
| H — Local Sync | 5 | 2 | 1 | 2 |
| I — Background Jobs | 8 | 7 | 1 | 0 |
| J — Tenant Isolation | 5 | 3 | 1 | 1 |
| **Total** | **56** | **47** | **3** | **6** |

---

## Open Gap Assignments

| Gap | Assigned Sprint |
|---|---|
| ObjectiveMetric entity and persistence | Sprint O |
| IntakeResponse/IntakeForm contract drift | Sprint O |
| CRUD audit events (patient/note access) | Sprint O / Sprint S |
| Sync allowlist completeness for clinical entities | Sprint R |
| Signed note / locked intake sync protection (end-to-end) | Sprint R |
| Tenant ClinicId == null strict gating | Sprint S |
| Cross-tenant visibility proof (comprehensive) | Sprint S |
| Multi-node background job concurrency safety | Sprint T |

---

*Last updated: Sprint K — March 2026*  
*This map must be updated at the end of each remediation sprint (O–T) to reflect newly evidenced criteria.*
