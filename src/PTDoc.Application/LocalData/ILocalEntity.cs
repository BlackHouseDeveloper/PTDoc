using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData;

/// <summary>
/// Marker interface for entities persisted in the MAUI local encrypted SQLite database.
/// All local entities carry sync metadata that enables offline-first conflict resolution.
/// </summary>
public interface ILocalEntity
{
    /// <summary>
    /// Auto-incremented primary key for local SQLite storage.
    /// </summary>
    int LocalId { get; set; }

    /// <summary>
    /// Corresponding server-side UUID. Zero when the record has never been synced.
    /// </summary>
    Guid ServerId { get; set; }

    /// <summary>
    /// Current synchronisation state of this local record.
    /// </summary>
    SyncState SyncState { get; set; }

    /// <summary>
    /// UTC timestamp of the last modification (local or server).
    /// Used for last-write-wins conflict resolution.
    /// </summary>
    DateTime LastModifiedUtc { get; set; }

    /// <summary>
    /// UTC timestamp of the last successful sync with the server. Null if never synced.
    /// </summary>
    DateTime? LastSyncedUtc { get; set; }
}
