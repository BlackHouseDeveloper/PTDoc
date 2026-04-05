# PTDoc CI/CD Guidelines

## Overview

This document outlines continuous integration and deployment standards for PTDoc. The active CI pipelines enforce build quality, test coverage, and database provider compatibility on every pull request.

## CI/CD Principles

### Core Values
1. **Fail Fast** - Catch issues early in the pipeline
2. **Repeatable Builds** - Same input → same output every time
3. **Automated Testing** - No manual testing for regressions
4. **Security First** - SAST, dependency scanning, secret detection
5. **HIPAA Compliance** - Audit trails, controlled deployments

### Pipeline Stages

```
┌─────────────┐    ┌──────────┐    ┌──────────┐    ┌────────────┐
│   Commit    │───▶│  Build   │───▶│   Test   │───▶│   Deploy   │
│   (Push/PR) │    │  (Multi- │    │  (Unit + │    │ (Staging → │
│             │    │ Platform)│    │  E2E)    │    │ Production)│
└─────────────┘    └──────────┘    └──────────┘    └────────────┘
                         │               │               │
                         ▼               ▼               ▼
                   [Validation]    [Quality Gates]  [Approval]
```

## Active CI Workflows

### `ci-core.yml` – Core Build & Test
Runs on every pull request against `main`. Validates:
- Restore/build of the single `tests/PTDoc.Tests/PTDoc.Tests.csproj` dependency graph
- `dotnet format` style checks
- `[Category=CoreCi]` tests only
- Superseded PR runs are canceled via workflow concurrency

