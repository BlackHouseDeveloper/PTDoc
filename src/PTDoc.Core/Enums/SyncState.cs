namespace PTDoc.Core.Enums;

/// <summary>
/// Represents the synchronization state of an entity in the offline-first architecture.
/// </summary>
public enum SyncState
{
    /// <summary>
    /// Entity has local changes that need to be synchronized to the server.
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// Entity is synchronized with the server.
    /// </summary>
    Synced = 1,
    
    /// <summary>
    /// Entity has a synchronization conflict that requires manual resolution.
    /// </summary>
    Conflict = 2
}
