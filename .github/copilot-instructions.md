# PTDoc - AI Coding Assistant Instructions

## Canonical Context Source

**PRIMARY REFERENCE:** Always consult `docs/context/ptdoc-figma-make-prototype-v5-context.md` FIRST for all PTDoc v5 implementation decisions. This consolidated document is the single source of truth for:
- Design system (colors, typography, components)
- UI architecture and page specifications
- Component catalog with props and variants
- Data models and API contracts
- UX rules and accessibility requirements
- React→Blazor conversion patterns

**Figma Design Reference:** https://www.figma.com/make/1Fd3pzaGzvHboxFKuCz4dY/PTDoc-Prototype-v5?p=f&t=s9McithEAB55SH6O-0
- When implementing or reviewing **.tsx** files, use **Figma Desktop → Map / Pages / Layers navigation** to locate the corresponding design screen/component
- This provides authoritative visual context for conversion fidelity
- If design appears to conflict with consolidated context doc, surface the conflict as a question rather than assuming

**Citation Requirements:**
When generating code or making implementation decisions for PTDoc v5:
1. **Cite your sources** - Reference which section(s) of the consolidated context doc you followed (e.g., "Per Section 6.3 PTDocButton specification...")
2. **Acknowledge gaps** - If the consolidated doc doesn't cover a specific scenario, explicitly state "Not found in consolidated doc, applying [fallback approach]"
3. **Surface conflicts** - If Figma design contradicts the consolidated doc, or if different sections appear inconsistent, ask for clarification rather than making assumptions
4. **Trace decisions** - In complex implementations, show your reasoning path through the doc hierarchy (e.g., "Section 7 design tokens → Section 6 component catalog → Section 11 implementation notes")

This ensures all AI-generated code is traceable to authoritative sources and makes it easier to identify when the consolidated doc needs updates.

