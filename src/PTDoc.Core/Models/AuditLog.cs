using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents an audit log entry for compliance and traceability.
/// No PHI should be stored in log entries.
/// </summary>
public class AuditLog : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    // Event details
    public string EventType { get; set; } = string.Empty;
    public string EventCategory { get; set; } = string.Empty; // Auth, Patient, Note, Sync, etc.
    public DateTime EventTimestampUtc { get; set; }
    
    // User context
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? UserRole { get; set; }
    
    // Entity context (no PHI)
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
    
    // Action details
    public string? Action { get; set; }
    public string? Result { get; set; } // Success, Failure, PartialSuccess
    
    // Additional context (structured JSON, no PHI)
    public string? ContextJson { get; set; }
    
    // Error details (if applicable)
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    
    // Request metadata (for API requests)
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
    
    // Compliance flags
    public bool IsSensitiveAction { get; set; }
    public bool RequiresRetention { get; set; }
    
    // Audit timestamps
    public DateTime CreatedAt { get; set; }
}
