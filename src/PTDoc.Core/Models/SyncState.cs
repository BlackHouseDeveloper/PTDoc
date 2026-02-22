namespace PTDoc.Core.Models;

/// <summary>
/// Represents the synchronization state of an entity in the offline-first architecture.
/// </summary>
public enum SyncState
{
    /// <summary>
    /// Entity has local changes that need to be synced to the server
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Entity is in sync with the server
    /// </summary>
    Synced = 1,

    /// <summary>
    /// Entity has a conflict that needs manual resolution
    /// </summary>
    Conflict = 2
}
