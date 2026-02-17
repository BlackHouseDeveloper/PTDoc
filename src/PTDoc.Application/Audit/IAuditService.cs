namespace PTDoc.Application.Audit;

/// <summary>
/// Interface for audit logging operations.
/// Ensures compliance tracking without storing PHI in logs.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an authentication event (login, logout, failed attempt).
    /// </summary>
    Task LogAuthenticationEventAsync(AuthenticationAuditEvent auditEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs a patient-related event (create, update, merge, etc.).
    /// Does not include PHI in the log entry.
    /// </summary>
    Task LogPatientEventAsync(PatientAuditEvent auditEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs a clinical note event (create, update, sign, etc.).
    /// </summary>
    Task LogNoteEventAsync(NoteAuditEvent auditEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs a sync event (push, pull, conflict).
    /// </summary>
    Task LogSyncEventAsync(SyncAuditEvent auditEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs an external integration event (payment, fax, HEP).
    /// </summary>
    Task LogIntegrationEventAsync(IntegrationAuditEvent auditEvent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves audit logs for a specific entity (for compliance review).
    /// </summary>
    Task<List<AuditLogEntry>> GetEntityAuditLogsAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Base audit event.
/// </summary>
public abstract class BaseAuditEvent
{
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public string? UserRole { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime EventTimestampUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Authentication audit event.
/// </summary>
public class AuthenticationAuditEvent : BaseAuditEvent
{
    public string EventType { get; set; } = string.Empty; // Login, Logout, FailedLogin, Lockout
    public bool IsSuccessful { get; set; }
    public string? FailureReason { get; set; }
    public bool IsLockedOut { get; set; }
}

/// <summary>
/// Patient audit event.
/// </summary>
public class PatientAuditEvent : BaseAuditEvent
{
    public Guid PatientId { get; set; }
    public string Action { get; set; } = string.Empty; // Create, Update, Merge, SoftDelete
    public string? AdditionalContext { get; set; }
}

/// <summary>
/// Clinical note audit event.
/// </summary>
public class NoteAuditEvent : BaseAuditEvent
{
    public Guid NoteId { get; set; }
    public string Action { get; set; } = string.Empty; // Create, Update, Sign, CoSign, Addendum
    public string NoteType { get; set; } = string.Empty;
    public string? SignatureHash { get; set; }
}

/// <summary>
/// Sync audit event.
/// </summary>
public class SyncAuditEvent : BaseAuditEvent
{
    public string Action { get; set; } = string.Empty; // Push, Pull, ConflictDetected, ConflictResolved
    public int EntityCount { get; set; }
    public bool HasConflict { get; set; }
    public string? ConflictDetails { get; set; }
}

/// <summary>
/// External integration audit event.
/// </summary>
public class IntegrationAuditEvent : BaseAuditEvent
{
    public string IntegrationName { get; set; } = string.Empty; // Payment, Fax, HEP
    public string Action { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public string? ExternalId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Audit log entry (for retrieval).
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventCategory { get; set; } = string.Empty;
    public DateTime EventTimestampUtc { get; set; }
    public string? Username { get; set; }
    public string? Action { get; set; }
    public string? Result { get; set; }
}
