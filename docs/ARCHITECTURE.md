# PTDoc Architecture

## Overview

PTDoc is an enterprise healthcare documentation platform built using **Clean Architecture** principles. The system enables physical therapy practices to document patient care, manage appointments, and maintain HIPAA-compliant clinical records across multiple platforms (web, iOS, Android, macOS).

## Architectural Principles

### Clean Architecture

PTDoc follows Robert C. Martin's Clean Architecture with strict dependency rules:

```
┌─────────────────────────────────────────┐
│         Presentation Layer              │
│  (Api, Web, Maui, UI)                   │
└──────────────┬──────────────────────────┘
               │ depends on
┌──────────────▼──────────────────────────┐
│       Infrastructure Layer              │
│  (EF Core, Services, Auth)              │
└──────────────┬──────────────────────────┘
               │ implements
┌──────────────▼──────────────────────────┐
│       Application Layer                 │
│  (Interfaces, DTOs, Contracts)          │
└──────────────┬──────────────────────────┘
               │ uses
┌──────────────▼──────────────────────────┐
│          Core Layer                     │
│  (Domain Entities, Business Rules)      │
└─────────────────────────────────────────┘
```

**Dependency Rules:**
1. Core has **zero dependencies** - pure domain logic
2. Application depends **only on Core** - defines contracts
3. Infrastructure depends on **Application + Core** - implements contracts
4. Presentation depends on **Infrastructure + Application** - wires up DI

### Cross-Cutting Concerns

- **Logging:** Structured logging via ILogger<T>
- **Validation:** Data annotations + FluentValidation
- **Error Handling:** Centralized exception handling middleware
- **Security:** JWT authentication, role-based authorization
- **Auditing:** Database-level audit trails for PHI access

## Layer Details

### 1. Core Layer (PTDoc.Core)

**Purpose:** Domain entities and business logic with no external dependencies.

**Structure:**
```
PTDoc.Core/
├── Models/
│   ├── Patient.cs
│   ├── Appointment.cs
│   └── ClinicalNote.cs
└── (No interfaces - pure POCOs)
```

**Example Entity:**
```csharp
namespace PTDoc.Core.Models;

public class Patient
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    
    // Navigation properties
    public List<Appointment> Appointments { get; set; } = new();
    public List<ClinicalNote> ClinicalNotes { get; set; } = new();
}
```

**Key Characteristics:**
- No framework dependencies (no EF, no ASP.NET)
- No interfaces (Application layer defines those)
- Business logic only (validation rules, calculations)
- Serializable POCOs

### 2. Application Layer (PTDoc.Application)

**Purpose:** Define contracts and application workflows without implementation details.

**Structure:**
```
PTDoc.Application/
├── Auth/
│   ├── ICredentialValidator.cs
│   ├── ITokenService.cs
│   ├── ITokenStore.cs
│   └── IUserService.cs
├── Services/
│   ├── IPatientService.cs (future)
│   ├── ISyncService.cs
│   └── IConnectivityService.cs
└── DTOs/
    ├── LoginRequest.cs
    ├── TokenResponse.cs
    └── PatientDto.cs
```

**Example Interface:**
```csharp
namespace PTDoc.Application.Auth;

public interface ITokenService
{
    Task<TokenResponse> GenerateTokenAsync(string username);
    Task<TokenResponse?> RefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string refreshToken);
}
```

**Offline-First Services:**

PTDoc implements offline-first architecture through two core services:

```csharp
namespace PTDoc.Application.Services;

/// <summary>
/// Service for managing data synchronization state and operations
/// Supports offline-first architecture with local SQLite persistence
/// </summary>
public interface ISyncService
{
    DateTime? LastSyncTime { get; }
    bool IsSyncing { get; }
    event Action? OnSyncStateChanged;
    
    Task InitializeAsync();
    Task<bool> SyncNowAsync();
    string GetElapsedTimeSinceSync();
}

/// <summary>
/// Service for detecting and monitoring network connectivity status
/// Supports real-time online/offline detection for sync operations
/// </summary>
public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool>? OnConnectivityChanged;
    
    Task InitializeAsync();
    Task<bool> CheckConnectivityAsync();
}
```

**Key Characteristics:**
- Defines **what** the system does, not **how**
- Interfaces only - no implementations
- DTOs for data transfer across boundaries
- No database, HTTP, or UI concerns

