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
│   └── IPatientService.cs (future)
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
│   └── PatientService.cs (future)
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
│   ├── PTDocMetricCard.razor
│   └── PatientCard.razor (future)
├── Pages/
│   └── Overview.razor (future)
└── wwwroot/
    ├── css/
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

## Production Database Configuration

### Provider Selection

The API selects the database provider at startup via the `Database:Provider`
configuration key (or its environment variable equivalent `Database__Provider`):

| Value | Provider | Use Case |
|-------|----------|----------|
| `Sqlite` | SQLite | Local development (default) |
| `SqlServer` | Microsoft SQL Server | Production / staging |
| `Postgres` | PostgreSQL | Production / staging |

For non-SQLite providers, also supply `ConnectionStrings:PTDocsServer`
(`ConnectionStrings__PTDocsServer` as an environment variable).

### Migration Safety

The `Database:AutoMigrate` setting controls whether EF Core migrations run
automatically at startup:

| Environment | Default | Recommended |
|-------------|---------|-------------|
| `Development` | `true` (auto-migrate) | Default is fine |
| `Production` | `false` (no auto-migrate) | Run migrations via CLI |

Override the default by setting `Database:AutoMigrate` (`Database__AutoMigrate`
as an env var) to `true` or `false` explicitly.

### Required Environment Variables for Production

```bash
ASPNETCORE_ENVIRONMENT=Production
Database__Provider=SqlServer           # or Postgres
ConnectionStrings__PTDocsServer=...    # full connection string (from secrets manager)
Jwt__SigningKey=...                     # ≥ 32-char secret (from secrets manager)
```

> **Security:** Never commit production connection strings or signing keys.
> See `docs/SECURITY.md` and `docs/EF_MIGRATIONS.md` for deployment guidance.

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

## Observability & Health Monitoring (Sprint F)

### Health Endpoints

The API exposes two standard health check endpoints for deployment platforms and
load balancers. Both endpoints are publicly accessible (no authentication required)
and never return sensitive configuration data.

| Endpoint | Purpose | HTTP Status |
|----------|---------|-------------|
| `GET /health/live` | Liveness — confirms the process is running | 200 OK |
| `GET /health/ready` | Readiness — confirms DB connectivity + migration state | 200 / 503 |

**Readiness response format (JSON):**
```json
{
  "status": "Healthy",
  "checks": [
    { "name": "database",   "status": "Healthy", "description": "Database is reachable.", "durationMs": 12.3 },
    { "name": "migrations", "status": "Healthy", "description": "All migrations are applied.", "durationMs": 4.1 }
  ]
}
```

When `status` is `"Unhealthy"`, the response uses HTTP 503. `"Degraded"` (pending
migrations) returns 200 so the service continues to receive traffic while an
operator investigates.

### Diagnostics Endpoint

`GET /diagnostics/db` — requires authentication (Bearer token). Returns:

| Field | Description |
|-------|-------------|
| `provider` | Active database provider (`Sqlite`, `SqlServer`, or `Postgres`) |
| `connectivity` | `"Connected"` or `"Unreachable"` |
| `migrationStatus` | `"Current"` or `"PendingMigrations"` |
| `appliedMigrationCount` | Number of migrations applied to the database |
| `pendingMigrationCount` | Number of migrations not yet applied |
| `pendingMigrations` | Names of pending migrations (empty list when current) |

> **Security:** Connection strings and encryption keys are never included in
> diagnostics responses. The endpoint always requires a valid JWT Bearer token.

### Migration State Logging

At startup the API logs:

- Selected database provider (`Information` level).
- Whether auto-migrate is enabled (`Information` level).
- Names of pending migrations before applying them (`Information` level).
- Successful migration application (`Information` level).

If the `MigrationStateHealthCheck` detects drift at runtime, it logs a `Warning`
with the list of pending migration names.

### Migration Safety

See [EF_MIGRATIONS.md](EF_MIGRATIONS.md) for deployment migration commands.

The CI `db-migration-validate` job (Sprint F) validates:

1. **No pending model changes** — `dotnet ef migrations has-pending-model-changes`
   exits non-zero if the EF model has diverged from the last snapshot.
2. **Migration state tests** — `[Category=Observability]` tests verify that
   `GetPendingMigrationsAsync()` returns an empty list after `MigrateAsync()`.

## Background Jobs & Async Processing (Sprint I)

PTDoc uses native .NET hosted services (`BackgroundService`) for periodic maintenance tasks.
No external job queue or scheduler is required for current workloads.

### Services

| Service | Project | Default Interval | Purpose |
|---------|---------|-----------------|---------|
| `SyncRetryBackgroundService` | Infrastructure | 30 s | Resets eligible failed sync queue items to `Pending` and triggers a push cycle |
| `SessionCleanupBackgroundService` | Infrastructure | 5 min | Revokes expired user sessions via `IAuthService.CleanupExpiredSessionsAsync` |

### Configuration

Options are read from `appsettings.json` (or environment variables):

```json
{
  "BackgroundJobs": {
    "SyncRetry": {
      "Interval": "00:00:30",
      "MinRetryDelay": "00:01:00"
    },
    "SessionCleanup": {
      "Interval": "00:05:00"
    }
  }
}
```

- **`BackgroundJobs:SyncRetry:Interval`** — how often the retry sweep runs (default 30 s).
- **`BackgroundJobs:SyncRetry:MinRetryDelay`** — minimum age of `LastAttemptAt` before an item is eligible for retry (default 60 s, prevents hot-retry loops).
- **`BackgroundJobs:SessionCleanup:Interval`** — how often expired sessions are swept (default 5 min).

### Design Principles

