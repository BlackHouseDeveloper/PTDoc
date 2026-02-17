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

3. **Commit with conventional commits:**
   ```bash
   git add .
   git commit -m "feat(intake): add patient intake form component"
   ```

4. **Push and create PR:**
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

# Manual EF Core command
EF_PROVIDER=sqlite dotnet ef migrations add AddPatientNotes \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

#### Applying Migrations

```bash
# Apply all pending migrations
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api

# Apply to specific migration
EF_PROVIDER=sqlite dotnet ef database update AddPatientNotes \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

#### Rolling Back Migrations

```bash
# Remove last migration (if not applied)
EF_PROVIDER=sqlite dotnet ef migrations remove \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api

# Revert database to previous migration
EF_PROVIDER=sqlite dotnet ef database update PreviousMigrationName \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

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

### Offline-First Development Patterns

PTDoc implements offline-first architecture to ensure clinicians can work without connectivity.

#### Core Services

Two services manage offline-first capabilities:

**ISyncService - Data Synchronization**
```csharp
// src/PTDoc.Application/Services/ISyncService.cs
public interface ISyncService
{
    DateTime? LastSyncTime { get; }
    bool IsSyncing { get; }
    event Action? OnSyncStateChanged;
    
    Task InitializeAsync();
    Task<bool> SyncNowAsync();
    string GetElapsedTimeSinceSync();
}
```

**IConnectivityService - Network Monitoring**
```csharp
// src/PTDoc.Application/Services/IConnectivityService.cs
public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool>? OnConnectivityChanged;
    
    Task InitializeAsync();
    Task<bool> CheckConnectivityAsync();
}
```

#### Service Registration

Register in both Web and MAUI platforms:

```csharp
// PTDoc.Web/Program.cs or PTDoc.Maui/MauiProgram.cs
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<IConnectivityService, ConnectivityService>();
```

#### UI Integration Pattern

Components can reactively respond to sync and connectivity state:

```razor
@* Example: Offline-aware component *@
@inject ISyncService SyncService
@inject IConnectivityService ConnectivityService
@implements IDisposable

<div class="status-bar">
    <span>@(ConnectivityService.IsOnline ? "Online" : "Offline")</span>
    <span>Last sync: @SyncService.GetElapsedTimeSinceSync()</span>
    <button disabled="@(!ConnectivityService.IsOnline || SyncService.IsSyncing)"
            @onclick="HandleSync">
        @(SyncService.IsSyncing ? "Syncing..." : "Sync Now")
    </button>
</div>

@code {
    protected override void OnInitialized()
    {
        SyncService.OnSyncStateChanged += HandleStateChange;
        ConnectivityService.OnConnectivityChanged += HandleConnectivityChange;
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SyncService.InitializeAsync();
            await ConnectivityService.InitializeAsync();
            StateHasChanged();
        }
    }
    
    private async Task HandleSync()
    {
        if (ConnectivityService.IsOnline && !SyncService.IsSyncing)
        {
            await SyncService.SyncNowAsync();
        }
    }
    
    private void HandleStateChange() => InvokeAsync(StateHasChanged);
    private void HandleConnectivityChange(bool _) => InvokeAsync(StateHasChanged);
    
    public void Dispose()
    {
        SyncService.OnSyncStateChanged -= HandleStateChange;
        ConnectivityService.OnConnectivityChanged -= HandleConnectivityChange;
    }
}
```

#### Event-Driven State Management

Key patterns for reactive UI:

1. **Subscribe to events in OnInitialized()** - Safe during prerender
2. **Initialize services in OnAfterRenderAsync(firstRender)** - JSRuntime available
3. **Use InvokeAsync() for thread-safe state updates** - Events fire on background threads
4. **Always implement IDisposable** - Prevent memory leaks from event subscriptions
5. **Check service state before operations** - Don't sync when offline or already syncing

#### Testing Offline Scenarios

Simulate offline mode in browser DevTools:

```bash
# 1. Open DevTools (F12)
# 2. Network tab → Throttling → Offline
# 3. Verify:
#    - UI shows "Offline" badge
#    - Sync button is disabled
#    - Last sync time still updates
# 4. Go back online
#    - UI shows "Online" badge
#    - Sync button becomes enabled
```

#### Future Implementation Notes

Current implementation simulates sync operations. When implementing actual sync:

1. **Query local SQLite** for records with `SyncState = Pending`
2. **Push to API** with optimistic concurrency (ETag headers)
3. **Pull delta changes** using timestamp-based queries
4. **Update local database** and mark as `SyncState = Synced`
5. **Handle conflicts** per `docs/PTDocs+_Offline_Sync_Conflict_Resolution.md`

See `src/PTDoc.Infrastructure/Services/SyncService.cs` TODO comments for implementation details.

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

## Additional Resources

- [README.md](../README.md) - Getting started guide
- [ARCHITECTURE.md](ARCHITECTURE.md) - Technical architecture
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform considerations
- [ACCESSIBILITY_USAGE.md](ACCESSIBILITY_USAGE.md) - Accessibility guide
- [Blazor-Context.md](Blazor-Context.md) - Blazor best practices (includes MAUI Hybrid guidance)