### 3. Infrastructure Layer (PTDoc.Infrastructure)

**Purpose:** Implement Application interfaces using concrete technologies (EF Core, HTTP, file system).

**Structure:**
```
PTDoc.Infrastructure/
├── Data/
│   ├── ApplicationDbContext.cs
│   └── Migrations/
├── Services/
│   ├── CredentialValidator.cs
│   ├── TokenService.cs (future)
│   ├── PatientService.cs (future)
│   ├── SyncService.cs
│   └── ConnectivityService.cs
└── Auth/
    └── AuthenticatedHttpMessageHandler.cs
```

**Example Implementation:**
```csharp
namespace PTDoc.Infrastructure.Services;

public class CredentialValidator : ICredentialValidator
{
    private readonly ApplicationDbContext _context;
    
    public CredentialValidator(ApplicationDbContext context)
    {
        _context = context;
    }
    
    public async Task<ClaimsIdentity?> ValidateAsync(
        string username, string pin)
    {
        // Database lookup implementation
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);
        
        if (user == null || user.Pin != pin)
            return null;
        
        return new ClaimsIdentity(/* claims */, "PTDocAuth");
    }
}
```

**Offline-First Implementation:**

```csharp
namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Implementation of ISyncService using localStorage for persistence
/// Currently simulates sync operations (cloud API integration coming soon)
/// </summary>
public class SyncService : ISyncService
{
    private readonly IJSRuntime _jsRuntime;
    private DateTime? _lastSyncTime;
    private bool _isSyncing;
    
    public DateTime? LastSyncTime => _lastSyncTime;
    public bool IsSyncing => _isSyncing;
    public event Action? OnSyncStateChanged;
    
    public async Task InitializeAsync()
    {
        // Load last sync time from browser localStorage
        var storedTime = await _jsRuntime.InvokeAsync<string?>(
            "localStorage.getItem", "ptdoc_last_sync_time");
        
        if (!string.IsNullOrEmpty(storedTime) && 
            DateTime.TryParse(storedTime, out var parsedTime))
        {
            _lastSyncTime = parsedTime;
        }
    }
    
    public async Task<bool> SyncNowAsync()
    {
        if (_isSyncing) return false;
        
        try
        {
            _isSyncing = true;
            OnSyncStateChanged?.Invoke();
            
            // TODO: Replace simulation with actual sync
            // 1. Query local SQLite for changed records
            // 2. Push changes to cloud API
            // 3. Pull changes from cloud API
            // 4. Update local SQLite database
            await Task.Delay(1500); // Simulate network operation
            
            _lastSyncTime = DateTime.UtcNow;
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", 
                "ptdoc_last_sync_time", _lastSyncTime.Value.ToString("o"));
            
            return true;
        }
        finally
        {
            _isSyncing = false;
            OnSyncStateChanged?.Invoke();
        }
    }
    
    public string GetElapsedTimeSinceSync()
    {
        if (!_lastSyncTime.HasValue) return "Never";
        
        var elapsed = DateTime.UtcNow - _lastSyncTime.Value;
        if (elapsed.TotalSeconds < 10) return "Just now";
        
        // Format as "3m 25s ago" or "1h 4m ago"
        var parts = new List<string>();
        if (elapsed.Hours > 0) parts.Add($"{elapsed.Hours}h");
        if (elapsed.Minutes > 0) parts.Add($"{elapsed.Minutes}m");
        if (elapsed.TotalMinutes < 1) parts.Add($"{elapsed.Seconds}s");
        
        return parts.Any() ? string.Join(" ", parts) + " ago" : "Just now";
    }
}

/// <summary>
/// Implementation of IConnectivityService using browser Network Information API
/// Falls back to periodic checks if API is not available
/// </summary>
public class ConnectivityService : IConnectivityService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _isOnline = true; // Assume online initially
    
    public bool IsOnline => _isOnline;
    public event Action<bool>? OnConnectivityChanged;
    
    public async Task InitializeAsync()
    {
        try
        {
            // Check initial status and register event handlers
            _isOnline = await _jsRuntime.InvokeAsync<bool>("eval", "navigator.onLine");
            
            await _jsRuntime.InvokeVoidAsync("eval", $@"
                window.addEventListener('online', 
                    () => DotNet.invokeMethodAsync('PTDoc.Infrastructure', 
                        'OnConnectivityStatusChanged', true));
                window.addEventListener('offline', 
                    () => DotNet.invokeMethodAsync('PTDoc.Infrastructure', 
                        'OnConnectivityStatusChanged', false));
            ");
        }
        catch (InvalidOperationException)
        {
            // JSRuntime not available during prerender - assume online
            _isOnline = true;
        }
    }
    
    public async Task<bool> CheckConnectivityAsync()
    {
        try
        {
            _isOnline = await _jsRuntime.InvokeAsync<bool>("eval", "navigator.onLine");
            return _isOnline;
        }
        catch
        {
            return _isOnline; // Return last known state
        }
    }
}
```

