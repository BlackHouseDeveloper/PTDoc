# PTDoc Enterprise Development Guide

## Quick Start

### Prerequisites Checklist
- [ ] **macOS** (Apple Silicon or Intel) - Primary development platform
- [ ] **.NET 8.0 SDK** - [Download here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [ ] **Xcode** (latest) - Required for iOS/Mac targets
- [ ] **Git** - Version control
- [ ] **GitHub CLI** (optional) - For workflow automation

### Enterprise Development Setup

1. **Clone the repository:**
   ```bash
   git clone https://github.com/BlackHouseDeveloper/PTDoc.git
   cd PTDoc
   ```

2. **Run the automated setup:**
   ```bash
   ./PTDoc-Foundry.sh
   ```

3. **Create database and seed data:**
   ```bash
   ./PTDoc-Foundry.sh --create-migration --seed
   ```

4. **Verify your environment:**
   ```bash
   ./cleanbuild-ptdoc.sh
   ```

5. **Launch the application:**
   ```bash
   ./run-ptdoc.sh
   ```

## Development Workflows

### Daily Development Routine

```bash
# 1. Pull latest changes
git pull origin main

# 2. Restore and build
dotnet restore
dotnet build

# 3. Run API (Terminal 1)
dotnet run --project src/PTDoc.Api --urls http://localhost:5170

# 4. Run Web or MAUI (Terminal 2)
./run-ptdoc.sh
```

### Feature Development Workflow

1. **Create feature branch:**
   ```bash
   git checkout -b feature/patient-intake-form
   ```

2. **Make changes and test:**
   ```bash
   # Make code changes
   dotnet build
   dotnet test
   ```

3. **Update the changelog:**
   - Add a concise entry to `docs/CHANGELOG.md` describing the user-visible, architectural, compliance, or workflow changes included in the session or PR.

4. **Commit with conventional commits:**
   ```bash
   git add .
   git commit -m "feat(intake): add patient intake form component"
   ```

5. **Push and create PR:**
   ```bash
   git push -u origin feature/patient-intake-form
   gh pr create --title "Add Patient Intake Form" --body "Implements #123"
   ```

### Testing Workflow

```bash
# Run all tests
dotnet test

# Run tests with coverage (when test projects are added)
dotnet test --collect:"XPlat Code Coverage"

# Run tests for specific category
dotnet test --filter Category=Integration
```

### Database Development

#### Creating Migrations

```bash
# Using helper script (recommended)
./PTDoc-Foundry.sh --create-migration

# Manual EF Core command (SQLite - default)
EF_PROVIDER=sqlite dotnet ef migrations add AddPatientNotes \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api \
  --context ApplicationDbContext
```

See `docs/EF_MIGRATIONS.md` for SQL Server and Postgres migration commands.

#### Applying Migrations

```bash
# Apply all pending migrations (SQLite)
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api

# Apply to specific migration
EF_PROVIDER=sqlite dotnet ef database update AddPatientNotes \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api
```

#### Rolling Back Migrations

```bash
# Remove last migration (if not applied)
EF_PROVIDER=sqlite dotnet ef migrations remove \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api

# Revert database to previous migration
EF_PROVIDER=sqlite dotnet ef database update PreviousMigrationName \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api
```

## Production Deployment

### Required Environment Variables

Set the following environment variables before starting the API in production:

| Variable | Required | Description |
|----------|----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Yes | Set to `Production` |
| `Database__Provider` | Yes | `SqlServer` or `Postgres` |
| `ConnectionStrings__PTDocsServer` | Yes (non-SQLite) | Full database connection string |
| `Jwt__SigningKey` | Yes | ≥ 32-character secret key |

> **Security:** Inject secrets via environment variables, container secrets, or a
> secrets manager. Never commit connection strings or signing keys to the repository.

### Migration Safety

Automatic migrations are **disabled** in production by default. Apply migrations
explicitly during your deployment pipeline using the EF Core CLI (which reads
`EF_PROVIDER` and `Database__ConnectionString`):

```bash
# SQL Server
EF_PROVIDER=sqlserver \
  Database__ConnectionString="Server=prod-db;Database=PTDoc;Integrated Security=True;" \
  dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api

# PostgreSQL
EF_PROVIDER=postgres \
  Database__ConnectionString="Host=prod-db;Port=5432;Database=ptdoc;Username=ptdoc;Password=..." \
  dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.Postgres \
  -s src/PTDoc.Api
```

> **Note:** `Database__ConnectionString` is the design-time variable for `dotnet ef` CLI.
> The runtime API uses `ConnectionStrings__PTDocsServer` (see Required Environment Variables above).

See `docs/EF_MIGRATIONS.md` for full production deployment commands including
idempotent SQL script generation and rollback instructions.

### Enabling Auto-Migrate (Optional)

For managed container deployments that guarantee single-instance startup you can
re-enable automatic migration:

```bash
Database__AutoMigrate=true
```

See `docs/ARCHITECTURE.md` — *Production Database Configuration* for details on
provider selection and migration safety.

## Project Structure & Architecture

### Clean Architecture Layers

```
PTDoc/
├── src/
│   ├── PTDoc.Core/              # Domain entities (no dependencies)
│   ├── PTDoc.Application/       # Application interfaces and contracts
│   ├── PTDoc.Infrastructure/    # EF Core, services, implementations
│   ├── PTDoc.Api/               # REST API with JWT auth
│   ├── PTDoc.Web/               # Blazor Server/WASM web app
│   ├── PTDoc.Maui/              # .NET MAUI mobile/desktop app
│   └── PTDoc.UI/                # Shared Blazor Razor Class Library
├── docs/                        # Documentation
├── PTDoc-Foundry.sh            # Setup script
├── run-ptdoc.sh                # Launch helper
└── cleanbuild-ptdoc.sh         # Build script
```

### Dependency Rules

1. **Core** has zero dependencies - pure domain models
2. **Application** defines interfaces - no implementation details
3. **Infrastructure** implements Application interfaces
4. **Api/Web/Maui** are presentation layers
5. **Never** reference Infrastructure from Application or Core

### Adding New Features

#### 1. Domain Entity (Core)
```csharp
// src/PTDoc.Core/Models/Appointment.cs
namespace PTDoc.Core.Models;

public class Appointment
{
    public int Id { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
}
```

#### 2. Application Interface (Application)
```csharp
// src/PTDoc.Application/Services/IAppointmentService.cs
namespace PTDoc.Application.Services;

public interface IAppointmentService
{
    Task<List<Appointment>> GetUpcomingAsync();
    Task<Appointment?> GetByIdAsync(int id);
    Task<Appointment> CreateAsync(Appointment appointment);
}
```

#### 3. Infrastructure Implementation (Infrastructure)
```csharp
// src/PTDoc.Infrastructure/Services/AppointmentService.cs
namespace PTDoc.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly ApplicationDbContext _context;
    
    public AppointmentService(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<List<Appointment>> GetUpcomingAsync()
    {
        return await _context.Appointments
            .Where(a => a.ScheduledAt > DateTime.UtcNow)
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync();
    }
    
    // ... other implementations
}
```

#### 4. Blazor Component (UI)
```razor
@* src/PTDoc.UI/Components/AppointmentList.razor *@
@inject IAppointmentService AppointmentService

<div class="appointment-list">
    @if (isLoading)
    {
        <p>Loading appointments...</p>
    }
    else if (appointments.Any())
    {
        @foreach (var appointment in appointments)
        {
            <div class="appointment-card">
                <h3>@appointment.PatientName</h3>
                <p>@appointment.ScheduledAt.ToString("g")</p>
            </div>
        }
    }
</div>

@code {
    private List<Appointment> appointments = new();
    private bool isLoading = true;
    
    protected override async Task OnInitializedAsync()
    {
        appointments = await AppointmentService.GetUpcomingAsync();
        isLoading = false;
    }
}
```

## Healthcare-Specific Guidelines

### HIPAA Compliance Checklist

When working with patient data:

- [ ] Implement audit logging for all data access
- [ ] Use encrypted connections (HTTPS/TLS)
- [ ] Enforce session timeouts (15 min inactivity, 8 hr absolute)
- [ ] Validate all input to prevent injection attacks
- [ ] Store sensitive data encrypted at rest
- [ ] Log authentication attempts and failures
- [ ] Implement role-based access control
- [ ] Use secure password policies
- [ ] Handle PHI deletion requests properly

### Clinical Terminology Standards

Use standardized medical terminology:
- **ICD-10** codes for diagnoses
- **CPT** codes for procedures
- **LOINC** for lab observations
- **SNOMED CT** for clinical findings

### Accessibility Requirements

All components must meet **WCAG 2.1 AA** standards:

```razor
@* Good: Accessible form field *@
<label for="patient-dob">Date of Birth</label>
<input id="patient-dob" 
       type="date" 
       aria-required="true"
       aria-describedby="dob-hint" />
<span id="dob-hint" class="hint-text">Format: MM/DD/YYYY</span>

@* Bad: Missing label *@
<input type="date" placeholder="Date of Birth" />
```

See [ACCESSIBILITY_USAGE.md](ACCESSIBILITY_USAGE.md) for complete guidelines.

## Code Quality Standards

### Code Style

PTDoc follows Microsoft C# conventions with these additions:

```csharp
// Use explicit types (not var) for clarity
string patientName = "John Doe";

// Use expression-bodied members for simple properties
public string FullName => $"{FirstName} {LastName}";

// Use nullable reference types
public string? MiddleName { get; set; }

// Use async/await for I/O operations
public async Task<Patient> GetPatientAsync(int id)
{
    return await _context.Patients.FindAsync(id);
}
```

### Blazor Component Guidelines

**Critical Rules:**
1. Always use PascalCase for component names
2. Never mutate `[Parameter]` properties after initialization
3. Use `[EditorRequired]` for mandatory parameters
4. Show loading states for async operations
5. Update `_Imports.razor` when adding new namespaces

See [Blazor-Context.md](Blazor-Context.md) for comprehensive guidance.

### Git Commit Conventions

Use conventional commits format:

```
feat(auth): add JWT refresh token endpoint
fix(patient): resolve date formatting in PDF export
docs(readme): update setup instructions
chore(deps): upgrade to .NET 8.0.2
refactor(services): extract patient service interface
test(auth): add unit tests for token validation
```

**Types:**
- `feat` - New feature
- `fix` - Bug fix
- `docs` - Documentation changes
- `chore` - Maintenance tasks
- `refactor` - Code refactoring
- `test` - Adding tests
- `perf` - Performance improvements

## Debugging

### API Debugging

```bash
# Run with detailed logging
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/PTDoc.Api --verbosity detailed

# Watch logs
tail -f logs/ptdoc-api.log
```

### Web Debugging

```bash
# Enable Blazor debugging
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/PTDoc.Web
```

Then press `Shift+Alt+D` in browser to attach debugger.

### MAUI Debugging

**iOS Simulator:**
```bash
# Enable verbose logging
dotnet build -t:Run -f net8.0-ios src/PTDoc.Maui/PTDoc.csproj \
  -v detailed
```

**Android Emulator:**
```bash
# View logcat
adb logcat | grep PTDoc
```

### Database Debugging

```bash
# View database schema
sqlite3 dev.PTDoc.db ".schema"

# Query data
sqlite3 dev.PTDoc.db "SELECT * FROM Patients LIMIT 10;"

# Enable EF Core query logging
export Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information
```

## Performance Optimization

### API Performance

```csharp
// Use AsNoTracking for read-only queries
var patients = await _context.Patients
    .AsNoTracking()
    .ToListAsync();

// Use pagination
var patients = await _context.Patients
    .Skip(page * pageSize)
    .Take(pageSize)
    .ToListAsync();

// Use projection to reduce data transfer
var summaries = await _context.Patients
    .Select(p => new PatientSummary 
    { 
        Id = p.Id, 
        Name = p.Name 
    })
    .ToListAsync();
```

### Blazor Performance

```csharp
// Use @key for list rendering
@foreach (var patient in patients)
{
    <PatientCard @key="patient.Id" Patient="@patient" />
}

// Virtualize large lists
<Virtualize Items="@patients" Context="patient">
    <PatientCard Patient="@patient" />
</Virtualize>

// Use OnAfterRender for expensive operations
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await LoadHeavyDataAsync();
    }
}
```

## Security Best Practices

### Authentication

```csharp
// Always validate JWT tokens
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    // Endpoint requires authentication
}

// Check user claims
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
```

### Input Validation

```csharp
// Use data annotations
public class CreatePatientRequest
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [EmailAddress]
    public string? Email { get; set; }
    
    [Range(0, 120)]
    public int Age { get; set; }
}

// Validate in services
public async Task<Patient> CreateAsync(CreatePatientRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        throw new ValidationException("Patient name is required");
    }
    
    // ... create patient
}
```

## Documentation

### Inline Documentation

```csharp
/// <summary>
/// Retrieves a patient by their unique identifier.
/// </summary>
/// <param name="id">The patient's unique identifier</param>
/// <returns>The patient if found, null otherwise</returns>
/// <exception cref="UnauthorizedException">
/// Thrown when the user lacks permission to access the patient
/// </exception>
public async Task<Patient?> GetByIdAsync(int id)
{
    // Implementation
}
```

### API Documentation

PTDoc uses OpenAPI/Swagger for API documentation:

```bash
# View API docs (when API is running)
open http://localhost:5170/swagger
```

## Developer Diagnostics Mode

PTDoc includes developer diagnostics capabilities that can be surfaced across Blazor Web and .NET MAUI platforms. To keep production deployments secure by default, diagnostics are hidden unless explicitly enabled.

### Precedence Order

When diagnostic components initialize, they apply the first matching configuration source in this order:

1. **`PTDOC_DEVELOPER_MODE` environment variable** (`true`/`false`, case-insensitive)
2. **`App:DeveloperMode` setting** from active configuration (e.g., `appsettings.json`)
3. **Build-default fallback** (`true` for Debug builds, `false` for Release builds)

This allows production operators to temporarily enable diagnostics without redeploying.

### Platform-Specific Configuration

#### .NET MAUI (Mobile/Desktop)

Environment variables are read from the process environment.

**macOS/Linux:**
```bash
# Temporary (current shell session)
export PTDOC_DEVELOPER_MODE=true
./run-ptdoc.sh

# Or inline
PTDOC_DEVELOPER_MODE=true dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

**Windows PowerShell:**
```powershell
# Persistent (all new shells)
setx PTDOC_DEVELOPER_MODE 1

# Session only
$env:PTDOC_DEVELOPER_MODE='1'
dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

#### MAUI Android

Environment variables are not persisted between launches. Use configuration files instead:

**Option 1: Configuration File (Recommended)**
```json
// appsettings.Development.json
{
  "App": {
    "DeveloperMode": true
  }
}
```

**Option 2: ADB System Property (Temporary)**
```bash
# Set system property
adb shell setprop debug.ptdoc.developer_mode 1

# Application code would need to read this property
# This requires custom code to hydrate configuration
```

#### MAUI iOS

**Xcode Scheme Environment Variables:**
1. Open PTDoc.csproj in Xcode
2. Product → Scheme → Edit Scheme
3. Run → Arguments → Environment Variables
4. Add: `PTDOC_DEVELOPER_MODE` = `true`

**Configuration File:**
```json
// appsettings.Production.json
{
  "App": {
    "DeveloperMode": true
  }
}
```

#### Blazor WebAssembly

Browser sandboxes do not expose OS environment variables, so configuration typically resolves to:
- Configuration setting in `wwwroot/appsettings.json`
- Build default (Debug vs Release)

**Configuration:**
```json
// wwwroot/appsettings.Development.json
{
  "App": {
    "DeveloperMode": true
  }
}
```

### Configuration Examples

**appsettings.json Structure:**
```json
{
  "App": {
    "DeveloperMode": false,
    "DiagnosticsRequiredRole": "Operator"
  }
}
```

**appsettings.Development.json (Local Development):**
```json
{
  "App": {
    "DeveloperMode": true
  }
}
```

### Security Considerations

#### Production Deployments

**Critical:** Developer diagnostics can expose sensitive information:
- Database connection strings
- API endpoints and configuration
- User session data
- Application secrets

**Best Practices:**
1. Never enable in production without access controls
2. Use `App:DiagnosticsRequiredRole` to restrict access
3. Prefer environment variable overrides over config file changes
4. Audit diagnostic access in HIPAA-compliant logs
5. Disable after troubleshooting is complete

#### Healthcare Compliance

For HIPAA-compliant deployments:
- Limit diagnostic output to exclude PHI
- Log all diagnostic mode activations
- Require authentication to view diagnostics
- Auto-disable after time period
- Review diagnostic logs during security audits

### Diagnostic Features

When enabled, PTDoc may show:
- Performance metrics and timings
- API request/response inspection
- Authentication state details
- Cache hit/miss ratios
- Database query statistics
- Build and version information
- Admin-only developer fault injection for deterministic AI troubleshooting

### Diagnostics Troubleshooting

#### Diagnostics Not Showing

**Check precedence order:**
```bash
# 1. Environment variable (highest priority)
echo $PTDOC_DEVELOPER_MODE

# 2. Configuration file
cat src/PTDoc.Web/wwwroot/appsettings.json | grep DeveloperMode

# 3. Build configuration (default)
dotnet build -c Debug    # DeveloperMode defaults to true
dotnet build -c Release  # DeveloperMode defaults to false
```

#### Diagnostics Enabled in Production

**Immediate mitigation:**
```bash
# Unset environment variable
unset PTDOC_DEVELOPER_MODE

# Or set to false
export PTDOC_DEVELOPER_MODE=false

# Restart application
./run-ptdoc.sh
```

**Long-term fix:**
- Remove `"DeveloperMode": true` from production config files
- Add `.gitignore` entry for `appsettings.Production.json` if it contains overrides
- Implement role-based access control via `DiagnosticsRequiredRole`

### Implementation Example

```csharp
// Component reading developer mode setting
@inject IConfiguration Configuration

@code {
    private bool _developerMode;

    protected override void OnInitialized()
    {
        // Check environment variable first
        var envVar = Environment.GetEnvironmentVariable("PTDOC_DEVELOPER_MODE");
        if (!string.IsNullOrEmpty(envVar))
        {
            _developerMode = bool.Parse(envVar);
            return;
        }

        // Check configuration
        _developerMode = Configuration.GetValue<bool>("App:DeveloperMode", false);

        // Fallback to build configuration
#if DEBUG
        _developerMode = true;
#endif
    }
}
```

## Troubleshooting

For common issues, see:
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Detailed troubleshooting guide
- [BUILD.md](BUILD.md) - Build-specific issues

## Operational Diagnostics & Observability (Sprint F)

### Health Endpoints

The API exposes standard health check endpoints. Use them to verify database
connectivity and migration state after deployment:

```bash
# Liveness check (HTTP 200 when process is running)
curl http://localhost:5170/health/live

# Readiness check (JSON, HTTP 503 when database is unreachable)
curl http://localhost:5170/health/ready
```

**Readiness JSON response:**
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "database",   "status": "Healthy", "description": "Database is reachable.", "durationMs": 8.2 },
    { "name": "migrations", "status": "Healthy", "description": "All migrations are applied.", "durationMs": 3.1 }
  ]
}
```

| `status` | Meaning |
|----------|---------|
| `Healthy` | Database connected and all migrations applied |
| `Degraded` | Connected but pending migrations exist — investigate |
| `Unhealthy` | Database unreachable — HTTP 503 returned |

### Database Diagnostics Endpoint

Authenticated users can retrieve detailed database state:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5170/diagnostics/db
```

