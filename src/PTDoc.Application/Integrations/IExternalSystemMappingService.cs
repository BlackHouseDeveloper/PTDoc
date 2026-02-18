namespace PTDoc.Application.Integrations;

/// <summary>
/// Service for managing external system mappings.
/// Ensures unique constraint enforcement and prevents duplicate patient creation.
/// </summary>
public interface IExternalSystemMappingService
{
    /// <summary>
    /// Get or create a mapping for a patient to an external system.
    /// If mapping exists, returns existing mapping. Otherwise creates new mapping.
    /// </summary>
    Task<ExternalSystemMappingResult> GetOrCreateMappingAsync(
        Guid internalPatientId,
        string externalSystemName,
        string externalId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get existing mapping by external system and ID.
    /// </summary>
    Task<ExternalSystemMappingResult?> GetMappingByExternalIdAsync(
        string externalSystemName,
        string externalId,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all mappings for a patient.
    /// </summary>
    Task<List<ExternalSystemMappingResult>> GetPatientMappingsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of external system mapping operation.
/// </summary>
public class ExternalSystemMappingResult
{
    public Guid Id { get; set; }
    public string ExternalSystemName { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public Guid InternalPatientId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsNewMapping { get; set; } // True if just created, false if existing
}