**Key Characteristics:**
- Implements Application interfaces
- Contains EF Core DbContext and migrations
- Handles external dependencies (database, HTTP, storage)
- Platform-agnostic (shared between API, Web, MAUI)

### 4. Presentation Layer

#### 4a. PTDoc.Api

**Purpose:** REST API with JWT authentication for MAUI clients.

**Structure:**
```
PTDoc.Api/
├── Program.cs              # DI + middleware setup
├── Auth/
│   ├── AuthEndpoints.cs    # /auth/token, /auth/refresh
│   ├── JwtTokenIssuer.cs
│   ├── JwtOptions.cs
│   └── InMemoryRefreshTokenStore.cs
└── Controllers/ (future)
    └── PatientsController.cs
```

**Example Endpoint:**
```csharp
app.MapPost("/auth/token", async (
    LoginRequest request,
    ICredentialValidator validator,
    JwtTokenIssuer issuer) =>
{
    var identity = await validator.ValidateAsync(
        request.Username, request.Pin);
    
    if (identity == null)
        return Results.Unauthorized();
    
    var token = issuer.GenerateToken(identity);
    return Results.Ok(new TokenResponse { AccessToken = token });
})
.AllowAnonymous();
```

**Key Features:**
- JWT Bearer authentication
- OpenAPI/Swagger documentation
- CORS enabled for local development
- Rate limiting for production

#### 4b. PTDoc.Web

**Purpose:** Blazor web application with cookie-based authentication.

**Structure:**
```
PTDoc.Web/
├── Program.cs              # Cookie auth + reverse proxy
├── Auth/
│   └── WebUserService.cs
├── Components/
│   ├── App.razor
│   ├── Layout/
│   └── Pages/ (future)
└── wwwroot/
```

**Authentication:**
```csharp
builder.Services.AddAuthentication("PTDocAuth")
    .AddCookie("PTDocAuth", options =>
    {
        options.LoginPath = "/login";
        
        // HIPAA compliance: Session timeouts
        options.ExpireTimeSpan = TimeSpan.FromMinutes(15);  // Inactivity
        options.Cookie.MaxAge = TimeSpan.FromHours(8);      // Absolute
        options.SlidingExpiration = true;
        
        // Security settings
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });
```

**Key Features:**
- Stateless client (no local database)
- API-only data access via reverse proxy
- Cookie-based session management
- HIPAA-compliant session timeouts

#### 4c. PTDoc.Maui

**Purpose:** Cross-platform mobile/desktop app (iOS, Android, macOS) with offline capability.

**Structure:**
```
PTDoc.Maui/
├── MauiProgram.cs          # Platform setup
├── Auth/
│   ├── MauiAuthenticationStateProvider.cs
│   ├── MauiUserService.cs
│   └── SecureStorageTokenStore.cs
├── Pages/
├── Components/
└── Platforms/
    ├── Android/
    ├── iOS/
    └── MacCatalyst/
```

**Platform Configuration:**
```csharp
// MauiProgram.cs
#if ANDROID
    var apiBaseUrl = "http://10.0.2.2:5170"; // Android emulator → host
#else
    var apiBaseUrl = "http://localhost:5170"; // iOS/Mac
#endif

builder.Services.AddHttpClient<ITokenService, TokenService>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddScoped<ITokenStore, SecureStorageTokenStore>();
```

**Key Features:**
- EF Core + SQLite for offline storage
- SecureStorage for tokens (Keychain/Keystore)
- Sync with API when online
- Platform-specific native integrations

