using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Lightweight cached intake form record for offline-first MAUI access.
/// Stores draft intake data locally so patients and clinicians can complete intake
/// forms offline; the draft is pushed to the server when reconnected.
/// </summary>
public class LocalIntakeFormDraft : ILocalEntity
{
    /// <inheritdoc/>
    public int LocalId { get; set; }

    /// <inheritdoc/>
    public Guid ServerId { get; set; }

    /// <summary>Server-side ID of the associated patient.</summary>
    public Guid PatientServerId { get; set; }

    /// <summary>Intake response data as a JSON blob.</summary>
    public string ResponseJson { get; set; } = "{}";

    /// <summary>Pain map data as a JSON blob.</summary>
    public string PainMapData { get; set; } = "{}";

    /// <summary>Consents data as a JSON blob.</summary>
    public string Consents { get; set; } = "{}";

    /// <summary>Version of the intake template used.</summary>
    public string TemplateVersion { get; set; } = "1.0";

    /// <summary>True when the intake has been submitted and locked on the server.</summary>
    public bool IsLocked { get; set; }

    /// <summary>UTC timestamp when the intake was submitted. Null until submitted.</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <inheritdoc/>
    public SyncState SyncState { get; set; }

    /// <inheritdoc/>
    public DateTime LastModifiedUtc { get; set; }

    /// <inheritdoc/>
    public DateTime? LastSyncedUtc { get; set; }
}
