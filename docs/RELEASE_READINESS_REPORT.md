# PTDoc Release Readiness Report — Sprint T

**Generated:** Sprint T — March 2026  
**Status:** ✅ Release Ready (pending CI gate confirmation)  
**Scope:** Sprints A–T (complete remediation cycle)

---

## 1. Executive Summary

Sprint T closes the final CI gate suite for PTDoc, converting all remediation work from
Sprints K–S into CI-validated release evidence. This report documents the security posture,
migration readiness, and test gate status required for production release.

All remediable acceptance criteria from Sprints A–J (identified in Sprint K baseline) have
been addressed. One known partial item (multi-node distributed lock for background jobs) is
explicitly out of scope per Sprint T constraints — no additional development scope is permitted
and single-node deployments are safe.

---

## 2. CI Gate Status

### 2.1 Sprint T Release Gates (`ci-release-gate.yml`)

| Gate | Job | Test Category | Status |
|------|-----|---------------|--------|
| RBAC Enforcement | `rbac-gate` | `[Category=RBAC]` | Must pass |
| Tenant Isolation | `tenant-gate` | `[Category=Tenancy]` | Must pass |
| Offline Sync / Conflict Resolution | `offline-sync-gate` | `[Category=OfflineSync]` | Must pass |
| Clinical Compliance Rules | `compliance-gate` | `[Category=Compliance]` | Must pass |
| End-to-End Workflow | `e2e-workflow-gate` | `[Category=EndToEnd]` | Must pass |
| Release Summary + Artifact | `release-summary` | (aggregates above) | Must pass |

### 2.2 Database Migration Gates (`ci-db.yml`)

| Gate | Job | Validation Method |
|------|-----|-------------------|
| SQLite migration | `db-sqlite` | `MigrateAsync()` + `[Category=DatabaseProvider]` tests |
| SQL Server migration | `db-sqlserver` | `dotnet ef database update` + `[Category=DatabaseProvider]` tests |
| PostgreSQL migration | `db-postgres` | `dotnet ef database update` + `[Category=DatabaseProvider]` tests |
| Model drift check | `db-migration-validate` | `has-pending-model-changes` + `[Category=Observability]` tests |

### 2.3 Secret Policy Gates (`ci-secret-policy.yml`)

| Gate | Job | Validation Method |
|------|-----|-------------------|
| Config file scan | `secret-policy-scan` | Python scanner on all tracked `appsettings*.json` files |
| Secret policy tests | `secret-policy-scan` | `[Category=SecretPolicy]` tests |

### 2.4 Core Build Gate (`ci-core.yml`)

| Gate | Job | Scope |
|------|-----|-------|
| Build test dependency graph | `build-and-test` | `tests/PTDoc.Tests/PTDoc.Tests.csproj` and project references |
| Format check | `build-and-test` | `dotnet format --verify-no-changes` |
| Core owner tests | `build-and-test` | `[Category=CoreCi]` |

---

## 3. Test Coverage Summary

### 3.1 Test Categories and CI Gate Mapping

| Category | Test Files | CI Gate |
|----------|-----------|---------|
| `RBAC` | `Security/RbacRoleMatrixTests.cs`, `Security/AuthorizationCoverageTests.cs`, `Security/RbacHttpSmokeTests.cs` | `ci-release-gate.yml` — `rbac-gate` |
| `Tenancy` | `Tenancy/TenantIsolationTests.cs` | `ci-release-gate.yml` — `tenant-gate` |
| `OfflineSync` | `Sync/SyncConflictResolutionTests.cs`, `Sync/SyncClientProtocolTests.cs`, `LocalData/LocalSyncOrchestratorTests.cs` | `ci-release-gate.yml` — `offline-sync-gate` |
| `Compliance` | `Compliance/RulesEngineTests.cs`, `Compliance/SignatureServiceTests.cs`, `Compliance/NoteComplianceIntegrationTests.cs` | `ci-release-gate.yml` — `compliance-gate` |
| `EndToEnd` | `Integration/EndToEndWorkflowTests.cs` | `ci-release-gate.yml` — `e2e-workflow-gate` |
| `DatabaseProvider` | `Integration/DatabaseProviderSmokeTests.cs` | `ci-db.yml` |
| `Observability` | `Integration/ObservabilityTests.cs` | `ci-db.yml` — `db-migration-validate` |
| `SecretPolicy` | `Security/SecretPolicyScanTests.cs` | `ci-secret-policy.yml` |
| `CoreCi` | `Security/AuthAuditTests.cs`, `Security/SecurityHeadersTests.cs`, `Integration/ProductionConfigurationTests.cs`, `Integration/SqlCipherAccessTests.cs` | `ci-core.yml` |

