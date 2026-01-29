# Developer Diagnostics Mode

PTDoc includes developer diagnostics capabilities that can be surfaced across Blazor Web and .NET MAUI platforms. To keep production deployments secure by default, diagnostics are hidden unless explicitly enabled.

## Precedence Order

When diagnostic components initialize, they apply the first matching configuration source in this order:

1. **`PTDOC_DEVELOPER_MODE` environment variable** (`true`/`false`, case-insensitive)
2. **`App:DeveloperMode` setting** from active configuration (e.g., `appsettings.json`)
3. **Build-default fallback** (`true` for Debug builds, `false` for Release builds)

This allows production operators to temporarily enable diagnostics without redeploying.

## Platform-Specific Configuration

### .NET MAUI (Mobile/Desktop)

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

### MAUI Android

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

### MAUI iOS

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

### Blazor WebAssembly

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

## Configuration Examples

### appsettings.json Structure

```json
{
  "App": {
    "DeveloperMode": false,
    "DiagnosticsRequiredRole": "Operator"
  }
}
```

### appsettings.Development.json (Local Development)

```json
{
  "App": {
    "DeveloperMode": true
  }
}
```

## Security Considerations

### Production Deployments

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

### Healthcare Compliance

For HIPAA-compliant deployments:
- Limit diagnostic output to exclude PHI
- Log all diagnostic mode activations
- Require authentication to view diagnostics
- Auto-disable after time period
- Review diagnostic logs during security audits

## Diagnostic Features

When enabled, PTDoc may show:
- Performance metrics and timings
- API request/response inspection
- Authentication state details
- Cache hit/miss ratios
- Database query statistics
- Build and version information

## Troubleshooting

### Diagnostics Not Showing

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

### Diagnostics Enabled in Production

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

## Implementation Example

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

## Related Documentation

- [SECURITY.md](SECURITY.md) - Security best practices
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Common issues
