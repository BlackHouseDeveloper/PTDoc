# PTDoc Troubleshooting Guide

## Quick Diagnostics

### Check System Requirements

```bash
# Verify .NET SDK version (must be 8.0.417 per global.json)
dotnet --version

# Check MAUI workloads (macOS only)
dotnet workload list | grep maui

# Verify Xcode (macOS only)
xcode-select -p
```

### Run Automated Setup

```bash
# The Foundry script validates environment and fixes common issues
./PTDoc-Foundry.sh
```

### View Detailed Build Logs

```bash
# Clean build with verbose logging
./cleanbuild-ptdoc.sh

# Or manually
dotnet build --verbosity detailed > build.log 2>&1
```

## Common Build Errors

### Error: "The SDK version '8.0.417' was not found"

**Cause:** Wrong .NET SDK version installed (global.json enforces 8.0.417)

**Solution:**
```bash
# Download and install .NET 8.0.417 SDK
# https://dotnet.microsoft.com/en-us/download/dotnet/8.0

# Verify installation
dotnet --version

# If multiple SDKs installed, remove global.json temporarily
# mv global.json global.json.bak
# dotnet --version
# mv global.json.bak global.json
```

### Error: "The workload 'maui' is not installed"

**Cause:** .NET MAUI workload not installed

**Solution:**
```bash
# Install MAUI workload
dotnet workload install maui

# If that fails, restore workloads
dotnet workload restore

# Update workloads
dotnet workload update
```

### Error: "No DbContext was found in PTDoc.Infrastructure"

**Cause:** EF Core tools cannot find DbContext or provider is wrong

**Solution:**
```bash
# Ensure using SQLite provider
EF_PROVIDER=sqlite dotnet ef dbcontext info \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api

# If still failing, rebuild Infrastructure project
dotnet build src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj
```

### Error: "Cannot find a MSBuild project in the directory"

**Cause:** Running dotnet commands from wrong directory

**Solution:**
```bash
# Navigate to solution root
cd /path/to/PTDoc

# Verify you're in correct directory
ls PTDoc.sln

# Run commands with full paths
dotnet build PTDoc.sln
```

### Error: "Project has dependency cycle"

**Cause:** Violating Clean Architecture dependency rules

**Solution:**
```bash
# Check for circular references
./cleanbuild-ptdoc.sh

# Review dependency rules:
# Core → (no dependencies)
# Application → Core
# Infrastructure → Application
# Api/Web/Maui → Infrastructure

# Remove incorrect project references
dotnet remove src/PTDoc.Application reference src/PTDoc.Infrastructure
```

## Runtime Errors

### Error: "Unable to resolve service for type 'ITokenStore'"

**Cause:** Service not registered in DI container

**Solution:**
```csharp
// In Program.cs, ensure service is registered
// MAUI:
builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();

// Web:
builder.Services.AddScoped<ITokenStore, CookieTokenStore>();

// API:
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
```

### Error: "JWT signing key has not been configured"

**Cause:** Using placeholder JWT key in appsettings.json

**Solution:**
```bash
# Generate secure key
openssl rand -base64 64

# Update appsettings.Development.json
{
  "Jwt": {
    "SigningKey": "<paste-generated-key-here>"
  }
}
```

### Error: "Cannot write to parameter property"

**Cause:** Blazor component mutating `[Parameter]` property after initialization

**Solution:**
```csharp
// Bad: Writing to parameter
[Parameter] public bool IsExpanded { get; set; }

private void Toggle()
{
    IsExpanded = !IsExpanded; // ❌ Don't do this
}

// Good: Use internal state
[Parameter] public bool InitiallyExpanded { get; set; }
private bool isExpanded;

protected override void OnInitialized()
{
    isExpanded = InitiallyExpanded; // Copy to internal state
}

private void Toggle()
{
    isExpanded = !isExpanded; // ✅ Modify internal state
}
```

### Error: "Component not rendering" or "blank page"

**Cause:** Multiple possible issues

**Solution:**
```bash
# Check browser console for JS errors
# Open DevTools (F12) → Console tab

# Verify component naming (must be PascalCase)
# ✅ PTDocMetricCard.razor
# ❌ ptdocmetriccard.razor

# Check _Imports.razor has component namespace
# @using PTDoc.UI.Components

# Ensure loading state is shown during async operations
@if (isLoading)
{
    <p>Loading...</p>
}
else if (data != null)
{
    <!-- Render data -->
}
```

## Database Issues

### Error: "SQLite Error 1: 'no such table: Patients'"

**Cause:** Migrations not applied to database

