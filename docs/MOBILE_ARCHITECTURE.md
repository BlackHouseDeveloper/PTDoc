# PTDoc MAUI Mobile Architecture

## Overview

The `PTDoc.Maui` project is the cross-platform mobile/desktop client for iOS, Android, and macOS. It uses .NET MAUI Blazor Hybrid to share Blazor UI components from `PTDoc.UI` while adding device-specific capabilities such as secure storage and local persistence.

This document covers the local encrypted SQLite architecture introduced in **Sprint D**.

---

## Local Encrypted SQLite Database (Sprint D)

### Purpose

The MAUI client maintains a device-local encrypted SQLite database that:

- Allows clinicians to access cached patient and appointment data without an active network connection
- Serves as the source of truth for locally generated changes until they are synced to the server
- Stores only the lightweight summaries needed for list and detail views — not full clinical records
- Enables conflict detection when local changes diverge from server changes

### Storage Location

| Platform | Path |
|----------|------|
| Android | `<App Data Directory>/ptdoc_local.db` |
| iOS | `<App Data Directory>/ptdoc_local.db` |
| macOS | `<App Data Directory>/ptdoc_local.db` |

`FileSystem.AppDataDirectory` resolves to the platform's private application data container that is excluded from backups and inaccessible to other apps.

### Encryption

The local database is encrypted with **SQLCipher** (via `SQLitePCLRaw.bundle_e_sqlcipher`). SQLCipher applies AES-256 encryption to every page of the SQLite database file.

#### Encryption Key Management

The per-device encryption key is:

1. **Generated** once using `System.Security.Cryptography.RandomNumberGenerator` (32 cryptographically random bytes, Base64-encoded → 44 characters)
2. **Stored** in the platform's hardware-backed secure storage
   - iOS: Keychain (via `Microsoft.Maui.Storage.SecureStorage`)
   - Android: Android Keystore (via `Microsoft.Maui.Storage.SecureStorage`)
   - macOS: Keychain
3. **Retrieved** on subsequent launches from secure storage

The key is **never hardcoded**, never logged, and never committed to source control.

#### Key Provider

```
PTDoc.Application.Security.IDbKeyProvider      ← interface
PTDoc.Maui.Security.SecureStorageDbKeyProvider ← MAUI implementation
PTDoc.Infrastructure.Security.EnvironmentDbKeyProvider ← API/server implementation
```

`SecureStorageDbKeyProvider` is registered as a **Singleton** in `MauiProgram.cs` and is injected into the `LocalDbContext` factory at startup.

If SecureStorage is unavailable (rare, typically on device with no hardware key store), the provider throws an `InvalidOperationException` (fail-closed) and the application falls back to online-only mode.

#### SQLCipher Initialisation

The `SqliteConnection` is registered as a **Singleton** (it holds the SQLCipher auth state). `LocalDbContext` is registered as **Scoped** so each DI scope (UI component, background task) gets its own EF Core context instance. EF Core does not dispose connections it does not own, so the shared Singleton connection remains open across all context lifetimes.

```csharp
// MauiProgram.cs — local DB registration

// 1. Ensure SQLCipher is the active native provider before any connection is created.
SqliteProviderBootstrapper.EnsureInitialized();

// 2. Singleton SqliteConnection — opens the file with the SQLCipher password once.
builder.Services.AddSingleton<SqliteConnection>(sp =>
{
    var key = Task.Run(() => sp.GetRequiredService<IDbKeyProvider>().GetKeyAsync())
                  .GetAwaiter().GetResult();
    var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ptdoc_local.db");

    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = dbPath,
        Password = key
    }.ToString();

    var connection = new SqliteConnection(connectionString);
    connection.Open();

    return connection;
});

// 3. Scoped LocalDbContext — each scope shares the authenticated connection.
builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
{
    options.UseSqlite(sp.GetRequiredService<SqliteConnection>());
}, ServiceLifetime.Scoped);
```

The `Task.Run()` wrapper avoids a `SynchronizationContext` deadlock: `SecureStorage.GetAsync` marshals back to the MAUI platform dispatcher, which can deadlock when awaited synchronously on the main thread. `Task.Run()` schedules the work on a thread-pool thread with no captured dispatcher context.

The SQLCipher password must be applied **before** any EF Core queries run; this is why the connection is built with `SqliteConnectionStringBuilder.Password` and opened before being passed to EF Core.

---

## Local Data Entities

Local entities are lightweight cache records containing only the fields needed for offline display. They are defined in `PTDoc.Application/LocalData/Entities/` and implement `ILocalEntity`.

### `ILocalEntity`

All local entities implement this interface:

```csharp
public interface ILocalEntity
{
    int LocalId { get; set; }         // SQLite auto-increment PK
    Guid ServerId { get; set; }       // Server-side UUID (zero if never synced)
    SyncState SyncState { get; set; } // Pending | Synced | Conflict
    DateTime LastModifiedUtc { get; set; }
    DateTime? LastSyncedUtc { get; set; }
}
```

### Entity Summary

| Entity | Purpose |
|--------|---------|
| `LocalUserProfile` | Cached identity of the authenticated user for offline display |
| `LocalPatientSummary` | Cached patient name, MRN, and contact info for list views |
| `LocalAppointmentSummary` | Cached appointment schedule for daily/weekly views |
| `LocalSyncMetadata` | Per-entity-type sync watermarks and continuation tokens |

---

## Offline Synchronisation Scaffolding

### Sync State Lifecycle

```text
New local record ──► SyncState.Pending
                         │
                    (sync succeeds)
                         │
                         ▼
                  SyncState.Synced
                         │
              (server has newer version)
                         │
                         ▼
                  SyncState.Conflict ──► manual resolution
```

### Tracking What Needs Sync

