namespace PTDoc.Application.Compliance;

/// <summary>
/// Service for logging compliance and security events.
/// CRITICAL: Audit metadata must contain NO PHI.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs a compliance rule evaluation event.
    /// </summary>
    Task LogRuleEvaluationAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs a rule override event (PT-only, requires attestation).
    /// </summary>
    Task LogRuleOverrideAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs a note signature event.
    /// </summary>
    Task LogNoteSignedAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs an addendum creation event.
    /// </summary>
    Task LogAddendumCreatedAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

/// <summary>
/// Audit event with no PHI in metadata.
/// </summary>
public class AuditEvent
{
    public string EventType { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Metadata { get; set; } = new();

    // Additional fields to map to AuditLog
    public string Severity { get; set; } = "Info"; // "Info", "Warning", "Error"
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }

    public static AuditEvent RuleEvaluation(string ruleId, bool passed, Guid? userId = null)
    {
        return new AuditEvent
        {
            EventType = "RuleEvaluation",
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["RuleId"] = ruleId,
                ["Passed"] = passed,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent RuleOverride(string ruleId, string reason, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "RuleOverride",
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["RuleId"] = ruleId,
                ["Reason"] = reason,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent NoteSigned(Guid noteId, string noteType, string signatureHash, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "NoteSigned",
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["NoteType"] = noteType,
                ["SignatureHash"] = signatureHash,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent AddendumCreated(Guid noteId, Guid addendumId, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "AddendumCreated",
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["AddendumId"] = addendumId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }
}
