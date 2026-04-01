using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Lightweight cached patient record for offline-first MAUI access.
/// Contains only the fields required for list and summary views to minimise local storage size.
/// Full patient details should be fetched from the server when online.
/// </summary>
public class LocalPatientSummary : ILocalEntity
{
    /// <inheritdoc/>
    public int LocalId { get; set; }

    /// <inheritdoc/>
    public Guid ServerId { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /// <inheritdoc/>
    public SyncState SyncState { get; set; }

    /// <inheritdoc/>
    public DateTime LastModifiedUtc { get; set; }

    /// <inheritdoc/>
    public DateTime? LastSyncedUtc { get; set; }
}
