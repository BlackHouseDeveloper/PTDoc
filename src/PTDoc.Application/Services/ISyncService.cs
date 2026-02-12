namespace PTDoc.Application.Services;

/// <summary>
/// Service for managing data synchronization state and operations
/// Supports offline-first architecture with local SQLite persistence
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Gets the timestamp of the last successful sync operation
    /// </summary>
    DateTime? LastSyncTime { get; }

    /// <summary>
    /// Gets whether a sync operation is currently in progress
    /// </summary>
    bool IsSyncing { get; }

    /// <summary>
    /// Event raised when sync state changes (LastSyncTime or IsSyncing)
    /// </summary>
    event Action? OnSyncStateChanged;

    /// <summary>
    /// Initialize sync service - loads persisted sync state
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Perform manual sync operation (patient and physician data)
    /// Updates LastSyncTime on success
    /// </summary>
    /// <returns>True if sync succeeded, false otherwise</returns>
    Task<bool> SyncNowAsync();

    /// <summary>
    /// Get elapsed time since last sync in formatted string (e.g., "3m 25s", "1h 4m")
    /// </summary>
    string GetElapsedTimeSinceSync();
}