**Response:**
```json
{
  "provider": "Sqlite",
  "connectivity": "Connected",
  "migrationStatus": "Current",
  "appliedMigrationCount": 2,
  "pendingMigrationCount": 0,
  "pendingMigrations": []
}
```

> **Security:** Connection strings and encryption keys are never exposed in this response.

### Local Azure AI Setup

PTDoc's AI path is currently Azure OpenAI-only. For local development, prefer API user-secrets or environment variables. Using environment variables:

```bash
export FeatureFlags__EnableAiGeneration=true
export AzureOpenAIEndpoint="https://<your-resource>.cognitiveservices.azure.com"
export AzureOpenAIKey="<your-azure-openai-resource-key>"
export AzureOpenAIDeployment="ptdoc-gpt-4o-mini"
export AzureOpenAIApiVersion="2025-01-01-preview"
export Ai__MaxOutputTokens=400
```

Recommended low-cost baseline:

- Azure deployment type: `Global Standard`
- Azure model deployment: `gpt-4o-mini`
- `Ai__MaxOutputTokens=400`

`Ai__MaxOutputTokens` is optional. When unset, PTDoc now defaults to `400` and clamps configured values to `128..800` before calling Azure OpenAI.
`AzureOpenAIApiVersion` is also optional; when unset, PTDoc falls back to `2024-06-01`.

