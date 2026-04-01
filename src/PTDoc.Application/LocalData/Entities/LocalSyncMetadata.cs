namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Per-entity-type synchronisation watermark stored in the local database.
/// Tracks the last pull and push timestamps for each entity type so that
/// incremental sync fetches only the changes made since the last successful sync.
/// </summary>
public class LocalSyncMetadata
{
    /// <summary>Auto-incremented local primary key.</summary>
    public int Id { get; set; }

    /// <summary>
    /// Logical entity type name this metadata tracks, e.g. "Patient", "Appointment".
    /// Unique per row.
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last successful pull from the server for this entity type.</summary>
    public DateTime? LastPulledAt { get; set; }

    /// <summary>UTC timestamp of the last successful push to the server for this entity type.</summary>
    public DateTime? LastPushedAt { get; set; }

    /// <summary>
    /// Opaque continuation token returned by the server after a pull.
    /// Passed back on the next pull request for cursor-based pagination.
    /// </summary>
    public string? SyncToken { get; set; }

    /// <summary>Number of local records currently in a Pending or Conflict sync state.</summary>
    public int PendingCount { get; set; }
}