#### 4d. PTDoc.UI

**Purpose:** Shared Blazor Razor Class Library for reusable components.

**Structure:**
```
PTDoc.UI/
├── _Imports.razor
├── Components/
│   ├── Layout/
│   │   └── GlobalHeader.razor      # Menu, sync, connectivity
│   ├── PTDocMetricCard.razor
│   └── PatientCard.razor (future)
├── Pages/
│   └── Overview.razor (future)
└── wwwroot/
    ├── css/
    │   ├── tokens.css              # Design tokens
    │   └── app.css                 # Global styles
    ├── images/                     # UI assets
    └── js/
```

**Example Component:**
```razor
@* PTDoc.UI/Components/PTDocMetricCard.razor *@

<div class="metric-card">
    <h3>@Title</h3>
    <p class="metric-value">@Value</p>
    @if (ChildContent != null)
    {
        @ChildContent
    }
</div>

@code {
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;
    
    [Parameter]
    public string Value { get; set; } = string.Empty;
    
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

**GlobalHeader Component:**

The GlobalHeader component demonstrates offline-first UI patterns:

```razor
@* PTDoc.UI/Components/Layout/GlobalHeader.razor *@
@using PTDoc.Application.Services
@inherits GlobalHeaderBase

<header class="global-header">
    <div class="header-container">
        <!-- Menu Toggle Button -->
        <button type="button" class="menu-toggle" @onclick="ToggleMenu">
            @if (IsMenuOpen) { /* Chevron icon */ }
            else { /* Hamburger icon */ }
        </button>

        <!-- Right side controls -->
        <div class="header-controls">
            <!-- Last Sync Indicator -->
            <div class="sync-info">
                <span class="sync-text">Last sync: @LastSyncDisplay</span>
            </div>

            <!-- Sync Now Button -->
            <button type="button" 
                    class="sync-button"
                    disabled="@(IsSyncing || !IsOnline)"
                    @onclick="HandleSyncNow">
                <img src="sync-icon.svg" class="@(IsSyncing ? "syncing" : "")" />
                <span>@(IsSyncing ? "Syncing..." : "Sync Now")</span>
            </button>

            <!-- Online/Offline Badge -->
            <div class="status-badge @(IsOnline ? "online" : "offline")" 
                 role="status" aria-live="polite">
                <img src="online-icon.svg" />
                <span>@(IsOnline ? "Online" : "Offline")</span>
            </div>
        </div>
    </div>
</header>
```

**Code-Behind Pattern:**
```csharp
public class GlobalHeaderBase : ComponentBase, IDisposable
{
    [Inject] private ISyncService SyncService { get; set; } = default!;
    [Inject] private IConnectivityService ConnectivityService { get; set; } = default!;
    
    protected bool IsOnline => ConnectivityService.IsOnline;
    protected bool IsSyncing => SyncService.IsSyncing;
    protected string LastSyncDisplay => SyncService.GetElapsedTimeSinceSync();
    
    private Timer? _syncDisplayTimer;
    
    protected override void OnInitialized()
    {
        // Subscribe to service events
        SyncService.OnSyncStateChanged += HandleSyncStateChanged;
        ConnectivityService.OnConnectivityChanged += HandleConnectivityChanged;
    }
    
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SyncService.InitializeAsync();
            await ConnectivityService.InitializeAsync();
            
            // Update "Last sync" display every 10 seconds
            _syncDisplayTimer = new Timer(
                _ => InvokeAsync(StateHasChanged),
                null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            
            StateHasChanged();
        }
    }
    
    protected async Task HandleSyncNow()
    {
        if (!IsSyncing && IsOnline)
        {
            await SyncService.SyncNowAsync();
        }
    }
    
    private void HandleSyncStateChanged() => InvokeAsync(StateHasChanged);
    private void HandleConnectivityChanged(bool _) => InvokeAsync(StateHasChanged);
    
    public void Dispose()
    {
        SyncService.OnSyncStateChanged -= HandleSyncStateChanged;
        ConnectivityService.OnConnectivityChanged -= HandleConnectivityChanged;
        _syncDisplayTimer?.Dispose();
    }
}
```

**Key Features:**
- Shared between Web and MAUI apps
- WCAG 2.1 AA compliant components
- Reusable healthcare-specific widgets

## Data Flow

### Authentication Flow (MAUI)

```
┌──────────┐       ┌─────────┐       ┌─────────────┐
│  MAUI    │──1──▶│   API   │──2──▶│Credential   │
│  Client  │       │         │       │Validator    │
└──────────┘       └─────────┘       └─────────────┘
     │                  │                     │
     │                  │                     │
     │◀─────3───────────┤◀────────4───────────┘
     │   JWT Token      │    ClaimsIdentity
     │                  │
     5. Store in        │
        SecureStorage   │
     │                  │
     └──────6──────────▶│
       API calls with   │
       Bearer token     │