**Solution:**
```bash
# Apply all migrations
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api

# Or use Foundry script
./PTDoc-Foundry.sh --create-migration
```

### Error: "database is locked"

**Cause:** Multiple processes accessing SQLite database

**Solution:**
```bash
# Find process using database
lsof dev.PTDoc.db

# Kill stuck process
kill -9 <PID>

# For MAUI, delete database and recreate
rm ~/Library/Containers/com.ptdoc.app/Data/ptdoc.db
```

### Error: "The entity type 'Patient' requires a primary key"

**Cause:** Entity missing `[Key]` attribute or Id property

**Solution:**
```csharp
// Add primary key
public class Patient
{
    public int Id { get; set; } // ✅ Convention-based
    
    // Or explicit:
    [Key]
    public int PatientId { get; set; }
}
```

## Authentication Issues

### Error: "401 Unauthorized" on API calls

**Cause:** Missing or invalid JWT token

**Solution:**
```bash
# Test token endpoint
curl -X POST http://localhost:5170/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","pin":"1234"}'

# Use token in subsequent requests
curl http://localhost:5170/api/patients \
  -H "Authorization: Bearer <token-here>"
```

### Error: "Token expired" after 15 minutes

**Cause:** Expected behavior - access tokens expire per HIPAA policy

**Solution:**
```bash
# Use refresh token endpoint
curl -X POST http://localhost:5170/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refresh-token-here>"}'

# MAUI apps should auto-refresh via AuthenticatedHttpMessageHandler
```

### Error: "Session expired" on web app

**Cause:** Cookie-based auth enforces 15-min inactivity + 8-hr absolute timeout

**Solution:**
```csharp
// Expected HIPAA behavior - user must re-login
// Adjust timeouts only if compliance allows:

// In PTDoc.Web/Program.cs:
.AddCookie("PTDocAuth", options =>
{
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15); // Inactivity
    options.Cookie.MaxAge = TimeSpan.FromHours(8);     // Absolute
});
```

## Platform-Specific Issues

### iOS Simulator

**Issue:** "Unable to connect to API at http://localhost:5170"

**Solution:**
```bash
# Verify API is running
curl http://localhost:5170/health

# iOS simulator uses localhost (not 10.0.2.2)
# Check MauiProgram.cs:
#if ANDROID
    var apiBaseUrl = "http://10.0.2.2:5170";
#else
    var apiBaseUrl = "http://localhost:5170"; // iOS uses localhost
#endif
```

**Issue:** "Code signing required for product type 'Application'"

**Solution:**
```bash
# Set development team in Xcode
# Open PTDoc.csproj in Xcode
# Signing & Capabilities → Team → Select your Apple ID

# Or build with automatic signing
dotnet build -f net8.0-ios \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:CodesignProvision=Automatic
```

### Android Emulator

**Issue:** "Unable to connect to API at http://localhost:5170"

**Solution:**
```bash
# Android emulator uses 10.0.2.2 for host machine localhost
# Verify MauiProgram.cs:
#if ANDROID
    var apiBaseUrl = "http://10.0.2.2:5170"; // Correct for Android
#endif

# Test connectivity from emulator
adb shell ping -c 3 10.0.2.2

# Verify API is accessible
curl http://10.0.2.2:5170/health
```

**Issue:** "Installation failed with error: INSTALL_FAILED_UPDATE_INCOMPATIBLE"

**Solution:**
```bash
# Uninstall existing app
adb uninstall com.ptdoc.app

# Rebuild and reinstall
dotnet build -t:Run -f net8.0-android src/PTDoc.Maui/PTDoc.csproj
```

### macOS (Mac Catalyst)

**Issue:** "The application cannot be opened"

**Solution:**
```bash
# Clear derived data
rm -rf ~/Library/Developer/Xcode/DerivedData

# Rebuild
dotnet build -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj

# If codesigning issues, remove app bundle
rm -rf src/PTDoc.Maui/bin/Debug/net8.0-maccatalyst/maccatalyst-arm64/PTDoc.app
```

### Blazor Web

**Issue:** "Blazor disconnected" or "Reconnecting to server"

**Cause:** SignalR connection lost (Blazor Server only)

**Solution:**
```csharp
// In PTDoc.Web/Components/App.razor, ensure:
<script src="_framework/blazor.web.js"></script>

// Check browser console for errors
// Common causes:
// - API not running
// - CORS misconfigured
// - Network proxy blocking WebSocket
```

## Performance Issues

### Slow API Response Times