### 3.2 RBAC Coverage

The RBAC gate validates:
- All policy constants defined (`PatientRead`, `PatientWrite`, `NoteRead`, `NoteWrite`, `IntakeRead`, `IntakeWrite`, `ClinicalStaff`)
- Role assignments: PT, PTA, Admin, Aide, Patient
- PTA domain restriction: PTA cannot sign Eval, ProgressNote, or Discharge note types
- Authorization policy matrix matches PFPT specification

### 3.3 Tenant Isolation Coverage

The tenant isolation gate validates:
- `ITenantContextAccessor` reads `clinic_id` from JWT/session claim
- EF global query filters scope Patient, Appointment, ClinicalNote, IntakeForm by ClinicId
- Cross-tenant records are invisible to non-system contexts
- ClinicId == null system-context path is strictly gated (only contexts with `CurrentClinicId == null` see unscoped records)

### 3.4 Offline Sync Coverage

The offline sync gate validates:
- `SyncQueue` enqueue/dequeue semantics
- Conflict resolution: draft notes use Last-Write-Wins (LWW)
- Signed note immutability: `ReceiveClientPushAsync` rejects pushes to signed notes
- Locked intake immutability: `ReceiveClientPushAsync` rejects pushes to locked intake forms
- Entity allowlist: only Patient, Appointment, IntakeForm, ClinicalNote, ObjectiveMetric, AuditLog are synced
- Timestamp conflict detection
- Sync queue persistence with encrypted SQLite database

### 3.5 Compliance Coverage

The compliance gate validates:
- Progress note frequency rule (PN_FREQUENCY): hard stop after 10 visits without PN/Eval
- Evaluation frequency rule (EVAL_FREQUENCY): evaluation timing constraints
- Discharge note rule (DISCHARGE_TIMING): discharge note timing requirements
- Note immutability: signed/countersigned notes cannot be overwritten
- NoteEdited audit trail: audit events logged on note update (no PHI in metadata)
- PTA signing restrictions enforced at API level

---

## 4. Security Posture

### 4.1 Secret Management

| Control | Status | Evidence |
|---------|--------|---------|
| No real keys in tracked files | ✅ Enforced | `ci-secret-policy.yml` scan + `[Category=SecretPolicy]` tests |
| Placeholder values cause startup failure | ✅ Enforced | `Program.cs` startup validation + `ConfigurationValidationTests` |
| CI secrets ephemeral (not persisted) | ✅ Enforced | `ci-core.yml` — `openssl rand` at job start |
| Developer secrets via UserSecrets (not VCS) | ✅ Documented | `setup-dev-secrets.sh`, `docs/SECURITY.md` |
| MAUI encryption key via OS SecureStorage | ✅ Enforced | `PTDoc.Maui/MauiProgram.cs`, `docs/MOBILE_ARCHITECTURE.md` |

### 4.2 Authentication and Authorization

| Control | Status | Evidence |
|---------|--------|---------|
| JWT Bearer authentication | ✅ Implemented | `src/PTDoc.Api/Program.cs` + `AuthServiceTests` |
| Session token authentication (PIN) | ✅ Implemented | `src/PTDoc.Api/Auth/SessionTokenAuthHandler.cs` |
| RBAC policy enforcement | ✅ CI-Gated | `ci-release-gate.yml` — `rbac-gate` |
| PTA signing domain restriction | ✅ Tested | `RbacRoleMatrixTests.cs` |
| Auth events audited | ✅ Tested | `Security/AuthAuditTests.cs` |

### 4.3 Data Protection

| Control | Status | Evidence |
|---------|--------|---------|
| No PHI in logs or audit metadata | ✅ Tested | `Integration/NoPHIIntegrationTests.cs` |
| Tenant isolation (EF query filters) | ✅ CI-Gated | `ci-release-gate.yml` — `tenant-gate` |
| Local database encryption (SQLCipher) | ✅ Tested | `LocalData/LocalDbContextTests.cs` |
| Signed note immutability | ✅ CI-Gated | `ci-release-gate.yml` — `compliance-gate` + `offline-sync-gate` |
| Security response headers | ✅ Tested | `Security/SecurityHeadersTests.cs` |

### 4.4 CodeQL Security Scanning

CodeQL scanning runs via `codeql.yml` on every push and PR to main. No critical or high
alerts should be open at release time. See `codeql.yml` for current status.

---

## 5. Migration Readiness

### 5.1 Provider Support