Set these values on **`PTDoc.Api`**. PTDoc builds the Azure chat-completions path itself, so `AzureOpenAIEndpoint` must stay the base resource URL only, not the full `/openai/deployments/.../chat/completions?...` request URL.

### Runtime Diagnostics Endpoint

Authenticated Admin/Owner users can inspect deployment parity and AI runtime mode:

```bash
curl -H "Authorization: Bearer <token>" http://localhost:5170/diagnostics/runtime
```

**Response highlights:**
```json
{
  "environmentName": "Production",
  "isDevelopment": false,
  "release": {
    "releaseId": "2026.04.20.1",
    "sourceSha": "abc123def456",
    "imageTag": "ptdoc-api:2026.04.20.1"
  },
  "aiRuntime": {
    "featureEnabled": true,
    "developerDiagnosticsEnabled": true,
    "startupValidationMode": "EagerAtStartup",
    "effectiveAzureOpenAiEndpoint": "https://ptdoc-ai.cognitiveservices.azure.com",
    "effectiveAzureOpenAiDeployment": "ptdoc-gpt-4o-mini",
    "effectiveAzureOpenAiApiVersion": "2025-01-01-preview",
    "configurationState": "Complete",
    "missingAzureOpenAiSettings": [],
    "runtimeHealthGate": "AuthenticatedSavedNoteAiRequestRequired"
  }
}
```