**Diagnosis:**
```bash
# Check database query performance
export Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Information
dotnet run --project src/PTDoc.Api

# Review logs for N+1 queries
# Look for multiple SELECT statements in rapid succession
```

**Solution:**
```csharp
// Use Include for eager loading
var patients = await _context.Patients
    .Include(p => p.Appointments)
    .ToListAsync();

// Use AsNoTracking for read-only queries
var patients = await _context.Patients
    .AsNoTracking()
    .ToListAsync();

// Add indexes to frequently queried columns
modelBuilder.Entity<Patient>()
    .HasIndex(p => p.LastName);
```

### Slow Blazor Rendering

**Diagnosis:**
```csharp
// Enable detailed logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Debug);
});
```

**Solution:**
```csharp
// Use @key directive for list rendering
@foreach (var patient in patients)
{
    <PatientCard @key="patient.Id" Patient="@patient" />
}

// Virtualize large lists
<Virtualize Items="@patients" Context="patient">
    <PatientCard Patient="@patient" />
</Virtualize>

// Avoid unnecessary StateHasChanged calls
// Blazor already re-renders on events
```

## MAUI-Specific Issues

### Issue: "SecureStorage not available"

**Cause:** Platform not initialized before accessing SecureStorage

**Solution:**
```csharp
// Ensure platform is initialized in MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    // ... configuration
    
    // Platform initialization happens automatically
    return builder.Build();
}

// In services, check availability
if (SecureStorage.Default != null)
{
    await SecureStorage.SetAsync("token", token);
}
```

### Issue: "FileNotFoundException: appsettings.json"

**Cause:** Configuration file not included in MAUI project

**Solution:**
```xml
<!-- In PTDoc.Maui/PTDoc.csproj -->
<ItemGroup>
  <MauiAsset Include="appsettings.json" />
  <MauiAsset Include="appsettings.Development.json" />
</ItemGroup>
```

## Developer Mode Issues

### Issue: "Diagnostics not showing"

**Cause:** Developer mode disabled

**Solution:**
```bash
# Enable via environment variable (highest priority)
export PTDOC_DEVELOPER_MODE=true

# Or via configuration file
# appsettings.Development.json:
{
  "App": {
    "DeveloperMode": true
  }
}

# Verify setting is read
dotnet run --project src/PTDoc.Web
# Check startup logs for "Developer mode: enabled"
```

See [developer-mode.md](developer-mode.md) for precedence order.

## Clean Architecture Violations

### Error: "Cannot reference Infrastructure from Application"

**Cause:** Violating dependency rules

**Solution:**
```bash
# Remove incorrect reference
cd src/PTDoc.Application
dotnet remove reference ../PTDoc.Infrastructure/PTDoc.Infrastructure.csproj

# Correct dependency flow:
# Core ← Application ← Infrastructure ← (Api, Web, Maui)
```

### Error: "Ambiguous reference between Application and Infrastructure interfaces"

**Cause:** Interface defined in multiple projects

**Solution:**
```csharp
// Keep interfaces in Application layer only
// Implementations in Infrastructure layer

// ✅ src/PTDoc.Application/Services/IPatientService.cs
public interface IPatientService { }

// ✅ src/PTDoc.Infrastructure/Services/PatientService.cs
public class PatientService : IPatientService { }
```

## Getting More Help

### Enable Verbose Logging

```bash
# API
export ASPNETCORE_ENVIRONMENT=Development
export Logging__LogLevel__Default=Debug
dotnet run --project src/PTDoc.Api --verbosity detailed

# MAUI
dotnet build -t:Run -f net8.0-ios -v detailed
```

### Collect Diagnostic Information

```bash
# System info
dotnet --info

# List installed SDKs
dotnet --list-sdks

# List MAUI workloads
dotnet workload list

# Check NuGet packages
dotnet list PTDoc.sln package

# Generate build log
dotnet build --verbosity diagnostic > build-diagnostic.log 2>&1
```

### Check Related Documentation

- [BUILD.md](BUILD.md) - Build instructions and common errors
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migration issues
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform-specific notes
- [Blazor-Context.md](Blazor-Context.md) - Blazor troubleshooting
- [ACCESSIBILITY_USAGE.md](ACCESSIBILITY_USAGE.md) - Accessibility testing

### Report Issues

If none of the above resolves your issue:

1. **Search existing issues:** https://github.com/BlackHouseDeveloper/PTDoc/issues
2. **Create new issue with:**
   - Platform (macOS/Windows/Linux)
   - .NET SDK version (`dotnet --version`)
   - Target framework (iOS/Android/Web/API)
   - Full error message and stack trace
   - Steps to reproduce
   - Diagnostic log output