- Each hosted service is registered as a **singleton** (standard for `IHostedService`).
- Scoped services (`ApplicationDbContext`, `ISyncEngine`, `IAuthService`) are consumed
  via **`IServiceScopeFactory`** — a fresh scope is created per execution cycle.
- Jobs are **idempotent**: running them multiple times produces the same result.
- A failure in one cycle is logged and the service continues to the next interval —
  a transient error never kills the background host.
- **No items beyond `MaxRetries`** are ever reset — permanent failures stay `Failed`.
- **`MinRetryDelay`** prevents hot-retry of items that just failed.

### Adding a New Background Job

1. Add configuration options to `PTDoc.Application/BackgroundJobs/IBackgroundJobService.cs`.
2. Implement `BackgroundService` + `IBackgroundJobService` in `PTDoc.Infrastructure/BackgroundJobs/`.
3. Register in `PTDoc.Api/Program.cs`:
   ```csharp
   builder.Services.Configure<YourJobOptions>(
       builder.Configuration.GetSection(YourJobOptions.SectionName));
   builder.Services.AddHostedService<YourBackgroundService>();
   ```
4. Add unit tests in `tests/PTDoc.Tests/BackgroundJobs/`.

## Multi-Tenant / Multi-Clinic Architecture (Sprint J)

PTDoc is designed to operate in a multi-clinic (multi-tenant) environment where each clinic's data is completely isolated from others.

### Tenancy Model

PTDoc uses a **shared-database, shared-schema** tenancy model with row-level filtering. All clinics share the same database tables. Each row in tenant-scoped tables carries a `ClinicId` foreign key that identifies its owning clinic.

This model was chosen because:
- It avoids the operational complexity of database-per-tenant
- It matches the current infrastructure and EF Core capabilities
- Row-level filtering can be enforced at the ORM layer via global query filters

### Tenant Entity

```csharp
// PTDoc.Core/Models/Clinic.cs
public class Clinic
{
    public Guid Id { get; set; }
    public string Name { get; set; }    // Display name
    public string Slug { get; set; }    // URL-friendly identifier (unique)
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### Tenant-Scoped Entities

The following entities carry `ClinicId` and are subject to tenant filtering:

| Entity           | ClinicId | Notes                                        |
|------------------|----------|----------------------------------------------|
| `Patient`        | ✓        | Root aggregate; all related data inherits scope |
| `Appointment`    | ✓        | Denormalized from Patient for query efficiency |
| `ClinicalNote`   | ✓        | Denormalized from Patient for query efficiency |
| `IntakeForm`     | ✓        | Denormalized from Patient for query efficiency |
| `User`           | ✓        | Clinicians belong to one clinic              |

Non-clinical entities (`AuditLog`, `SyncQueueItem`, `Session`, etc.) are **not** tenant-scoped — they are system-level or user-level.

### Data Access Isolation

Tenant isolation is enforced via **EF Core global query filters** on `ApplicationDbContext`:

```csharp
// Applied automatically to every query on tenant-scoped entities
modelBuilder.Entity<Patient>()
    .HasQueryFilter(p => CurrentClinicId == null || p.ClinicId == null || p.ClinicId == CurrentClinicId);
```

- If a tenant scope is active (`clinic_id` JWT claim present), queries automatically add `WHERE ClinicId = @current` to every query.
- If no tenant scope exists (system-level background jobs), the filter is bypassed and all rows are visible.
- Legacy rows with `ClinicId = NULL` (pre-Sprint J data) remain visible to all clinics for backward compatibility.

To intentionally bypass filters for admin/migration operations:
```csharp
context.Set<Patient>().IgnoreQueryFilters().Where(...);
```

### Tenant Context Flow

```
JWT Token (clinic_id claim)
    ↓
HttpTenantContextAccessor.GetCurrentClinicId()
    ↓
ApplicationDbContext._tenantContext?.GetCurrentClinicId()
    ↓
EF Core global query filter evaluation per query
```

### Identity Integration

The `clinic_id` is embedded in JWT access tokens at authentication time:

```
POST /auth/token → ClaimsIdentity includes: Claim("clinic_id", user.ClinicId.ToString())
POST /api/v1/auth/pin-login → AuthResult.ClinicId from User.ClinicId
GET /api/v1/auth/me → CurrentUserResponse.ClinicId
```

The `ITenantContextAccessor` interface (in `PTDoc.Application/Identity`) abstracts the tenant-resolution logic. The `HttpTenantContextAccessor` implementation reads the `clinic_id` JWT claim from the HTTP context.

### Backward Compatibility

Existing deployments are preserved:
- `ClinicId` is **nullable** on all entities — existing rows without a clinic assignment continue to work.
- Legacy patients (null ClinicId) are visible to any tenant context.
- New patients/appointments created by a clinic-scoped user automatically receive that clinic's ID (set at the service/API layer when creating records).

### Development Default Clinic

The `DatabaseSeeder` seeds a default development clinic:
- **ID:** `00000000-0000-0000-0000-000000000100`
- **Name:** `PTDoc Development Clinic`
- **Slug:** `ptdoc-dev`
- The `testuser` is assigned to this clinic.
- The demo `CredentialValidator` includes this clinic ID in JWT claims.

### Adding a New Clinic (Future)

When implementing full clinic management:
1. Create a `POST /api/v1/admin/clinics` endpoint
2. Assign users to the new clinic via `User.ClinicId`
3. New clinical records created by those users automatically inherit the clinic scope
4. No data migration required for existing records (they remain `null`-scoped)

## Related Documentation

- [BUILD.md](BUILD.md) - Build instructions
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform differences
- [SECURITY.md](SECURITY.md) - Security considerations
- [Blazor-Context.md](Blazor-Context.md) - Blazor architecture patterns
