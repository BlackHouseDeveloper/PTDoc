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

### Third-party clinical integrations

Humble Fax and Wibbi use clinic-scoped server-side provider adapters. Core owns provider-neutral fax/HEP state; Application owns integration contracts; `PTDoc.Integrations` owns provider HTTP payloads; Infrastructure owns connection persistence, private documents, transactional outbox processing, mappings, checkpoints, reconciliation, and notifications; API/UI are the façade and workflow surfaces. Core/Application never reference provider SDKs or Infrastructure, and browser/MAUI clients never call providers directly.

See [`HUMBLE_FAX_WIBBI_INTEGRATIONS.md`](HUMBLE_FAX_WIBBI_INTEGRATIONS.md) for configuration, data ownership, RBAC, deployment, and recovery.

### Reference Data Source Model

Clinic reference markdown files under `docs/clinicrefdata/` are governed by the status index in `docs/clinicrefdata/README.md`, which classifies each file as an authoring source, reference-only material, or archived history.

Lookup coverage is the one domain where markdown remains the canonical authoring source. `docs/clinicrefdata/ICD-10 codes.md` and `docs/clinicrefdata/Commonly used CPT codes and modifiers.md` are mirrored into `PTDoc.Application/Data/WorkspaceLookupReferenceData.json`, and the runtime lookup asset may add application-only metadata such as search terms or derived defaults. For CPT lookup, the modifier option list mirrors the markdown modifier cheat sheet, while `defaultPtSuggestedModifiers` is app policy derived from the markdown's GP plan-of-care guidance.

Intake catalogs, workspace catalogs, and outcome-measure definitions are runtime-canonical after Branches 1-4. Their authoritative implementations live in runtime services or embedded assets such as `PTDoc.Infrastructure/ReferenceData/IntakeReferenceDataCatalogService.cs`, `PTDoc.Application/Data/IntakeSupplementalReferenceData.json`, `PTDoc.Application/Data/WorkspaceReferenceCatalog.json`, and `PTDoc.Application/Data/OutcomeMeasureReferenceData.json`. Clinic markdown files in those domains are retained as human traceability references unless the README explicitly marks them as authoring sources.

The versioned intervention library is part of the embedded workspace catalog. Exercise and manual-technique selections persist both their stable catalog ID/version and a display snapshot in workspace schema v2. Structured exercise prescription and manual-technique fields are additive properties of `ClinicalNote.ContentJson`; no EF aggregate or database migration is required. New structured prescriptions also write the legacy dosage summary for older readers, while arbitrary historical dosage text remains an unparsed compatibility fallback.

`ReferenceDataProvenance.DocumentPath` is a repo-relative traceability pointer, not an authority signal by itself. Consumers must determine authority from the domain policy above and the `docs/clinicrefdata/README.md` classification. Archived clinic docs may still surface only when compatibility normalization remaps legacy persisted values to `docs/clinicrefdata/archive/...`; live runtime assets and services must not cite archived docs as active provenance.

Cross-layer normalization helpers for clinic reference-data provenance live in `PTDoc.Application.ReferenceData` so Infrastructure and Presentation can normalize compatibility paths without violating Clean Architecture dependencies.

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

Local Web sign-in uses an immutable identity ticket capped by the API deadline and
eight-hour absolute limit. `WebSessionActivity.ValidateCookieAsync` additionally
enforces the 15-minute idle window using the ticket's initial issue time or a
Data Protection-protected activity timestamp bound to that sign-in ID. Real input
renews only the separate `__Host-PTDoc.Activity.<session-id>` cookie after API
validation; it never rewrites the identity cookie. Automatic cookie sliding is
disabled for local sessions, so delayed renewal/denial responses cannot overwrite
another tab's sign-in or restore a logged-out browser. No Web database or shared
in-memory ticket store is introduced. OIDC retains its existing cookie behavior.

`WebSessionActivityStore` holds only the latest validated activity timestamp per
local sign-in, bounded by its absolute deadline. Cookie validation observes the
protected timestamp; a successful activity endpoint renews it only if the idle
window is still open after API validation. Circuit API forwarding checks this
server-side idle window before dispatch, so a click racing the browser monitor or
background autosave cannot bypass the 15-minute Web limit. Protected API traffic
never advances activity. The store is process-local: after restart or routing to
a different process, a validated browser activity cookie can seed it again;
without that evidence, forwarding falls back to the signed initial sign-in time
and fails closed once that window expires. It stores no token or draft content.

