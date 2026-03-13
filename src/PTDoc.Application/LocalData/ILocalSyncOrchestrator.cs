using PTDoc.Application.Sync;

namespace PTDoc.Application.LocalData;

/// <summary>
/// Orchestrates offline-first synchronization between the MAUI local encrypted database
/// and the central server API.
///
/// Sprint H responsibilities:
///  - Push locally-modified entities (SyncState.Pending) to the server.
///  - Pull server-side changes since the last pull watermark into the local database.
///  - Track per-entity-type sync watermarks via <see cref="Entities.LocalSyncMetadata"/>.
///  - Detect and safely mark conflicts without silent data loss.
///  - Preserve failed items for future retry.
/// </summary>
public interface ILocalSyncOrchestrator
{
    /// <summary>
    /// Push all locally-pending entities to the server.
    /// Entities are serialized and sent in a single batch request.
    /// On success each entity is marked <c>SyncState.Synced</c>;
    /// on failure the entity remains <c>SyncState.Pending</c> for the next retry.
    /// </summary>
    Task<LocalPushResult> PushPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pull entity changes from the server since the last recorded pull watermark.
    /// New or updated remote records are upserted into the local database.
    /// If a locally-pending record conflicts with a newer server version the local
    /// entity is marked <c>SyncState.Conflict</c> rather than silently overwritten.
    /// </summary>
    Task<LocalPullResult> PullChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a full sync cycle: push pending changes then pull server updates.
    /// </summary>
    Task<LocalSyncSummary> SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of local entities that have not yet been synced.
    /// </summary>
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a push operation from the MAUI local database to the server.</summary>
public class LocalPushResult
{
    /// <summary>Total number of locally-pending items that were submitted.</summary>
    public int PushedCount { get; init; }

    /// <summary>Number of items accepted by the server.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of items that failed (network error or server error); will be retried.</summary>
    public int FailedCount { get; init; }

    /// <summary>Number of items that were rejected due to a conflict.</summary>
    public int ConflictCount { get; init; }

    /// <summary>Non-PHI error descriptions for failed items.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>Result of a pull operation from the server into the MAUI local database.</summary>
public class LocalPullResult
{
    /// <summary>Total number of entity changes returned by the server.</summary>
    public int PulledCount { get; init; }

    /// <summary>Number of pulled entities successfully merged into the local database.</summary>
    public int AppliedCount { get; init; }

    /// <summary>Number of pulled entities that conflicted with locally-pending changes.</summary>
    public int ConflictCount { get; init; }

    /// <summary>Non-PHI error descriptions for items that could not be applied.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>Summary returned after a full sync cycle (push + pull).</summary>
public class LocalSyncSummary
{
    public required LocalPushResult Push { get; init; }
    public required LocalPullResult Pull { get; init; }
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Duration { get; init; }
}
