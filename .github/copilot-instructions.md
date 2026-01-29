# PTDoc - AI Coding Assistant Instructions

## Project Overview

**PTDoc** is an enterprise healthcare documentation platform for physical therapy practices. It uses Clean Architecture with .NET 8, featuring multi-platform Blazor UI (Web, MAUI for iOS/Android/macOS), JWT authentication, and SQLite for local data persistence. The solution is healthcare-focused with HIPAA compliance considerations.

## Architecture & Project Structure

### Clean Architecture Layers

```
PTDoc.Core         → Domain entities (no dependencies)
PTDoc.Application  → Application interfaces and contracts (depends on Core)
PTDoc.Infrastructure → EF Core, services, PDF generation (depends on Application)
PTDoc.Api          → REST API with JWT auth (depends on Application, Infrastructure)
PTDoc.Web          → Blazor Server/WASM web app
PTDoc.Maui         → .NET MAUI Blazor mobile/desktop app
PTDoc.UI           → Shared Blazor Razor Class Library (components)
```

### Dependency Rules

- **Core** has zero dependencies - pure domain models
- **Application** defines interfaces (like `ICredentialValidator`, `ITokenService`) - no implementation details
- **Infrastructure** implements Application interfaces (like `CredentialValidator`, `TokenService`)
- **Api/Web/Maui** are presentation layers - they wire up DI and reference Infrastructure
- Never reference Infrastructure from Application or Core

## Critical Development Workflows

### Initial Environment Setup

```bash
# Required: .NET 8.0.417 SDK (see global.json)
dotnet --version  # Must show 8.x

# For MAUI development (iOS/macOS):
sudo xcode-select --switch /Applications/Xcode.app
dotnet workload install maui

# Automated setup (runs all checks, restores packages)
./PTDoc-Foundry.sh

# Setup with database migration
./PTDoc-Foundry.sh --create-migration --seed
```

### Helper Scripts

- **PTDoc-Foundry.sh** - Comprehensive development environment setup
- **run-ptdoc.sh** - Interactive launcher for all platforms
- **cleanbuild-ptdoc.sh** - Clean build with detailed logging

### Running the Application

**API Server** (Development):
```bash
dotnet run --project src/PTDoc.Api --urls http://localhost:5170
```

**Web Application**:
```bash
dotnet run --project src/PTDoc.Web
```

**MAUI Desktop (Mac Catalyst)**:
```bash
dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

**MAUI Mobile**:
```bash
# iOS simulator
dotnet build -t:Run -f net8.0-ios src/PTDoc.Maui/PTDoc.csproj

# Android emulator (API uses 10.0.2.2 for host machine)
dotnet build -t:Run -f net8.0-android src/PTDoc.Maui/PTDoc.csproj
```

### Database Operations

**Automated (Recommended):**
```bash
# Create initial migration and update database
./PTDoc-Foundry.sh --create-migration

# Seed development data
./PTDoc-Foundry.sh --seed
```

**Manual EF Core Commands:**
```bash
# Create migration
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  --project src/PTDoc.Infrastructure \
  --startup-project src/PTDoc.Api

# Apply migrations
EF_PROVIDER=sqlite dotnet ef database update \
  --project src/PTDoc.Infrastructure \
  --startup-project src/PTDoc.Api
```

**Database Path Resolution Order:**
1. `PFP_DB_PATH` environment variable
2. `ConnectionStrings:DefaultConnection` in appsettings.{Environment}.json
3. Fallback: `Data Source=PTDoc.db`

See [docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md) for complete EF Core documentation.

## Project-Specific Conventions

### Authentication & Security

**JWT Token Flow**:
- API endpoints: `/auth/token` (login), `/auth/refresh` (refresh token)
- Access tokens: 15 min lifetime
- Refresh tokens: 30 days lifetime (stored in `InMemoryRefreshTokenStore`)
- Required JWT signing key: minimum 32 characters (validated on startup)

**Multi-Platform Auth Patterns**:
```csharp
// MAUI: Uses SecureStorageTokenStore for tokens
services.AddScoped<ITokenStore, SecureStorageTokenStore>();

// Web: Cookie-based auth with sliding expiration
.AddCookie("PTDocAuth", options => {
    options.ExpireTimeSpan = TimeSpan.FromMinutes(15);  // Inactivity timeout
    options.Cookie.MaxAge = TimeSpan.FromHours(8);      // HIPAA absolute limit
});

// API: JWT Bearer tokens
.AddJwtBearer(options => { ... });
```

**Platform-Specific API Base URLs**:
- Android emulator: `http://10.0.2.2:5170` (host machine localhost)
- iOS simulator: `http://localhost:5170`
- Override with `PTDoc_API_BASE_URL` environment variable

### Blazor Component Standards

**Critical Rules** (see [docs/Blazor-Context.md](../docs/Blazor-Context.md) for detailed guidance):

1. **Component Naming**: Always use PascalCase (e.g., `PTDocMetricCard.razor`), never lowercase
2. **Parameters**: 
   - Mark with `[Parameter]` attribute
   - **Never write to parameter properties after initialization** - treat as read-only
   - Use `[EditorRequired]` for mandatory parameters
   - For two-way binding: use `[Parameter] T Value` + `[Parameter] EventCallback<T> ValueChanged`