`ILocalRepository<T>.GetPendingSyncAsync()` returns all records where `SyncState == Pending` or `SyncState == Conflict`. The offline sync engine (Sprint E) will consume this list when pushing changes to the server.

### `LocalSyncMetadata`

The `LocalSyncMetadata` table stores one row per entity type and records:

- `LastPulledAt` — when the device last fetched fresh data for this type
- `LastPushedAt` — when the device last pushed local changes for this type
- `SyncToken` — server-provided cursor for incremental pull operations
- `PendingCount` — number of unsynchronised records (for UI badge display)

---

## Project Architecture

```
PTDoc.Application/
  LocalData/
    ILocalEntity.cs              ← marker interface for all local entities
    ILocalRepository.cs          ← generic CRUD + sync query contract
    ILocalDbInitializer.cs       ← startup initialisation contract
    Entities/
      LocalUserProfile.cs
      LocalPatientSummary.cs
      LocalAppointmentSummary.cs
      LocalSyncMetadata.cs

PTDoc.Infrastructure/
  LocalData/
    LocalDbContext.cs             ← EF Core context for local SQLite
    LocalRepository.cs            ← generic EF Core repository implementation
    LocalDbInitializer.cs         ← EnsureCreated startup initialiser

PTDoc.Maui/
  Security/
    SecureStorageDbKeyProvider.cs ← platform key provider (iOS Keychain / Android Keystore)
  App.xaml.cs                     ← calls ILocalDbInitializer.InitializeAsync() at startup
  MauiProgram.cs                  ← DI registration for all local DB services
```

---

## DI Registration Summary

| Service | Implementation | Lifetime |
|---------|---------------|---------|
| `IDbKeyProvider` | `SecureStorageDbKeyProvider` | Singleton |
| `SqliteConnection` | Platform-authenticated SQLCipher connection | Singleton |
| `LocalDbContext` | EF Core + SQLCipher (uses shared connection) | Scoped |
| `ILocalRepository<T>` | `LocalRepository<T>` | Scoped |
| `ILocalDbInitializer` | `LocalDbInitializer` | Singleton |

The `SqliteConnection` is a **Singleton** because it holds the SQLCipher authentication state. Closing and reopening it would require re-authenticating with the encryption key.

`LocalDbContext` is **Scoped** — each DI scope (UI component, background task) gets its own EF Core context instance, which is thread-safe. All instances share the Singleton connection; EF Core does not dispose connections it does not own, so the connection remains open and authenticated across context lifetimes.

### Startup Initialisation Order

`App.LocalDbInitTask` is a public `Task` field set on construction. Components that require local data access before the first render should `await App.LocalDbInitTask` to ensure the schema has been created before issuing their first query.

---

## User and clinic cache partitions (Plan 05)

Every local entity, queued sync operation and sync watermark has a nullable
`ScopeKey`. Runtime MAUI uses a singleton `LocalDataScope` with a key composed of
canonical user and selected-clinic GUIDs. Each `LocalDbContext` applies that key at
query time and rejects writes from a previous partition. Administration-only and
anonymous sessions have no usable clinical cache scope.

The scope lease covers repository and complete sync request/write operations.
Authentication restore, token refresh, sign-in replacement and explicit logout
serialize with that lease. Sync marks its HTTP requests as already holding the
lease so the authenticated handler does not acquire it twice. On a native HTTP
authentication failure, the handler clears credentials while holding the lease;
the next authentication-state read becomes anonymous. It does not call back into
UI logout while sync holds the lease. Cached drafts remain intact.

### Upgrading an existing device

The initializer adds nullable scope columns and changes watermark uniqueness to
`(ScopeKey, EntityType)` without deleting data. Previously unscoped rows stay null
and are hidden from ordinary queries and sync. They are **not automatically
recovered** by this release. Before upgrading devices with pending work, sync that
work in the original single-clinic release and confirm server persistence.

If a device is already upgraded with unsynced legacy work, preserve the encrypted
database and its hardware-backed key. Do not clear storage, reinstall, or assign
all null rows to the next login. An authorized recovery must establish the original
user, verify each patient's owning clinic against the server, and copy only verified
drafts/queue rows into that user's clinic partition on a protected copy first.
Unverifiable rows must remain quarantined for manual review. Reset/recreate that
partition's pull watermarks from the server rather than borrowing old global ones.
There is no end-user recovery UI in this implementation; unresolved device recovery
is a rollout blocker for that device, not permission to discard pending clinical work.

Local test construction may omit `LocalDataScope` for existing single-partition
fixtures; production MAUI always registers it. New coverage exercises partition
isolation, pending-draft retention, lease ordering and authentication failure during
sync. Native build/install and live clinic-switch testing remain required.

---

## HIPAA Considerations

- All patient data cached locally is encrypted at rest with AES-256 (SQLCipher)
- The encryption key is stored in hardware-backed secure enclave (Keychain / Keystore)
- `FileSystem.AppDataDirectory` is a sandboxed location inaccessible to other apps
- No PHI is written to application logs
- Cached summaries and unsynced clinical-note/intake drafts are sensitive clinical data; all partitions remain encrypted at rest.

---

## When to Consult This Document

- Implementing offline data access in MAUI components
- Adding new local entity types for caching
- Debugging SQLCipher initialisation failures
- Extending the sync scaffolding to push/pull from the server
- Reviewing the encryption key lifecycle

## See Also

- [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) — Clean Architecture layers
- [`docs/SYNC_ENGINE.md`](SYNC_ENGINE.md) — Server-side sync engine
- [`docs/SECURITY.md`](SECURITY.md) — Overall security posture
- [`docs/RUNTIME_TARGETS.md`](RUNTIME_TARGETS.md) — Web vs MAUI platform differences