| Provider | Migration Method | CI Validation |
|----------|-----------------|---------------|
| SQLite | `MigrateAsync()` at startup (dev) | `ci-db.yml` — `db-sqlite` |
| SQL Server | `dotnet ef database update` (prod) | `ci-db.yml` — `db-sqlserver` |
| PostgreSQL | `dotnet ef database update` (prod) | `ci-db.yml` — `db-postgres` |

### 5.2 Migration Safety

| Control | Status | Evidence |
|---------|--------|---------|
| No pending model changes | ✅ CI-Gated | `ci-db.yml` — `has-pending-model-changes` |
| `CanConnectAsync()` post-migration | ✅ Tested | `Integration/ObservabilityTests.cs` |
| Health check reports migration state | ✅ Tested | `GET /health/ready` endpoint |
| `AutoMigrate` defaults to `false` in production | ✅ Tested | `Integration/ProductionConfigurationTests.cs` |

### 5.3 Production Deployment Notes

1. Set `Database__Provider` to `SqlServer` or `Postgres`
2. Set `ConnectionStrings__PTDocsServer` to the production connection string
3. Run migrations manually before deploying: `dotnet ef database update -p <migrations-project> -s src/PTDoc.Api`
4. `Database__AutoMigrate` is `false` by default in production — do **not** enable for production deployments
5. See `docs/EF_MIGRATIONS.md` — *Production Deployment* section for full procedure

---

## 6. Known Limitations and Deferred Items

| Item | Status | Rationale |
|------|--------|-----------|
| Multi-node distributed lock (background jobs) | ⚠️ Single-node only | Sprint T prohibits new dev scope. Background services (`SyncRetryBackgroundService`, `SessionCleanupBackgroundService`) are safe in single-node deployments. Distributed lock (e.g., Redis) would be required for horizontal scaling. |
| MAUI iOS/Android device-level testing | ⚠️ Manual only | No active CI workflow builds `PTDoc.Maui`; simulator/device validation remains a manual pre-release step. |

---

## 7. Release Readiness Checklist

The following checklist must be satisfied before tagging a release:

### Automated (CI must be green)

- [ ] `ci-release-gate.yml` — all gates pass (`rbac-gate`, `tenant-gate`, `offline-sync-gate`, `compliance-gate`, `e2e-workflow-gate`, `release-summary`)
- [ ] `ci-db.yml` — all jobs pass (`db-sqlite`, `db-sqlserver`, `db-postgres`, `db-migration-validate`)
- [ ] `ci-secret-policy.yml` — `secret-policy-scan` job passes
- [ ] `ci-core.yml` — `build-and-test` job passes (`[Category=CoreCi]` + format check)
- [ ] `codeql.yml` — no new critical/high alerts

### Documentation and Manual Verification

- [ ] `docs/ACCEPTANCE_EVIDENCE_MAP.md` updated through Sprint T
- [ ] `docs/RELEASE_READINESS_REPORT.md` reviewed and approved
- [ ] `CHANGELOG.md` `[Unreleased]` section reviewed and finalized
- [ ] Production database migration procedure verified (`docs/EF_MIGRATIONS.md`)
- [ ] Production secret injection procedure verified (`docs/SECURITY.md`, `setup-dev-secrets.sh`)

### Clinical and Compliance

- [ ] AI output not auto-persisted (verified by code review — `NoteEndpoints.CreateNote` requires explicit acceptance)
- [ ] Signed clinical records cannot be overwritten (CI-gated by compliance + offline-sync gates)
- [ ] Medicare rules enforced at API level (CI-gated by compliance gate)
- [ ] No PHI in logs (CI-gated by `NoPHIIntegrationTests`)
- [ ] Offline-first behavior preserved for MAUI (CI-gated by offline-sync gate)

---

## 8. Reference Documents

| Document | Purpose |
|----------|---------|
| `docs/ACCEPTANCE_EVIDENCE_MAP.md` | Full acceptance criteria → evidence mapping (Sprints A–T) |
| `docs/CI.md` | CI pipeline documentation and workflow descriptions |
| `docs/SECURITY.md` | Security policy, auth design, HIPAA controls |
| `docs/EF_MIGRATIONS.md` | Migration commands and production deployment procedure |
| `docs/ARCHITECTURE.md` | Clean Architecture layer boundaries, observability, health checks |
| `docs/REMEDIATION_BASELINE.md` | Sprint K baseline: secret policy, background service evidence |

---

*Generated: Sprint T — March 2026*  
*This document is the authoritative release evidence pack for PTDoc Sprint T.*
