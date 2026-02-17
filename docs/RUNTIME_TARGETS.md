# Runtime Targets

PTDoc supports multiple deployment targets with different data access patterns.

## Target Platforms

### Web (net8.0)

**Characteristics:**
- **Stateless client** - No local database
- **API-only data access** - All data requests go through HTTP
- **No EF Core/SQLite dependencies** - Keeps bundle size minimal
- **Ephemeral caching** - Memory, IndexedDB, or localStorage only

**Project:** `PTDoc.Web`

**Authentication:** Cookie-based with 15-min inactivity + 8-hour absolute HIPAA timeout

### MAUI Devices (net8.0-android | net8.0-ios | net8.0-maccatalyst)

**Characteristics:**
- **Offline-first** - EF Core + SQLite for local storage
- **Sync-capable** - Push changes to API when online
- **Platform APIs** - Access to SecureStorage, file system, native features

**Project:** `PTDoc.Maui`

**Authentication:** JWT tokens stored in platform SecureStorage

## Data Synchronization Model

PTDoc implements offline-first synchronization using two core services:
- **ISyncService** - Manages sync state, triggers, and timestamps
- **IConnectivityService** - Monitors network connectivity in real-time

See [ARCHITECTURE.md](ARCHITECTURE.md) for service interfaces and [DEVELOPMENT.md](DEVELOPMENT.md) for implementation patterns.

### Source of Truth

- **API server** is always the authoritative source
- Devices sync local changes to server
- Conflicts are resolved server-side

### Concurrency Control

**ETags for optimistic concurrency:**
```http
GET /api/patients/123
ETag: "abc123xyz"

PUT /api/patients/123
If-Match: "abc123xyz"
```

**Conflict response:**
```http
HTTP/1.1 412 Precondition Failed
Content-Type: application/json

{
  "error": "Conflict",
  "message": "Resource has been modified by another user",
  "currentETag": "def456uvw"
}
```

### Delta Synchronization

**Query for changes since last sync:**
```http
GET /api/patients?updatedSince=2026-01-28T10:30:00Z
```

**Response includes:**
- All records modified after the timestamp
- Deleted record IDs (soft deletes tracked)

**Client responsibilities:**
- Track last successful sync timestamp (via `ISyncService.LastSyncTime`)
- Monitor connectivity status (via `IConnectivityService.IsOnline`)
- Request delta changes on reconnection
- Apply server changes to local database
- Retry failed updates with conflict resolution

For detailed conflict resolution rules, see [PTDocs+_Offline_Sync_Conflict_Resolution.md](PTDocs+_Offline_Sync_Conflict_Resolution.md).

## Platform-Specific Considerations

### Android
- API base URL: `http://10.0.2.2:5170` (emulator → host machine)
- SecureStorage backed by Android Keystore
- Database path: `/data/data/com.ptdoc.app/files/ptdoc.db`

### iOS
- API base URL: `http://localhost:5170` (simulator)
- SecureStorage backed by iOS Keychain
- Database path: App sandbox documents directory

### macOS (Mac Catalyst)
- API base URL: `http://localhost:5170`
- SecureStorage backed by macOS Keychain
- Database path: `~/Library/Containers/com.ptdoc.app/Data/`

## Development Workflow

### Web Development
```bash
# No database setup required
dotnet run --project src/PTDoc.Web

# Web client connects to API
# Configure API base URL in appsettings.json or environment
```

### Device Development
```bash
# Run API server first
dotnet run --project src/PTDoc.Api --urls http://localhost:5170

# Run device app (connects to API + local SQLite)
dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

## Database Considerations

### Web (No Database)
- No `DbContext` registration
- No SQLite package references
- Authentication via cookies (server-managed sessions)

### Devices (SQLite)
- EF Core with SQLite provider
- Migrations applied on app startup
- Connection string from `appsettings.json` or environment variable

## API Contract

All platforms communicate with the API using:
- **JSON payloads** for data transfer
- **JWT tokens** (devices) or **cookies** (web) for authentication
- **RESTful endpoints** following OpenAPI specification
- **ISO 8601 timestamps** for date/time values

## Testing Strategy

- **Unit tests** - Use in-memory provider for Infrastructure tests
- **Integration tests** - Use SQLite with test database
- **E2E tests** - Test API + Web client separately from device clients
- **Device tests** - Test sync logic with mock API responses