Local logout and replacement sign-in revoke the prior opaque API session through
`/api/v1/auth/logout` before completing the change. Local logout writes an
HttpOnly/Secure/SameSite=Strict `__Host-PTDoc.SignedOut.<sign-in-id>` marker rather
than deleting the shared identity cookie. Cookie validation rejects that sign-in;
a delayed logout response cannot delete a newer sign-in. Pre-activity tickets
without a sign-in ID use a SHA-256 digest of their opaque token as the marker key.
Markers contain no credential and expire at the original session deadline. They
only deny browser authentication; API-side revocation remains authoritative even
if a browser removes a marker. Replacement sign-in still issues a fresh identity
ticket. A revocation failure returns a retryable 503 and keeps the existing identity cookie. A terminal
activity result or API rejection also ends the circuit's scoped
`WebCircuitSessionState`; its HTTP forwarding handler rejects subsequent protected
operations while the monitor offers explicit recovery without navigating away
from unsaved drafts. `WebScopedHttpClientFactory` creates the authentication
wrapper in the caller's request/circuit scope for both named `ServerAPI` clients
and injected UI clients. Only the transport pipeline is pooled, and disposing a
wrapper does not dispose that shared transport. Other named clients retain their
configured transport and options without the authentication wrapper.

For current local activity tickets, `WebSessionCircuitHandler` binds the circuit
to the sign-in ID present when it opens. Blazor can replace the authenticated
principal on reconnect while preserving component state. A changed sign-in ends
the retained circuit with Reload recovery; an anonymous principal ends it with
Sign in recovery. Authentication-state notifications update the shared monitor,
and the forwarding handler independently validates the current circuit principal
before using a token, ignoring a stale handshake HttpContext in a bound circuit.
This prevents retained drafts from being submitted using a replacement identity.
`WebCircuitAuthenticationStateProvider` keeps the original UI snapshot for an
ended bound circuit so authorization views do not replace its editor or redirect
before explicit recovery. That snapshot cannot authorize API operations: the
shared terminal state blocks forwarding. Unbound prerender/request state still
accepts normal host authentication updates.
Request-only clients, previous-version tickets and OIDC keep their existing paths.

The activity monitor starts only for tickets carrying both a sign-in ID and the
current activity-ticket marker. Previous-version tickets keep normal expiry until
the next sign-in. Input throttling uses `performance.now()` so a wall-clock
correction cannot suspend renewal; the first trusted input is eligible immediately.
If JavaScript import/start fails during a disconnect, the circuit's next connection-up
notification retries startup. Concurrent notifications coalesce, and a per-component
startup ID makes a repeated JavaScript start preserve existing listeners, throttling
and in-flight renewal. No timer renews the session. Terminated/disposed monitors do
not restart.

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
| `GET /health` | Simple JSON process health for hosting smoke tests | 200 OK |
| `GET /health/live` | Liveness — confirms the process is running | 200 OK |
| `GET /health/ready` | Readiness — confirms DB connectivity + migration state | 200 / 503 |

**Simple health response format (JSON):**
```json
{
  "status": "Healthy",
  "app": "PTDoc API",
  "environment": "Production",
  "timestampUtc": "2026-05-23T00:00:00+00:00"
}
```

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

`GET /diagnostics/runtime` — also requires authentication (Bearer token). Returns:

| Field | Description |
|-------|-------------|
| `environmentName` | Active ASP.NET environment name (for example `Development`, `Testing`, `Production`) |
| `isDevelopment` | Whether the runtime is in `Development` mode |
| `release.releaseId` | Optional deployment/release identifier from `PTDOC_RELEASE_ID` or `Release:Id` |
| `release.sourceSha` | Optional source revision SHA from `PTDOC_SOURCE_SHA`, `Release:SourceSha`, or assembly metadata |
| `release.imageTag` | Optional image or package tag from `PTDOC_IMAGE_TAG` or `Release:ImageTag` |
| `aiRuntime.featureEnabled` | Whether `FeatureFlags:EnableAiGeneration` is enabled |
| `aiRuntime.developerDiagnosticsEnabled` | Whether developer-only diagnostics such as AI fault injection are enabled for this runtime |
| `aiRuntime.startupValidationMode` | `Disabled`, `EagerAtStartup`, or `LazyOnFirstRequest` depending on feature-flag state and environment mode |
| `aiRuntime.configurationState` | `NotRequired`, `Complete`, or `Incomplete` for the required Azure OpenAI settings |
| `aiRuntime.missingAzureOpenAiSettings` | Names of required Azure OpenAI settings that are still missing |
| `aiRuntime.runtimeHealthGate` | Explicit next gate required before treating AI as healthy in the environment |