3. **Wrapper Components**: Must define `[Parameter] RenderFragment? ChildContent` and render `@ChildContent`
4. **Namespace Registration**: Update `_Imports.razor` when adding components to new namespaces
5. **Async Data Loading**:
   ```csharp
   @if (isLoading) {
       <p>Loading...</p>
   } else if (data != null) {
       <!-- Render data -->
   }
   ```
   Always show loading state - never leave components blank during async operations

6. **Lifecycle Methods**:
   - Use `OnInitializedAsync` for data fetching
   - Use `OnAfterRenderAsync(bool firstRender)` for JS interop (check `firstRender`)
   - Avoid calling `StateHasChanged()` unless responding to external events (timers, non-Blazor callbacks)

7. **Prerendering Considerations**:
   - Components may run twice (server prerender + client interactive)
   - `OnAfterRender` only runs after interactive connection established
   - Use `[StreamRendering]` for slow-loading dashboard components

### Configuration Patterns

**appsettings.json Structure**:
```json
{
  "Jwt": {
    "Issuer": "PTDoc.Api",
    "Audience": "PTDoc",
    "SigningKey": "<min-32-chars>",  // NEVER use placeholders in production
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 30
  }
}
```

**Environment-Specific Settings**:
- `appsettings.json` - shared defaults (committed to repo)
- `appsettings.Development.json` - local dev overrides (committed)
- `appsettings.Production.json` - production settings (add to .gitignore if contains secrets)

## Healthcare-Specific Considerations

- **HIPAA Compliance**: All changes involving patient data must maintain audit trail capabilities
- **Session Timeouts**: Web app enforces 15-min inactivity + 8-hour absolute timeout
- **Data Security**: Cookie settings use `HttpOnly=true`, `SecurePolicy=Always`, `SameSite=Strict`
- **Clinical Terminology**: Use standardized medical terminology when implementing clinical features
- **Accessibility**: Components must support keyboard navigation and screen readers (WCAG 2.1 AA)

## Common Pitfalls to Avoid

### Dependency Injection Lifetimes
- **Scoped** services in Web (Blazor Server): one instance per circuit/user session
- **Singleton** services: safe in WASM/MAUI (single user), but dangerous in Blazor Server (shared across users)
- For user-specific state, use Scoped or CascadingParameters, never static fields

### Project References
- Always reference through interfaces: `ICredentialValidator` not `CredentialValidator`
- Infrastructure project should only be referenced by API/Web/Maui Program.cs for DI registration
- UI components in PTDoc.UI should only depend on PTDoc.Application (interfaces)

### Blazor Component Visibility Issues
Common causes of "invisible" components:
1. Forgot to update `_Imports.razor` with namespace
2. Component name starts with lowercase letter
3. Loading data without showing loading indicator
4. Required parameter not provided
5. Conditional rendering logic always false
6. Missing `@ChildContent` in wrapper components

## Testing & Validation

```bash
# Run all tests
dotnet test

# Build specific platform
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-ios
dotnet build src/PTDoc.Web/PTDoc.Web.csproj

# Check for errors
dotnet build --no-incremental
```

## Key Files & Patterns

- [MauiProgram.cs](../src/PTDoc.Maui/MauiProgram.cs) - Platform-specific API URL configuration, DI setup for MAUI
- [PTDoc.Web/Program.cs](../src/PTDoc.Web/Program.cs) - Cookie auth, reverse proxy config
- [PTDoc.Api/Program.cs](../src/PTDoc.Api/Program.cs) - JWT validation, auth endpoint mapping
- [docs/Blazor-Context.md](../docs/Blazor-Context.md) - Comprehensive Blazor lifecycle & pitfall guide
- [docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md) - Entity Framework migrations and database setup
- [docs/RUNTIME_TARGETS.md](../docs/RUNTIME_TARGETS.md) - Web vs device platform differences
- [docs/ACCESSIBILITY_USAGE.md](../docs/ACCESSIBILITY_USAGE.md) - WCAG 2.1 AA compliance guide
- [docs/developer-mode.md](../docs/developer-mode.md) - Developer diagnostics configuration
- [global.json](../global.json) - Enforces .NET SDK 8.0.417

## File Organization Standards

- Routable pages: `Pages/` folder
- Reusable components: `Components/` or `Shared/` folder  
- Auth implementations: `Auth/` folder in each project layer
- Models/entities: `Models/` (Core), `DTOs/` (API contracts)
- Services: `Services/` folder per layer
- Tests: Separate test project (e.g., `PTDoc.Tests`)

## Quick Reference

**Build all projects**: `dotnet build PTDoc.sln`  
**Restore packages**: `dotnet restore`  
**Clean build**: `./cleanbuild-ptdoc.sh`  
**Launch platform**: `./run-ptdoc.sh`  
**Setup environment**: `./PTDoc-Foundry.sh --help`  
**Format code**: Follow .editorconfig conventions (auto-formatting available via IDE)

---

**Last Updated**: January 2026  
**Primary Development OS**: macOS (Apple Silicon/Intel)  
**Target Framework**: .NET 8.0  
**Healthcare Compliance**: HIPAA considerations required for all data handling  
**Key Documentation**: See `docs/` folder for comprehensive guides