### `ci-db.yml` – Database Provider CI (Sprint C) + Migration Validation (Sprint F)
Runs on every pull request against `main`. Validates database schema and persistence across all supported providers using a **provider matrix**. See [Database Provider Testing](#database-provider-testing) below.

The workflow also includes the `db-migration-validate` job (Sprint F) that verifies no model changes lack a migration and runs `[Category=Observability]` tests. See [Migration Validation (Sprint F)](#migration-validation-sprint-f) below.

### `codeql.yml` – Security Scanning
Static analysis via GitHub CodeQL on every pull request.

### `ci-secret-policy.yml` – Secret Policy Enforcement (Sprint K)
Runs on every pull request against `main` and every push to `main`. Enforces the PFPT secret management policy:
- Scans tracked JSON config files for real signing keys (non-placeholder, non-empty values).
- Runs `[Category=SecretPolicy]` tests, which enumerate the same tracked files and apply the shared rules manifest used by the workflow helper.

See [Secret Policy CI (Sprint K)](#secret-policy-ci-sprint-k) below.

### `changelog-required.yml` – CHANGELOG Gate (Option C)
Runs on every pull request (opened, synchronize, reopened, ready_for_review, labeled, unlabeled).
- **Fails** if `docs/CHANGELOG.md` is not modified in the PR.
- **Passes** if the PR carries the `no-changelog` label (explicit bypass).
- Permissions: `pull-requests: read` only (no repo write access).

Add this check as a **required status check** in your branch protection / ruleset for `main` so that PRs cannot be merged without a CHANGELOG entry.

**Process for contributors:**
- Add an entry under `## [Unreleased]` in `docs/CHANGELOG.md` as part of every PR.
- If the change genuinely needs no changelog entry (e.g., a typo fix, CI-only change), apply the `no-changelog` label to the PR to bypass the gate.

### `ci-release-gate.yml` – Release Gate Suite (Sprint T)
Runs on every pull request against `main` and on manual dispatch. The final CI gate
suite required for release readiness. See [Release Gate CI (Sprint T)](#release-gate-ci-sprint-t) below.

Legacy workflows `phase8-validation.yml` and `update-docs-on-merge.yml` have been retired. Current CI uses the workflows above plus reusable gate logic under `.github/workflows/_dotnet-category-gate.yml`.

---

## Database Provider Testing

**Sprint C** introduced a dedicated database provider CI workflow (`.github/workflows/ci-db.yml`) that runs the `[Category=DatabaseProvider]` integration tests against three database engines.

### Provider Matrix

| Job | Provider | Container Service | Tests |
|---|---|---|---|
| `db-sqlite` | SQLite (in-memory) | None | `DatabaseProviderSmokeTests` |
| `db-sqlserver` | Microsoft SQL Server 2022 | `mcr.microsoft.com/mssql/server:2022-latest` | `DatabaseProviderSmokeTests` |
| `db-postgres` | PostgreSQL 16 | `postgres:16-alpine` | `DatabaseProviderSmokeTests` |

### What Is Validated

For each provider, the tests assert that:

1. **Migration application** – SQLite applies migrations through `MigrateAsync()`. SQL Server and PostgreSQL first validate the provider-specific `dotnet ef database update` path in CI, then the smoke test verifies the already-migrated schema without applying migrations a second time.
2. **Schema queryability** – Core sets such as `Clinics`, `Patients`, `Users`, `IntakeForms`, `ClinicalNotes`, `ObjectiveMetrics`, and `RuleOverrides` can all be queried without schema errors.
3. **Data persistence** – A clinic/patient/user/intake/note/objective-metric/override graph can be inserted and retrieved successfully.
4. **Provider portability** – The same smoke assertions execute against SQLite, SQL Server, and PostgreSQL using the provider selected by `DB_PROVIDER`.

### CI Failure Conditions

CI fails (and the PR is blocked) if:

- Any migration cannot be applied.
- The SQL Server or PostgreSQL design-time `dotnet-ef` path cannot resolve the provider-specific migration assembly or startup wiring.
- Queryability fails for any required set.
- Persistence operations fail for the smoke graph.
- Provider-specific SQL syntax errors surface.

### Container Services Configuration

**SQL Server 2022:**
```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    env:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "CI_Strong!Passw0rd"
      MSSQL_PID: "Developer"
    ports:
      - 1433:1433
```

**PostgreSQL 16:**
```yaml
services:
  postgres:
    image: postgres:16-alpine
    env:
      POSTGRES_USER: ptdoc_ci
      POSTGRES_PASSWORD: ci_postgres_pass
      POSTGRES_DB: ptdoc_ci
    ports:
      - 5432:5432
```

### Environment Variables for Provider Selection

The integration tests read:

| Variable | Purpose |
|---|---|
| `DB_PROVIDER` | Selects provider: `sqlite`, `sqlserver`, `postgres` |
| `Database__ConnectionString` | Connection string for SQL Server or PostgreSQL |

When `DB_PROVIDER` is not set (local dev), the suite defaults to SQLite. Unsupported `DB_PROVIDER` values fail fast instead of silently falling back to SQLite.

### How to Reproduce CI Database Tests Locally

**SQLite (no setup required):**
```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=DatabaseProvider" \
  --verbosity normal
```

**SQL Server (requires Docker):**
```bash
# Start SQL Server
docker run -d --name sqlserver-ci \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD='CI_Strong!Passw0rd' \
  -p 1433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest

# Create database
docker exec sqlserver-ci \
  /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'CI_Strong!Passw0rd' -C \
  -Q "CREATE DATABASE PTDoc_CI;"

# Validate EF CLI migrations (matches CI)
EF_PROVIDER=sqlserver \
Database__ConnectionString="Server=localhost,1433;Database=PTDoc_CI;User Id=sa;Password=CI_Strong!Passw0rd;TrustServerCertificate=True" \
Jwt__SigningKey=ci-sqlserver-ephemeral-key-migration-apply-placeholder \
dotnet tool run dotnet-ef database update \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api \
  --context PTDoc.Infrastructure.Data.ApplicationDbContext

# Run tests
DB_PROVIDER=sqlserver \
Database__ConnectionString="Server=localhost,1433;Database=PTDoc_CI;User Id=sa;Password=CI_Strong!Passw0rd;TrustServerCertificate=True" \
CI_DB_MIGRATIONS_ALREADY_APPLIED=true \
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=DatabaseProvider" \
  --verbosity normal
```

**PostgreSQL (requires Docker):**
```bash
# Start PostgreSQL
docker run -d --name postgres-ci \
  -e POSTGRES_USER=ptdoc_ci \
  -e POSTGRES_PASSWORD=ci_postgres_pass \
  -e POSTGRES_DB=ptdoc_ci \
  -p 5432:5432 \
  postgres:16-alpine

# Validate EF CLI migrations (matches CI)
EF_PROVIDER=postgres \
Database__ConnectionString="Host=localhost;Port=5432;Database=ptdoc_ci;Username=ptdoc_ci;Password=ci_postgres_pass" \
Jwt__SigningKey=ci-postgres-ephemeral-key-migration-apply-placeholder \
dotnet tool run dotnet-ef database update \
  -p src/PTDoc.Infrastructure.Migrations.Postgres \
  -s src/PTDoc.Api \
  --context PTDoc.Infrastructure.Data.ApplicationDbContext

# Run tests
DB_PROVIDER=postgres \
Database__ConnectionString="Host=localhost;Port=5432;Database=ptdoc_ci;Username=ptdoc_ci;Password=ci_postgres_pass" \
CI_DB_MIGRATIONS_ALREADY_APPLIED=true \
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=DatabaseProvider" \
  --verbosity normal
```

---

## Migration Validation (Sprint F)

**Sprint F** introduced the `db-migration-validate` job in `ci-db.yml` to detect
migration drift and validate the operational health-check contracts.

### What the Job Does

| Step | Description |
|------|-------------|
| `[Category=Observability]` tests | Assert that `GetPendingMigrationsAsync()` returns empty after `MigrateAsync()`, that applied count equals assembly count, and that `CanConnectAsync()` returns true. |
| `dotnet ef migrations has-pending-model-changes` | Exits non-zero if the EF Core model snapshot diverges from the current domain model. Prevents unmigrated changes from merging. |

### When It Fails

The job blocks merging when:
- Any `[Category=Observability]` test fails (migration state or connectivity assertion).
- `has-pending-model-changes` exits non-zero (model change without a migration).

### Running Locally

```bash
# Run observability tests (SQLite – no setup required)
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=Observability" \
  --verbosity normal

# Check for pending model changes
EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api
```

---

## Secret Policy CI (Sprint K)

**Sprint K** introduced `ci-secret-policy.yml` to enforce the PFPT secret management policy on every
pull request to `main` and every push to `main`. The policy and its rationale are documented in
`docs/REMEDIATION_BASELINE.md`.

### What the Job Does

| Step | Description |
|------|-------------|
| Config file scan | Uses `git ls-files` to enumerate all tracked `appsettings*.json` files, then verifies that any `Jwt:SigningKey` or `IntakeInvite:SigningKey` present is either empty or a known placeholder. JSON parse errors cause an explicit failure (not a silent pass). The workflow helper reads both tracked-file globs and placeholder rules from `.github/scripts/secret_policy_rules.json`. |
| `[Category=SecretPolicy]` tests | Runs `SecretPolicyScanTests`, which execute the same tracked-file enumeration (`git ls-files`) and shared rules manifest as the workflow helper. |

### When It Fails

The job blocks merging when:
- A tracked config file contains a signing key that is not in the approved placeholder list and is not empty.
- Any `[Category=SecretPolicy]` test fails.

### Approved Placeholders

| File | Key | Permitted Value |
|------|-----|-----------------|
| `src/PTDoc.Api/appsettings.json` | `Jwt:SigningKey` | `REPLACE_WITH_A_MIN_32_CHAR_SECRET` or `""` |
| `src/PTDoc.Api/appsettings.Development.json` | `Jwt:SigningKey` | `DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET` or `""` |
| `src/PTDoc.Web/appsettings.Development.json` | `IntakeInvite:SigningKey` | Any value starting with `REPLACE_` or `""` |

### Running Locally

```bash
# Run the workflow helper directly
python3 .github/scripts/scan_secret_policy.py

# Run SecretPolicy tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=SecretPolicy" \
  --verbosity normal
```

---

## Release Gate CI (Sprint T)

**Sprint T** introduced `ci-release-gate.yml` to serve as the final CI gate suite for
release readiness validation. It runs on every pull request against `main` and on
manual dispatch.

### Gate Jobs

| Job | Filter | Purpose |
|-----|--------|---------|
| `rbac-gate` | `Category=RBAC` | Validates the RBAC owner suites: policy matrix, route authorization coverage, and HTTP smoke checks |
| `tenant-gate` | `Category=Tenancy` | Validates tenant isolation (EF query filters, ClinicId scoping, cross-clinic data gating) |
| `offline-sync-gate` | `Category=OfflineSync` | Validates offline sync protocol, conflict resolution (LWW, signed immutability, intake locking), queue state handling, and role-based sync scoping |
| `compliance-gate` | `Category=Compliance` | Validates compliance rule orchestration, signature/co-sign flows, and audit trail behavior |
| `e2e-workflow-gate` | `Category=EndToEnd` | Validates the full HTTP workflow harness without double-counting RBAC owner tests |
| `release-summary` | (aggregates above) | Checks all gates passed, generates a timestamped release readiness checklist artifact |

### Test Category Coverage

The CI owner categories are intentionally exclusive. `ci-core.yml` runs `[Category=CoreCi]`, and each release/database workflow filters one of the gate categories below.

| `[Category=RBAC]` | `Security/RbacRoleMatrixTests.cs`, `Security/AuthorizationCoverageTests.cs`, `Security/RbacHttpSmokeTests.cs` |
| `[Category=Tenancy]` | `Tenancy/TenantIsolationTests.cs` |
| `[Category=OfflineSync]` | `Sync/SyncConflictResolutionTests.cs`, `Sync/SyncClientProtocolTests.cs`, `LocalData/LocalSyncOrchestratorTests.cs` |
| `[Category=Compliance]` | `Compliance/RulesEngineTests.cs`, `Compliance/SignatureServiceTests.cs`, `Compliance/NoteComplianceIntegrationTests.cs` |
| `[Category=EndToEnd]` | `Integration/EndToEndWorkflowTests.cs` |
| `[Category=SecretPolicy]` | `Security/SecretPolicyScanTests.cs` |
| `[Category=Observability]` | `Integration/ObservabilityTests.cs` |
| `[Category=DatabaseProvider]` | `Integration/DatabaseProviderSmokeTests.cs` |

### Release Readiness Artifact

Every `release-summary` job run uploads a `release-readiness-checklist-{run_number}.md`
artifact (retained 90 days) documenting the gate result, commit SHA, and checklist for
manual sign-off items.

### When It Fails

The `release-summary` job fails (and blocks merging) when any of the five gate jobs did
not succeed. Each gate job also fails individually if its `dotnet test` run reports any
failing tests.

### Running Locally

```bash
# RBAC tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=RBAC" --verbosity normal

# Tenant isolation tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=Tenancy" --verbosity normal

# Offline sync tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=OfflineSync" --verbosity normal

# Compliance tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=Compliance" --verbosity normal

# End-to-end workflow tests
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=EndToEnd" --verbosity normal
```

### Related Documentation

- `docs/RELEASE_READINESS_REPORT.md` — full release evidence pack
- `docs/ACCEPTANCE_EVIDENCE_MAP.md` — acceptance criteria → evidence mapping through Sprint T

---

## Build Standards

### Enforced SDK Version

**Requirement:** All builds must use .NET 8.0.417 (per global.json)

**Rationale:** Ensures consistent behavior across environments

**Implementation:**
```yaml
# .github/workflows/build.yml (future)
- name: Setup .NET
  uses: actions/setup-dotnet@v3
  with:
    global-json-file: global.json
```

**Local Enforcement:**
```bash
# Verify SDK version matches global.json
./PTDoc-Foundry.sh
./cleanbuild-ptdoc.sh
```

### Multi-Platform Builds

**Requirement:** Build must succeed for all target platforms before merge

**Platforms:**
- Web (Blazor Server/WASM)
- API (REST service)
- MAUI iOS (net8.0-ios)
- MAUI Android (net8.0-android)
- MAUI macOS (net8.0-maccatalyst)

**Implementation:**
```bash
# Local validation
./cleanbuild-ptdoc.sh

# Or manually
dotnet build PTDoc.sln
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-ios
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-android
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst
```

### Clean Architecture Validation

**Requirement:** Dependency rules must be enforced

**Rules:**
1. Core → (no dependencies)
2. Application → Core only
3. Infrastructure → Application + Core
4. Presentation (Api/Web/Maui) → Infrastructure + Application

**Validation:**
```bash
# Automated check in cleanbuild-ptdoc.sh
./cleanbuild-ptdoc.sh

# Manual validation
dotnet list src/PTDoc.Core/PTDoc.Core.csproj reference
# Should return: No project references found

dotnet list src/PTDoc.Application/PTDoc.Application.csproj reference
# Should return: ../PTDoc.Core/PTDoc.Core.csproj only
```

## Testing Standards

### Test Pyramid

```
     ┌──────────┐
     │   E2E    │  ← Few, slow, high confidence
     ├──────────┤
     │Integration│ ← Moderate number, medium speed
     ├──────────┤
     │   Unit   │  ← Many, fast, focused
     └──────────┘
```

### Unit Tests

**Coverage Target:** 80% for Core and Application layers

**Naming Convention:**
```
{ClassName}Tests.cs
{MethodName}_Should{ExpectedBehavior}_When{Condition}
```

**Example:**
```csharp
// Example structure for future test implementation
public class CredentialValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ShouldReturnNull_WhenCredentialsInvalid()
    {
        // Arrange
        var validator = new CredentialValidator(mockContext);
        
        // Act
        var result = await validator.ValidateAsync("invalid", "wrong");
        
        // Assert
        Assert.Null(result);
    }
}
```

### Integration Tests

**Purpose:** Test API endpoints, database operations, authentication

**Setup:**
```csharp
// Use WebApplicationFactory for API tests
public class AuthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public AuthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task TokenEndpoint_ReturnsToken_WhenCredentialsValid()
    {
        // Test implementation
    }
}
```

### E2E Tests

**Purpose:** Test full user workflows across platforms

**Tools:**
- **Blazor:** bUnit for component testing
- **Web UI:** Playwright for browser automation
- **MAUI:** Platform-specific test runners (XCTest, Espresso)

### Test Execution

**Requirement:** All tests must pass before merge

**Local Execution:**
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific category
dotnet test --filter Category=Integration
```

**CI Execution (Future):**
```yaml
- name: Run Tests
  run: dotnet test --no-build --verbosity normal --logger "trx"
  
- name: Upload Test Results
  if: always()
  uses: actions/upload-artifact@v3
  with:
    name: test-results
    path: '**/*.trx'
```

## Code Quality Gates

### Static Analysis

**Tools:**
- **Roslyn Analyzers** - Built-in C# analysis
- **StyleCop** - Code style enforcement
- **SonarQube** (future) - Code quality metrics

**Enforcement:**
```xml
<!-- Directory.Build.props (future) -->
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

### Security Scanning

**Tools (Future):**
- **Dependabot** - Dependency vulnerability scanning
- **CodeQL** - Static application security testing (SAST)
- **Snyk** - Open source security scanning

**Critical Checks:**
- No hardcoded secrets (JWT keys, connection strings)
- Dependencies have no known vulnerabilities
- SQL injection prevention (parameterized queries)
- XSS prevention (Blazor handles by default)

### License Compliance

**Allowed Licenses:**
- MIT
- Apache 2.0
- BSD 3-Clause

**Prohibited Licenses:**
- GPL (copyleft restriction)
- Commercial licenses without approval

**Validation:**
```bash
# Check NuGet package licenses
dotnet list PTDoc.sln package --include-transitive | grep License
```

## Branching Strategy

### Branch Model

```
main (production)
  ├── develop (integration)
  │   ├── feature/patient-intake
  │   ├── feature/appointment-scheduling
  │   └── bugfix/auth-token-refresh
  └── hotfix/critical-security-patch
```

**Branch Types:**
- `main` - Production-ready code
- `develop` - Integration branch for next release
- `feature/*` - New features
- `bugfix/*` - Bug fixes
- `hotfix/*` - Critical production fixes
- `release/*` - Release preparation

### Branch Protection Rules (Future)

**main Branch:**
- Require pull request reviews (min 1 approval)
- Require status checks to pass (build + tests)
- Require linear history (no merge commits)
- Require signed commits
- Prevent force pushes

**develop Branch:**
- Require pull request reviews (min 1 approval)
- Require status checks to pass
- Allow force pushes (with lease)

## Deployment Strategy

### Environments

```
Development → Staging → Production
   (PR)        (main)     (Release Tag)
```

**Development:**
- Deployed on every PR (future)
- Ephemeral environments
- No PHI data (synthetic only)

**Staging:**
- Deployed on merge to main
- Persistent environment
- De-identified test data
- Full security scanning

**Production:**
- Manual approval required
- Deployed on release tag
- Blue-green deployment (zero downtime)
- Rollback capability

### Deployment Checklist

Before production deployment:
- [ ] All tests passing
- [ ] Security scans clean
- [ ] Database migrations tested
- [ ] Rollback plan documented
- [ ] Monitoring alerts configured
- [ ] Audit logging verified
- [ ] HIPAA compliance review complete
- [ ] Change advisory board approval (if required)

### Database Migrations

**Requirement:** Migrations must be reversible

**Process:**
1. Generate migration locally
2. Review SQL script
3. Test on staging database
4. Apply to production during maintenance window
5. Verify with smoke tests

**Rollback:**
```bash
# Revert to previous migration
EF_PROVIDER=sqlite dotnet ef database update PreviousMigrationName \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api
```

## Secrets Management

### Development Secrets

**Storage:** `dotnet user-secrets` — stored in your OS user profile, **never** in tracked config files.

`appsettings.Development.json` contains placeholder values only. The app performs fail-fast startup validation and will refuse to start if placeholder or missing keys are detected.

**Setup (one command after cloning):**
```bash
# macOS / Linux
./setup-dev-secrets.sh

# Windows (PowerShell)
.\setup-dev-secrets.ps1
```

**Manual setup:**
```bash
# Generate and store JWT signing key for PTDoc.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 64)" \
  --project src/PTDoc.Api/PTDoc.Api.csproj

# Generate and store IntakeInvite signing key for PTDoc.Web
dotnet user-secrets set "IntakeInvite:SigningKey" "$(openssl rand -base64 32)" \
  --project src/PTDoc.Web/PTDoc.Web.csproj
```

### CI Secrets

CI workflows generate ephemeral signing keys at runtime using `openssl rand -base64`. No committed secrets are required. The generated values are set as `GITHUB_ENV` environment variables using ASP.NET Core's `__` separator convention:
```
Jwt__SigningKey=<runtime-generated>
IntakeInvite__SigningKey=<runtime-generated>
```

### Production Secrets

**Storage (intended):**
- Azure Key Vault
- AWS Secrets Manager
- HashiCorp Vault

**Access:**
- Managed identities (no credentials in code)
- Principle of least privilege
- Audit all access

**Injection:** Set the following environment variables at runtime:
```
Jwt__SigningKey=<value>
IntakeInvite__SigningKey=<value>
```

## Monitoring & Observability

### Logging

**Structured Logging:**
```csharp
_logger.LogInformation(
    "Patient {PatientId} accessed by user {UserId} at {Timestamp}",
    patientId, userId, DateTime.UtcNow);
```

**Log Levels:**
- **Trace:** Detailed diagnostic info
- **Debug:** Development-time debugging
- **Information:** General informational messages
- **Warning:** Unexpected but recoverable events
- **Error:** Errors that need attention
- **Critical:** Critical failures requiring immediate action

### Metrics (Future)

**Key Metrics:**
- Request rate (requests/sec)
- Error rate (errors/sec)
- Response time (p50, p95, p99)
- Database query duration
- Authentication success rate

**Tools:**
- Application Insights (Azure)
- CloudWatch (AWS)
- Prometheus + Grafana (self-hosted)

### Alerting (Future)

**Alert Conditions:**
- Error rate > 1% for 5 minutes
- Response time p95 > 500ms for 5 minutes
- Failed login attempts > 10 in 1 minute (potential brute force)
- Database connection failures
- Certificate expiration < 7 days

## Incident Response

### Severity Levels

**P0 (Critical):**
- Complete service outage
- Data breach or PHI exposure
- Security vulnerability actively exploited

**Response Time:** 15 minutes  
**Notification:** Page on-call engineer

**P1 (High):**
- Partial service outage
- Major feature broken
- Performance severely degraded

**Response Time:** 1 hour  
**Notification:** Slack alert

**P2 (Medium):**
- Minor feature broken
- Non-critical bug
- Performance moderately degraded

**Response Time:** 4 hours  
**Notification:** Ticket assignment

**P3 (Low):**
- Cosmetic issue
- Enhancement request
- Documentation error

**Response Time:** Next sprint  
**Notification:** Backlog

### Incident Workflow

1. **Detect** - Monitoring alerts or user report
2. **Triage** - Assess severity and impact
3. **Communicate** - Notify stakeholders
4. **Mitigate** - Implement temporary fix
5. **Resolve** - Deploy permanent fix
6. **Post-Mortem** - Document lessons learned

## Compliance & Auditing

### HIPAA Compliance

**Requirements:**
- All deployments logged with user, timestamp, changes
- Access to production requires MFA
- PHI access tracked in audit logs
- Encryption in transit (TLS 1.2+) and at rest
- Regular security assessments

**Audit Trail:**
```csharp
// Log all PHI access
_auditLogger.LogInformation(
    "User {UserId} viewed patient {PatientId} at {Timestamp}",
    userId, patientId, DateTime.UtcNow);
```

### Change Management

**Process:**
1. Submit change request (PR)
2. Code review by peers
3. Automated testing
4. Security review (if security-related)
5. Approval by tech lead
6. Deployment to staging
7. Production deployment approval

## Rollback Procedures

### API/Web Rollback

**Blue-Green Deployment:**
1. Deploy new version to "green" environment
2. Run smoke tests
3. Switch traffic to "green"
4. Monitor for issues
5. If problems detected, switch back to "blue"

### MAUI App Rollback

**App Store:**
- Cannot force downgrade user installs
- Submit hotfix update ASAP
- Use kill switch/feature flags to disable broken features

**Kill Switch:**
```csharp
// Remote config check (future)
if (await _configService.IsFeatureEnabledAsync("PatientIntake"))
{
    // Show feature
}
```

## Performance Benchmarks

### Build Time Targets

| Platform | Target | Acceptable | Unacceptable |
|----------|--------|------------|--------------|
| Solution | < 30s  | < 60s      | > 60s        |
| API      | < 10s  | < 20s      | > 20s        |
| Web      | < 15s  | < 30s      | > 30s        |
| MAUI iOS | < 45s  | < 90s      | > 90s        |

### Test Execution Targets

| Test Type | Target | Acceptable | Unacceptable |
|-----------|--------|------------|--------------|
| Unit      | < 5s   | < 10s      | > 10s        |
| Integration | < 30s | < 60s     | > 60s        |
| E2E       | < 2m   | < 5m       | > 5m         |

## Future CI/CD Roadmap

### Phase 1: Basic Automation
- [ ] GitHub Actions workflow for build + test
- [ ] Automated PR checks (build, tests, linting)
- [ ] Branch protection rules

### Phase 2: Security & Quality
- [ ] Dependabot for dependency updates
- [ ] CodeQL for security scanning
- [ ] SonarQube for code quality

### Phase 3: Deployment Automation
- [ ] Automated staging deployments
- [ ] Manual approval gates for production
- [ ] Blue-green deployment strategy

### Phase 4: Advanced Observability
- [ ] Application Performance Monitoring (APM)
- [ ] Distributed tracing
- [ ] Real-time alerting

## Related Documentation

- [BUILD.md](BUILD.md) - Build instructions
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Common issues
- [SECURITY.md](SECURITY.md) - Security best practices
- [ARCHITECTURE.md](ARCHITECTURE.md) - System architecture