Use this endpoint to answer two operational questions:

1. **Parity:** Which release/build identifier is actually deployed?
2. **Runtime mode:** Is AI disabled, eagerly validated at startup, or deferred until the first authenticated AI request?
3. **Developer diagnostics:** Are the admin-only one-shot AI fault endpoints expected to exist in this environment?

> **Important:** `/health/live`, `/health/ready`, and `/diagnostics/runtime` do
> **not** prove Azure OpenAI provider execution. The Azure provider path is still
> exercised lazily by the first authenticated AI request. For AI-enabled
> environments, the operational readiness gate is one authenticated saved-note AI
> request, preferably a real saved-note Plan generation flow.
>
> If `PTDoc.Web` is what you have exposed via devtunnel or reverse proxy, remember
> that the Azure AI environment variables still belong on `PTDoc.Api`, and direct
> `/diagnostics/runtime` or `/api/v1/ai/*` troubleshooting needs to target the API
> host or its logs rather than the web tunnel.

`PTDoc.Web` now exposes its own admin-only `GET /diagnostics/runtime` surface too.
Use the web-host version to confirm the effective upstream API base address resolved
from `ReverseProxy:Clusters:apiCluster:Destinations:api:Address`, and use the API-host
version to confirm the effective Azure endpoint, deployment, and API version. The two
diagnostics surfaces are complementary: the web host tells you which API instance it is
pointing at, and the API host tells you which Azure target it is trying to call.