**Design System Implementation:**
PTDoc uses a token-based design system with CSS custom properties:
- **Tokens**: `src/PTDoc.UI/wwwroot/css/tokens.css` - All semantic design tokens (colors, spacing, typography, shadows, etc.)
- **Global styles**: `src/PTDoc.UI/wwwroot/css/app.css` - Base styles and resets
- **Theme support**: Light/Dark mode via `:root` and `.dark` selectors
- **Primary color**: Emerald Green (#16a34a light, #22c55e dark) - healthcare-friendly, accessible
- **Typography**: Inter font family across all platforms
- **Always use tokens**: `var(--primary)` not `#16a34a`, `var(--spacing-4)` not `1rem`
- **Semantic naming**: Use role-based tokens (`--primary`, `--muted`) never hue-based (`--blue-dark`)

See [docs/style-system.md](../docs/style-system.md) and [docs/design-system/THEME_VISUAL_GUIDE.md](../docs/design-system/THEME_VISUAL_GUIDE.md) for complete design token reference.

**Archive Policy:**
Archived documents under `docs/_archive/` MUST NOT be used as authoritative sources. They exist only for historical reference and must never override the consolidated context. If Copilot encounters conflicting information between active docs and archived docs, always defer to active documentation and surface the conflict.

## Project Overview

**PTDoc** is an enterprise healthcare documentation platform for physical therapy practices. It uses Clean Architecture with .NET 8, featuring multi-platform Blazor UI (Web, MAUI for iOS/Android/macOS), JWT authentication, and SQLite for local data persistence. The solution is healthcare-focused with HIPAA compliance considerations.

**Current Implementation Status** (February 2026):
- **Foundation Complete**: Clean Architecture, authentication, database infrastructure
- **Active Work**: Converting React/TypeScript prototype (PTDoc v5) to Blazor components
- **Design System**: Token-based CSS custom properties (emerald green theme, Inter typography)
- **Target Branch Pattern**: Feature branches for UI implementation (e.g., `UI-Implementation-Dashboard-Home-("/")`)
- **Next Phase**: Dashboard components, patient management UI, clinical documentation forms

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

8. **Styling Components**:
   - Use CSS custom properties from `tokens.css`: `var(--primary)`, `var(--spacing-4)`, etc.
   - Never hardcode colors, spacing, or typography values
   - Support light/dark themes via token switching
   - Add component-specific styles in `/wwwroot/css/components/` if needed
   - Follow mobile-first responsive design (breakpoints in tokens)

### API Contracts & DTOs

**DTO Naming Conventions**:
- Request DTOs: `{Entity}{Action}Request.cs` (e.g., `CreatePatientRequest.cs`)
- Response DTOs: `{Entity}Response.cs` or `{Entity}DetailResponse.cs`
- List responses: `{Entity}ListResponse.cs` with pagination metadata

**DTO Location**:
- **PTDoc.Application**: Interface-level DTOs (contracts)
- **PTDoc.Api**: API-specific request/response models
- **Never**: DTOs in PTDoc.Core (domain entities only)

**Example API Contract**:
```csharp
// PTDoc.Application/DTOs/Patients/CreatePatientRequest.cs
public record CreatePatientRequest
{
    [Required]
    [StringLength(100)]
    public string FirstName { get; init; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string LastName { get; init; } = string.Empty;
    
    [Required]
    public DateOnly DateOfBirth { get; init; }
    
    [Phone]
    public string? PhoneNumber { get; init; }
    
    [EmailAddress]
    public string? Email { get; init; }
}

// PTDoc.Application/DTOs/Patients/PatientResponse.cs
public record PatientResponse
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public int Age { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

// PTDoc.Application/DTOs/Common/PagedResponse.cs
public record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

**API Endpoint Patterns**:
```csharp
// PTDoc.Api/Endpoints/PatientEndpoints.cs
public static class PatientEndpoints
{
    public static void MapPatientEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/patients")
            .RequireAuthorization()
            .WithTags("Patients");
        
        // GET /api/patients
        group.MapGet("/", async (
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            IPatientService service) =>
        {
            var result = await service.GetPatientsAsync(page, pageSize);
            return Results.Ok(result);
        }).WithName("GetPatients");
        
        // GET /api/patients/{id}
        group.MapGet("/{id:int}", async (
            int id,
            IPatientService service) =>
        {
            var patient = await service.GetPatientAsync(id);
            return patient is not null 
                ? Results.Ok(patient) 
                : Results.NotFound();
        }).WithName("GetPatient");
        
        // POST /api/patients
        group.MapPost("/", async (
            CreatePatientRequest request,
            IPatientService service) =>
        {
            var patient = await service.CreatePatientAsync(request);
            return Results.CreatedAtRoute("GetPatient", 
                new { id = patient.Id }, patient);
        }).WithName("CreatePatient");
        
        // PUT /api/patients/{id}
        group.MapPut("/{id:int}", async (
            int id,
            UpdatePatientRequest request,
            IPatientService service) =>
        {
            var updated = await service.UpdatePatientAsync(id, request);
            return updated ? Results.NoContent() : Results.NotFound();
        }).WithName("UpdatePatient");
    }
}
```

**Error Response Standard**:
```csharp
// PTDoc.Application/DTOs/Common/ErrorResponse.cs
public record ErrorResponse
{
    public string Error { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, string[]>? ValidationErrors { get; init; }
    public string? TraceId { get; init; }
}
```

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

### Testing Patterns

**Unit Testing Blazor Components** (using bUnit):
```csharp
// PTDoc.Tests/Components/PTDocButtonTests.cs
using Bunit;
using PTDoc.UI.Components;

public class PTDocButtonTests : TestContext
{
    [Fact]
    public void Button_RendersWithCorrectText()
    {
        // Arrange
        var cut = RenderComponent<PTDocButton>(parameters => parameters
            .Add(p => p.Text, "Save Patient"));
        
        // Assert
        cut.Find("button").TextContent.Should().Contain("Save Patient");
    }
    
