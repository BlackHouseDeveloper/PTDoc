namespace PTDoc.Application.Sync;

/// <summary>
/// A single entity change submitted by a MAUI client during a push operation.
/// Contains only non-PHI metadata plus the opaque entity JSON payload.
/// </summary>
public class ClientSyncPushItem
{
    /// <summary>Logical entity type, e.g. "Patient" or "Appointment".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Server-assigned UUID for an existing record.
    /// Use <see cref="Guid.Empty"/> for a record that has never been synced to the server.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>Local SQLite primary key, returned unchanged in the push response.</summary>
    public int LocalId { get; set; }

    /// <summary>Operation: "Create", "Update", or "Delete".</summary>
    public string Operation { get; set; } = "Update";

    /// <summary>Full JSON payload of the entity (may contain PHI – encrypted in transit via HTTPS).</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>UTC timestamp from the local record, used for last-write-wins conflict resolution.</summary>
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Batch push request from a MAUI client to the server.
/// </summary>
public class ClientSyncPushRequest
{
    public IList<ClientSyncPushItem> Items { get; set; } = new List<ClientSyncPushItem>();
}

/// <summary>
/// Per-item result of a client push operation.
/// </summary>
public class ClientSyncPushItemResult
{
    /// <summary>Echo of the client's local primary key.</summary>
    public int LocalId { get; set; }

    /// <summary>
    /// Server-assigned UUID. Populated for new records that were created on the server.
    /// Equals <see cref="ClientSyncPushItem.ServerId"/> for existing records.
    /// </summary>
    public Guid ServerId { get; set; }

    /// <summary>"Accepted", "Conflict", or "Error".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Non-PHI error description when <see cref="Status"/> is "Error" or "Conflict".</summary>
    public string? Error { get; set; }

    /// <summary>Server-side modification timestamp after the push was applied.</summary>
    public DateTime? ServerModifiedUtc { get; set; }
}

/// <summary>
/// Server response to a MAUI client push request.
/// </summary>
public class ClientSyncPushResponse
{
    public int AcceptedCount { get; set; }
    public int ConflictCount { get; set; }
    public int ErrorCount { get; set; }
    public IList<ClientSyncPushItemResult> Items { get; set; } = new List<ClientSyncPushItemResult>();
}

/// <summary>
/// A single entity change returned by the server during a pull operation.
/// </summary>
public class ClientSyncPullItem
{
    /// <summary>Logical entity type, e.g. "Patient" or "Appointment".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Server-assigned UUID.</summary>
    public Guid ServerId { get; set; }

    /// <summary>"Upsert" or "Delete".</summary>
    public string Operation { get; set; } = "Upsert";

    /// <summary>Full JSON payload of the entity.</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>UTC timestamp of the last server-side modification.</summary>
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>
/// Server response to a MAUI client pull request.
/// </summary>
public class ClientSyncPullResponse
{
    /// <summary>Entity changes since the requested watermark.</summary>
    public IList<ClientSyncPullItem> Items { get; set; } = new List<ClientSyncPullItem>();

    /// <summary>UTC timestamp at which the server snapshot was taken.</summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
}