### Developer AI Fault Injection

When `aiRuntime.developerDiagnosticsEnabled` is `true`, `PTDoc.Api` also exposes an
admin-only, developer-only diagnostics surface for deterministic AI failure testing:

- `GET /diagnostics/ai-faults`
- `PUT /diagnostics/ai-faults`
- `DELETE /diagnostics/ai-faults`

Faults are intentionally narrow:

- one-shot only
- in-memory only
- cleared on first matching request
- cleared on API restart
- scoped by `mode + noteId + targetUserId`

Supported modes:

- `plan_generation_failure`
- `clinical_summary_accept_failure`

If `targetUserId` is omitted when arming or clearing a fault, the API defaults it to
the current admin/owner caller. This is useful for same-user verification and still
allows explicit PT-targeting in shared troubleshooting environments.

Example arm request:

```bash
curl -X PUT \
  -H "Authorization: Bearer <admin-token>" \
  -H "Content-Type: application/json" \
  http://localhost:5170/diagnostics/ai-faults \
  -d '{
    "mode": "plan_generation_failure",
    "noteId": "11111111-1111-1111-1111-111111111111",
    "targetUserId": "00000000-0000-0000-0001-000000000001"
  }'
```

Example clear request:

```bash
curl -X DELETE \
  -H "Authorization: Bearer <admin-token>" \
  "http://localhost:5170/diagnostics/ai-faults?mode=plan_generation_failure&noteId=11111111-1111-1111-1111-111111111111&targetUserId=00000000-0000-0000-0001-000000000001"
```

