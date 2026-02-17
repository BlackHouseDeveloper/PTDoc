namespace PTDoc.Core.Models;

/// <summary>
/// Represents an audit log entry for compliance and security tracking.
/// Must NOT contain PHI - use structured event types and correlation IDs.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Timestamp
    public DateTime TimestampUtc { get; set; }
    
    // Event classification
    public string EventType { get; set; } = string.Empty; // "Login", "NoteSign", "PatientAccess", etc.
    public string Severity { get; set; } = "Info"; // "Info", "Warning", "Error"
    
    // Context
    public Guid? UserId { get; set; }
    public string? EntityType { get; set; } // "Patient", "ClinicalNote", etc.
    public Guid? EntityId { get; set; }
    
    // Correlation for tracing
    public string CorrelationId { get; set; } = string.Empty;
    
    // Additional metadata (JSON, NO PHI)
    public string MetadataJson { get; set; } = "{}";
    
    // Result
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}