```

1. User enters credentials in MAUI app
2. API calls `ICredentialValidator.ValidateAsync()`
3. API returns JWT access + refresh tokens
4. MAUI stores tokens in `SecureStorage` via `ITokenStore`
5. Subsequent API calls include `Authorization: Bearer <token>`
6. `AuthenticatedHttpMessageHandler` auto-refreshes expired tokens

### Data Synchronization (MAUI ↔ API)

```
┌──────────────┐                    ┌──────────────┐
│ MAUI SQLite  │                    │  API Server  │
│   Database   │                    │   Database   │
└──────────────┘                    └──────────────┘
       │                                    │
       │  1. User creates patient locally   │
       ├──────────────────────────────────▶│
       │     POST /api/patients             │
       │                                    │
       │◀───────────────────────────────────┤
       │  2. Server returns created patient │
       │     with server-assigned ID        │
       │                                    │
       │  3. Update local database with     │
       │     server ID + ETag               │
       │                                    │
       │  4. Query for changes since last   │
       │     sync timestamp                 │
       ├──────────────────────────────────▶│
       │  GET /api/patients?updatedSince=   │
       │                                    │
       │◀───────────────────────────────────┤
       │  5. Receive delta changes          │
       │                                    │
```

**Conflict Resolution:**
- Server is always source of truth
- Client sends `If-Match: <ETag>` for updates
- Server returns `412 Precondition Failed` on conflict
- Client fetches latest and prompts user to resolve

### Offline-First Sync Flow

```
┌─────────────────┐         ┌─────────────────┐         ┌──────────────┐
│ GlobalHeader UI │         │  SyncService    │         │ Connectivity │
└────────┬────────┘         └────────┬────────┘         └──────┬───────┘
         │                           │                         │
         │  1. User clicks           │                         │
         │     "Sync Now"            │                         │
         ├──────────────────────────▶│                         │
         │                           │                         │
         │                           │  2. Check IsOnline      │
         │                           ├────────────────────────▶│
         │                           │                         │
         │                           │◀────────────────────────┤
         │                           │  3. Return online status│
         │                           │                         │
         │                           │  4. If online:          │
         │                           │     - Set IsSyncing=true│
         │                           │     - Fire OnSyncState  │
         │                           │       Changed event     │
         │                           │     - Simulate sync     │
         │                           │       (1.5s delay)      │
         │                           │     - Update LastSync   │
         │                           │     - Persist to        │
         │                           │       localStorage      │
         │                           │     - Set IsSyncing=false│
         │                           │     - Fire event again  │
         │                           │                         │
         │◀──────────────────────────┤                         │
         │  5. UI updates via event  │                         │
         │     (reactive binding)    │                         │
         │                           │                         │
