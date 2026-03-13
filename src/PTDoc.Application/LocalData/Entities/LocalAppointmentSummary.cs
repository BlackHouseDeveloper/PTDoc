using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Lightweight cached appointment record for offline-first MAUI access.
/// Supports display of the clinician's schedule when the device is offline.
/// </summary>
public class LocalAppointmentSummary : ILocalEntity
{
    /// <inheritdoc/>
    public int LocalId { get; set; }

    /// <inheritdoc/>
    public Guid ServerId { get; set; }

    /// <summary>Server-side ID of the associated patient.</summary>
    public Guid PatientServerId { get; set; }

    public string PatientFirstName { get; set; } = string.Empty;
    public string PatientLastName { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }

    /// <inheritdoc/>
    public SyncState SyncState { get; set; }

    /// <inheritdoc/>
    public DateTime LastModifiedUtc { get; set; }

    /// <inheritdoc/>
    public DateTime? LastSyncedUtc { get; set; }
}