### Residual Plan-AI Verification Runbook

Use this sequence when verifying the remaining Plan AI edge cases without breaking real
Azure configuration:

1. Query `PTDoc.Web` `GET /diagnostics/runtime` and confirm the effective upstream API base address.
2. Query that exact `PTDoc.Api` host’s `GET /diagnostics/runtime` and confirm:
   - Azure config is complete
   - expected endpoint, deployment, and API version are reported
   - `developerDiagnosticsEnabled` is `true`
3. In the target clinician session, open a new unsaved note and confirm all three Plan AI buttons are disabled with the save-first reason.
4. Save the note, arm `plan_generation_failure` for that `noteId` and consuming user, and trigger one Plan AI action.
5. Verify a clinician-safe inline failure with `Reference ID`, no review banner, and unchanged manual text.
6. Allow the fault to auto-consume or clear it, then generate a clinical summary normally and confirm pending review appears.
7. Arm `clinical_summary_accept_failure` for the same `noteId` and consuming user, click `Accept`, and verify:
   - a visible safe accept-failure message,
   - no accepted-success note,
   - pending review still active,
   - AI draft still visible,
   - `Discard` restores the pre-review text.
8. Clear the fault and re-run a normal summary accept to confirm no lingering injected state.

### Detecting Migration Drift

