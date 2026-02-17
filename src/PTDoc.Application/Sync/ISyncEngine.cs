using PTDoc.Core.Enums;

namespace PTDoc.Application.Sync;

/// <summary>
/// Interface for the offline sync engine that handles bidirectional synchronization.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Executes a sync cycle: pushes local changes and pulls remote changes.
    /// </summary>
    Task<SyncResult> SyncAsync(bool isManualTrigger = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pushes local changes to the server in the correct order.
    /// </summary>
    Task<PushResult> PushChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Pulls remote changes from the server.
    /// </summary>
    Task<PullResult> PullChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resolves a conflict for a specific entity.
    /// </summary>
    Task<ConflictResolutionResult> ResolveConflictAsync(Guid entityId, string entityType, ConflictResolutionStrategy strategy, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current sync status.
    /// </summary>
    Task<SyncStatus> GetSyncStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a sync operation.
/// </summary>
public class SyncResult
{
    public bool IsSuccessful { get; set; }
    public int EntitiesPushed { get; set; }
    public int EntitiesPulled { get; set; }
    public int ConflictsDetected { get; set; }
    public List<SyncError> Errors { get; set; } = new();
    public DateTime SyncStartedUtc { get; set; }
    public DateTime SyncCompletedUtc { get; set; }
}

/// <summary>
/// Result of pushing changes.
/// </summary>
public class PushResult
{
    public bool IsSuccessful { get; set; }
    public int EntitiesPushed { get; set; }
    public int EntitiesFailed { get; set; }
    public List<SyncError> Errors { get; set; } = new();
}

/// <summary>
/// Result of pulling changes.
/// </summary>
public class PullResult
{
    public bool IsSuccessful { get; set; }
    public int EntitiesPulled { get; set; }
    public int ConflictsDetected { get; set; }
    public List<SyncError> Errors { get; set; } = new();
}

/// <summary>
/// Sync error details.
/// </summary>
public class SyncError
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public bool IsConflict { get; set; }
}

/// <summary>
/// Current sync status.
/// </summary>
public class SyncStatus
{
    public bool IsSyncing { get; set; }
    public int PendingChanges { get; set; }
    public int UnresolvedConflicts { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public DateTime? NextScheduledSyncUtc { get; set; }
    public bool IsOnline { get; set; }
}

/// <summary>
/// Result of conflict resolution.
/// </summary>
public class ConflictResolutionResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public SyncState NewSyncState { get; set; }
}

/// <summary>
/// Strategy for resolving conflicts.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// Keep local version (last-write-wins for drafts).
    /// </summary>
    KeepLocal,
    
    /// <summary>
    /// Accept remote version.
    /// </summary>
    AcceptRemote,
    
    /// <summary>
    /// Reject the change (for signed entities).
    /// </summary>
    Reject,
    
    /// <summary>
    /// Create addendum (for signed notes).
    /// </summary>
    CreateAddendum,
    
    /// <summary>
    /// Manual merge required.
    /// </summary>
    ManualMerge
}