> **AI runtime note:** `GET /diagnostics/runtime` does not prove Azure OpenAI
> provider execution. Health probes and diagnostics cover process, environment,
> and database readiness only. Treat one authenticated saved-note AI request as
> the operational runtime-health gate for AI-enabled deployments.

When `aiRuntime.developerDiagnosticsEnabled` is `true`, the API also exposes an
admin-only developer diagnostics surface for deterministic AI fault injection:

- `GET /diagnostics/ai-faults`
- `PUT /diagnostics/ai-faults`
- `DELETE /diagnostics/ai-faults`

These endpoints are intentionally in-memory, one-shot, and actor-scoped by
`mode + noteId + targetUserId`. They exist to support troubleshooting and live
verification of clinician-safe failure paths without breaking the real Azure
OpenAI configuration.

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

## Organizations and clinic isolation

PTDoc uses a shared database and schema. Organizations contain clinics, while each
patient and clinical record retains its owning `ClinicId`. Organization membership
never confers patient access. These choices were approved for [Plan 05](client-review/plan-05.md).

### Membership and administration

`Organization`, `OrganizationMembership`, `ClinicMembership`, and
`OrganizationInvitation` are Core entities. Application defines the administration
contracts; Infrastructure owns persistence and authorization; API exposes the routes;
the shared UI provides `/organizations` on Web and MAUI.

| Authority | Organization staff metadata | Clinic/patient access |
| --- | --- | --- |
| Organization Owner | Create clinics, invite/approve staff, manage organization roles and all clinic grants; retain an active owner | Only through a separate active clinic membership |
| Organization Administrator | Invite/approve ordinary staff and manage ordinary clinic grants in their organization | Only through a separate active clinic membership |
| Organization Member | View own organizations and clinic grants; accept an email-bound invitation | Selected clinic membership's role/capabilities only |
| Clinic Owner/Admin/PT/PTA/etc. | No organization administration from a clinic role alone | Existing role/capability policies within the selected clinic |
| Platform/background service | Explicit operational scope only | No implicit organization-wide PHI grant |

Memberships have composite organization/clinic + user keys, active state, and
optimistic versions. Mutations update the organization's concurrency version before
committing access changes and audit records. An owner cannot remove the last active
owner account. Administrators cannot grant organization authority or manage clinic
Owner/Admin grants or organization owners. Registration review is limited to clinics
in organizations the actor administers; account-wide credential resets retain their
home-clinic administration boundary.

Organization administrators may explicitly grant themselves ordinary clinic roles
(PT, PTA, FrontDesk, Billing, Aide or PracticeManager) within their organization.
The user confirmed this policy during PR 160 review. These grants use the same
membership, version and audit checks as grants to other staff. Owner/Admin clinic
grants and organization-role changes still require an organization owner; no
clinical access is automatically conferred by an organization role.

Patient portal accounts are excluded from organization staff listings, staff grants,
organization-role changes and invitation issuance/acceptance. Their existing home
memberships remain for patient authentication; those rows do not make them staff.
Registration administrators cannot edit or reactivate an organization owner merely
because that account has an ordinary home-clinic role.

Invitations contain a random, single-use, 72-hour code; only its SHA-256 hash is
persisted. The accepting account must be active and have the matching email. The
inviter must still be an active owner/administrator. Acceptance creates an ordinary
organization membership with no clinic grants. New staff use clinic registration
and local organization approval. Invitation delivery is manual/private; PTDoc does
not automatically send these codes.

### One selected clinic per session

`User.ClinicId` and `User.Role` remain the legacy home/registration values. They do
not describe every grant. `ClinicMembership.Role` supplies the role in a selected
clinic. Scheduling and fax triage use active memberships, allowing a clinician to
work in two clinics without moving their user or patients.

Compliance overrides, signing and co-signing resolve the actor's live role in the
note's owning clinic through `ClinicMembershipAccess`; they do not infer authority
from the home role. Overrides and co-signatures require PT in that clinic. Signing
retains the existing PT/PTA note-type and co-signature rules.

A local PIN sign-in accepts an optional `ClinicId`; omission selects the home clinic.
Changing clinics requires fresh sign-in and the target clinic's PIN/MFA policy.
Protected PIN/MFA challenges carry the selected clinic across requests. Opaque
sessions persist that selection; JWTs and refresh tokens carry `clinic_id`. The
session validator, local JWT validator, refresh-token store, and API membership
middleware recheck active user, clinic, organization and memberships. The middleware
also limits Entra roles to the current clinic grant. Entra continues using its
provisioned tenant mapping; interactive multi-clinic selection uses local PIN sign-in.