```

**Key Features:**
- Event-driven state management (no manual polling)
- LocalStorage persistence across sessions
- Graceful handling of prerender/SSR scenarios
- Simulated sync (TODO: actual EF Core + API integration)

**Future Implementation:**
1. Query local SQLite for records with `SyncState = Pending`
2. Push changes to cloud API with optimistic concurrency
3. Pull delta changes from API (timestamp-based)
4. Update local database and mark as `SyncState = Synced`
5. Handle conflicts according to `docs/PTDocs+_Offline_Sync_Conflict_Resolution.md`

## Security Architecture

### Authentication Methods

| Platform | Method | Storage | Expiration |
|----------|--------|---------|------------|
| API | JWT Bearer | N/A (stateless) | 15 min access, 30 day refresh |
| Web | Cookie | Server session | 15 min inactivity, 8 hr absolute |
| MAUI | JWT Bearer | SecureStorage (Keychain/Keystore) | 15 min access, 30 day refresh |

### HIPAA Compliance

**Session Timeouts:**
- 15-minute inactivity timeout (sliding)
- 8-hour absolute timeout (non-sliding)
- Automatic logout on timeout

**Audit Trails:**
- Log all PHI access (who, what, when)
- Track authentication attempts
- Record data modifications with timestamps

**Encryption:**
- TLS 1.2+ for data in transit
- SQLite encryption for data at rest (MAUI)
- Secure token storage (Keychain/Keystore)

**Access Controls:**
- Role-based authorization (RBAC)
- Attribute-based access control (ABAC) for clinical roles
- Minimum necessary access principle

## Technology Stack

### Backend
- **.NET 8.0** - Cross-platform framework
- **ASP.NET Core** - Web API framework
- **Entity Framework Core** - ORM
- **SQLite** - Embedded database
- **JWT** - Token-based authentication

### Frontend
- **Blazor Server** - Web app (PTDoc.Web)
- **Blazor WebAssembly** - Web app option
- **Blazor Hybrid** - MAUI app
- **.NET MAUI** - Cross-platform UI framework

### Infrastructure
- **GitHub Actions** - CI/CD (future)
- **Docker** - Containerization (future)
- **Azure** - Cloud hosting (future)

## Design Patterns

### Dependency Injection

All services registered in `Program.cs` with appropriate lifetimes:

```csharp
// Singleton: Shared across all requests/users
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();

// Scoped: One instance per request (API) or circuit (Blazor Server)
builder.Services.AddScoped<ICredentialValidator, CredentialValidator>();

// Transient: New instance every time
builder.Services.AddTransient<AuthenticatedHttpMessageHandler>();
```

### Repository Pattern (Future)

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<List<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
}

public class PatientRepository : IRepository<Patient>
{
    private readonly ApplicationDbContext _context;
    // Implementation...
}
```

### Unit of Work Pattern (Future)

```csharp
public interface IUnitOfWork
{
    IRepository<Patient> Patients { get; }
    IRepository<Appointment> Appointments { get; }
    Task<int> SaveChangesAsync();
}
```

## Scalability Considerations

### Current State (MVP)
- Single SQLite database per platform
- In-memory refresh token store (API)
- Synchronous API endpoints

### Future Improvements
- **Database:** Migrate to PostgreSQL/SQL Server for production
- **Caching:** Redis for distributed caching
- **Token Store:** Database-backed refresh token storage
- **API:** Async/await throughout, pagination, rate limiting
- **CDN:** Static assets served from CDN
- **Load Balancing:** Multiple API instances behind load balancer

## Testing Strategy

### Unit Tests
- Core domain logic (validation, calculations)
- Application interfaces (mock implementations)
- Infrastructure services (in-memory database)

### Integration Tests
- API endpoints (TestServer)
- Database operations (SQLite in-memory)
- Authentication flows (JWT validation)

### E2E Tests
- Blazor components (bUnit)
- Full user workflows (Playwright)
- Platform-specific (iOS/Android simulators)

## Deployment Architecture

### Development
```
Developer Machine
├── PTDoc.Api (localhost:5170)
├── PTDoc.Web (localhost:5145)
└── PTDoc.Maui (iOS Simulator / Android Emulator)
```

### Production (Future)
```
┌──────────────────────────────────────────┐
│             Load Balancer                │
└──────────────┬───────────────────────────┘
               │
    ┌──────────┴──────────┐
    │                     │
┌───▼───┐            ┌────▼────┐
│ API   │            │  Web    │
│ VM 1  │            │  VM     │
└───┬───┘            └────┬────┘
    │                     │
┌───▼──────────────────────▼───┐
│    PostgreSQL Database       │
└──────────────────────────────┘
```

### Mobile Distribution
- **iOS:** App Store (requires Apple Developer account)
- **Android:** Google Play Store (requires Google Play Console)
- **macOS:** Direct distribution or Mac App Store

## Related Documentation

- [BUILD.md](BUILD.md) - Build instructions
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform differences
- [SECURITY.md](SECURITY.md) - Security considerations
- [Blazor-Context.md](Blazor-Context.md) - Blazor architecture patterns
- [PTDocs+_Offline_Sync_Conflict_Resolution.md](PTDocs+_Offline_Sync_Conflict_Resolution.md) - Offline-first specifications
