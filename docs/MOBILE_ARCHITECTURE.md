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

```csharp
// MauiProgram.cs — local DB registration
builder.Services.AddDbContext<LocalDbContext>((sp, options) =>
{
    var key = sp.GetRequiredService<IDbKeyProvider>().GetKeyAsync().GetAwaiter().GetResult();
    var dbPath = Path.Combine(FileSystem.AppDataDirectory, "ptdoc_local.db");

    var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using var cmd = connection.CreateCommand();
    cmd.CommandText = "PRAGMA key = $key;";
    cmd.Parameters.AddWithValue("$key", key);
    cmd.ExecuteNonQuery();

    options.UseSqlite(connection);
}, ServiceLifetime.Singleton);
```

The `PRAGMA key` command must be executed on the connection **before** any EF Core queries run; this is why the connection is opened and authenticated manually before being passed to EF Core.

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
| `LocalDbContext` | EF Core + SQLCipher | Singleton |
| `ILocalRepository<T>` | `LocalRepository<T>` | Scoped |
| `ILocalDbInitializer` | `LocalDbInitializer` | Singleton |

`LocalDbContext` is registered as a **Singleton** because the underlying `SqliteConnection` must remain open to preserve the SQLCipher authentication state. A new EF Core context instance disposed would close the connection and lose the encryption context.

---

## HIPAA Considerations

- All patient data cached locally is encrypted at rest with AES-256 (SQLCipher)
- The encryption key is stored in hardware-backed secure enclave (Keychain / Keystore)
- `FileSystem.AppDataDirectory` is a sandboxed location inaccessible to other apps
- No PHI is written to application logs
- Cached data contains only summary fields (name, MRN, DOB) — no clinical note content

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
