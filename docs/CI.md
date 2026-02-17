# PTDoc CI/CD Guidelines

## Overview

This document outlines continuous integration and deployment standards for PTDoc. While full CI/CD automation is not yet implemented, these guidelines establish patterns for future GitHub Actions workflows and development practices.

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
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

## Secrets Management

### Development Secrets

**Storage:** Local `appsettings.Development.json` (not committed)

**Example:**
```json
{
  "Jwt": {
    "SigningKey": "<generated-key-here>"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=dev.PTDoc.db"
  }
}
```

**Generation:**
```bash
# Generate JWT signing key
openssl rand -base64 64
```

### Production Secrets

**Storage (Future):**
- Azure Key Vault
- AWS Secrets Manager
- HashiCorp Vault

**Access:**
- Managed identities (no credentials in code)
- Principle of least privilege
- Audit all access

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