`Guid.Empty` is a deliberate administration-only selection, distinct from null. It
uses the account's home security policy, emits only `OrganizationMember`, and the
API limits authenticated protected requests to organization and registration
administration routes. It cannot become an unscoped clinical session. Real clinics
must never use that reserved ID. Null session clinic values are legacy sessions and
resolve to the home clinic.

### Data isolation

`ITenantContextAccessor` resolves the selected clinic. Authenticated principals
without a tenant mapping fail provisioning. Clinical queries use global filters:

```csharp
modelBuilder.Entity<Patient>()
    .HasQueryFilter(patient => CurrentClinicId == null || patient.ClinicId == CurrentClinicId);
```

Patients, appointments, notes, intakes, documents, and their scoped children retain
these filters and existing endpoint/capability checks. Legacy null-owned records
are not visible in a clinic-scoped request. Background jobs without an HTTP tenant
must constrain their work explicitly; `IgnoreQueryFilters()` is not an organization
authorization mechanism. Organization administration queries only staff metadata
and always check the actor against the requested organization. Client-push creation
stamps the selected clinic for patients and derives appointment ownership from the
scoped patient; it never trusts a client-supplied tenant ID.

Registration administration follows the same separation: organization authority
alone can review staff registrations, but cannot list, read, edit or approve Patient
accounts. The retained patient-registration workflow additionally requires the
patient's home clinic to be the selected clinic and a live Admin/Owner grant there.
This requirement also applies when changing a staff registration's role to Patient.
Null and administration-only selections do not authorize patient registration access.

MAUI partitions local data, drafts, pending sync and watermarks by user plus clinic.
A shared scope lease serializes sync/cache operations with credential replacement.
Unscoped legacy device data is retained for controlled recovery, never assigned to
whichever clinic signs in next. See [MOBILE_ARCHITECTURE.md](MOBILE_ARCHITECTURE.md).

### Migration and clinic creation

`20261103030000_AddOrganizationMemberships` exists for SQLite, SQL Server and Postgres.
Every existing clinic initially receives its own organization, using the clinic ID
as organization ID. Existing home users receive explicit memberships preserving
their clinic role; active home Admin/Owner users become organization owners. Other
users become ordinary organization members. Patient, note, signed-record and clinic
IDs are unchanged. Null home users remain unassigned. See
[EF_MIGRATIONS.md](EF_MIGRATIONS.md) for rehearsal and rollback requirements.

New clinic creation through `/api/v1/organizations/{organizationId}/clinics` is owner
only and grants no staff access automatically. Legacy trusted clinic creation gets
its own organization, and registration gets an explicit home membership while the
account remains inactive. Trusted creation of the first already-active Admin/Owner
can bootstrap an ownerless organization; public inactive registrations cannot.
First assignment of an unassigned legacy home account gets initial memberships only
if no memberships exist. Save-time compatibility never restores revoked grants.

There is no automatic merger of independently backfilled organizations, clinic
reparenting, or reassignment of clinical records. The development default clinic
remains `DatabaseSeeder.DefaultClinicId` (`00000000-0000-0000-0000-000000000100`).

## Intake reconciliation concurrency

Explicit profile acceptance writes a new server-owned `ProfileSourceRevision` into the existing intake response JSON. Ordinary HTTP, service, and offline-sync writes must echo that revision before answers can be combined with stored source provenance; missing or older revisions require a reload. New and legacy drafts without a revision retain their existing behavior until the first explicit reconciliation. Creation strips caller-supplied revisions, and the wizard carries the accepted revision through reload/save without treating it as editable data.

`IntakeForm.LastModifiedUtc` is an EF concurrency token so a reconciliation committed between a writer's read and save cannot be overwritten. Both HTTP update routes return 409 on that race, and the direct service reports a reload error. Offline sync rejects stale source revisions before timestamp-based last-write-wins resolution. All three provider snapshots contain the concurrency metadata; no schema or data migration is required. Primary authorization answers are retained during reconciliation only when both normalized policy identity and legacy payer fingerprint remain the same.

## Related Documentation

- [BUILD.md](BUILD.md) - Build instructions
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows
- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform differences
- [SECURITY.md](SECURITY.md) - Security considerations
- [Blazor-Context.md](Blazor-Context.md) - Blazor architecture patterns