    [Fact]
    public void Button_CallsOnClickCallback()
    {
        // Arrange
        var clicked = false;
        var cut = RenderComponent<PTDocButton>(parameters => parameters
            .Add(p => p.OnClick, () => clicked = true));
        
        // Act
        cut.Find("button").Click();
        
        // Assert
        clicked.Should().BeTrue();
    }
}
```

**Service Layer Testing** (mocking interfaces):
```csharp
// PTDoc.Tests/Services/PatientServiceTests.cs
using Moq;
using PTDoc.Application.Interfaces;

public class PatientServiceTests
{
    [Fact]
    public async Task GetPatient_ReturnsPatient_WhenExists()
    {
        // Arrange
        var mockRepo = new Mock<IPatientRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(123))
            .ReturnsAsync(new Patient { Id = 123, Name = "John Doe" });
        
        var service = new PatientService(mockRepo.Object);
        
        // Act
        var result = await service.GetPatientAsync(123);
        
        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("John Doe");
    }
}
```

**Integration Testing (API)**:
```csharp
// PTDoc.Tests/Integration/PatientApiTests.cs
using Microsoft.AspNetCore.Mvc.Testing;

public class PatientApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    
    public PatientApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task GetPatients_ReturnsOk_WithValidToken()
    {
        // Arrange
        _client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", "valid_token");
        
        // Act
        var response = await _client.GetAsync("/api/patients");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

**Test Organization**:
- `PTDoc.Tests/` - Main test project
  - `Components/` - Blazor component tests (bUnit)
  - `Services/` - Service layer unit tests
  - `Integration/` - API integration tests
  - `Helpers/` - Test utilities and mocks

**Running Tests**:
```bash
# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

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
- [docs/Blazor-Context.md](../docs/Blazor-Context.md) - Comprehensive Blazor lifecycle, pitfall guide, and MAUI Hybrid architecture
- [docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md) - Entity Framework migrations and database setup
- [docs/RUNTIME_TARGETS.md](../docs/RUNTIME_TARGETS.md) - Web vs device platform differences
- [docs/ACCESSIBILITY_USAGE.md](../docs/ACCESSIBILITY_USAGE.md) - WCAG 2.1 AA compliance guide
- [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md) - Development workflows and developer diagnostics
- [global.json](../global.json) - Enforces .NET SDK 8.0.417

## File Organization Standards

### Component Organization

**PTDoc.UI (Shared Razor Class Library)**:
- **All reusable components MUST live here** - shared between Web and MAUI
- Structure:
  - `Components/` - Reusable UI components (buttons, cards, forms, etc.)
  - `Components/Layout/` - Layout components (navigation, headers, footers)
  - `Components/Auth/` - Authentication-related components
  - `Pages/` - Routable pages (use `@page` directive)
  - `wwwroot/css/` - Design tokens and global styles

**Platform-Specific Projects** (PTDoc.Web, PTDoc.Maui):
- `Pages/` - Platform-specific pages only (rare - prefer PTDoc.UI)
- `Components/` - Platform-specific components only when absolutely necessary
- `Auth/` - Platform-specific auth implementations (TokenStore, AuthenticationStateProvider)

**MAUI-Specific Styling Considerations**:
```csharp
// Check if running in MAUI context for mobile-specific styles
@inject IJSRuntime JS

@code {
    private bool _isMauiContext = false;
    
    protected override async Task OnInitializedAsync()
    {
        // Detect MAUI context via user agent or injected service
        try {
            _isMauiContext = await JS.InvokeAsync<bool>("eval", "window.hasOwnProperty('chrome')");
        } catch { }
    }
}

<!-- Apply mobile-optimized styling -->
<div class="@(_isMauiContext ? "touch-optimized" : "")">
    <!-- Component content -->
</div>
```

**Touch-Optimized CSS** (for MAUI):
```css
/* In component-specific CSS */
.touch-optimized {
    /* Larger touch targets */
    min-height: var(--touch-target-mobile); /* 44px */
    /* Disable text selection */
    user-select: none;
    -webkit-user-select: none;
    /* Remove tap highlight */
    -webkit-tap-highlight-color: transparent;
}
```

### Other Organization Rules

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

**Last Updated**: February 2026  
**Primary Development OS**: macOS (Apple Silicon/Intel)  
**Target Framework**: .NET 8.0  
**Healthcare Compliance**: HIPAA considerations required for all data handling  
**Key Documentation**: See `docs/` folder for comprehensive guides