If the readiness check or diagnostics shows `"migrationStatus": "PendingMigrations"`:

1. **Review pending migrations:**
   ```bash
   EF_PROVIDER=sqlite dotnet ef migrations list \
     -p src/PTDoc.Infrastructure.Migrations.Sqlite \
     -s src/PTDoc.Api
   ```

2. **Apply missing migrations (development):**
   ```bash
   EF_PROVIDER=sqlite dotnet ef database update \
     -p src/PTDoc.Infrastructure.Migrations.Sqlite \
     -s src/PTDoc.Api
   ```

3. **Production — run via CLI (AutoMigrate is false by default):**
   ```bash
   # Set appropriate env vars, then:
   dotnet ef database update \
     -p src/PTDoc.Infrastructure.Migrations.SqlServer \
     -s src/PTDoc.Api
   ```

4. **Check for unmigrated model changes:**
   ```bash
   EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes \
     -p src/PTDoc.Infrastructure.Migrations.Sqlite \
     -s src/PTDoc.Api
   ```
   Non-zero exit code means the EF Core model diverged from the last migration
   snapshot. Create a new migration to capture the change.

### Startup Logging

At startup the API logs the following at `Information` level (visible in
`Development`; may require adjusting log levels in production):

- `Database provider selected: Sqlite` (or `SqlServer` / `Postgres`)
- `Database auto-migrate: True (environment: Development)`
- `Applying 1 pending migration(s): 20260217034617_InitialCreate`
- `Database migrations applied successfully.`
- `No pending migrations — database schema is current.`

Set `Logging:LogLevel:Default` to `Information` or lower to see these messages
in production logs.

## Additional Resources

- [README.md](../README.md) - Getting started guide
- [ARCHITECTURE.md](ARCHITECTURE.md) - Technical architecture
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform considerations
- [ACCESSIBILITY_USAGE.md](ACCESSIBILITY_USAGE.md) - Accessibility guide
- [Blazor-Context.md](Blazor-Context.md) - Blazor best practices (includes MAUI Hybrid guidance)
