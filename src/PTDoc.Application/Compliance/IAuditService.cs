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

    /// <summary>
    /// Logs a note edit event (content or CPT codes changed on a draft note).
    /// </summary>
    Task LogNoteEditedAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs an authentication event (login success/failure, logout, token validation failure).
    /// CRITICAL: Must NOT include PIN, raw tokens, or PHI in metadata.
    /// </summary>
    Task LogAuthEventAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs an AI generation attempt event.
    /// NO PHI — only generation type, model, and note identity metadata.
    /// </summary>
    Task LogAiGenerationAttemptAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs clinician acceptance of AI-generated content.
    /// NO PHI — only generation type and note identity metadata.
    /// </summary>
    Task LogAiGenerationAcceptedAsync(AuditEvent auditEvent, CancellationToken ct = default);
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

    public static AuditEvent NoteEdited(Guid noteId, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "NoteEdited",
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event for a successful login.
    /// NO PIN, password, or PHI in metadata.
    /// </summary>
    public static AuditEvent LoginSuccess(Guid userId, string? ipAddress)
    {
        return new AuditEvent
        {
            EventType = "LoginSuccess",
            Severity = "Info",
            Success = true,
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["IpAddress"] = ipAddress ?? "unknown",
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event for a failed login attempt.
    /// NO PIN, password, or PHI in metadata.
    /// </summary>
    public static AuditEvent LoginFailed(string? ipAddress, string reason)
    {
        return new AuditEvent
        {
            EventType = "LoginFailed",
            Severity = "Warning",
            Success = false,
            ErrorMessage = reason,
            Metadata = new Dictionary<string, object>
            {
                ["IpAddress"] = ipAddress ?? "unknown",
                ["Reason"] = reason,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event for a logout action.
    /// </summary>
    public static AuditEvent Logout(Guid userId)
    {
        return new AuditEvent
        {
            EventType = "Logout",
            Severity = "Info",
            Success = true,
            UserId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event for a bearer token validation failure.
    /// NO raw token value in metadata.
    /// </summary>
    public static AuditEvent TokenValidationFailed(string? ipAddress, string reason)
    {
        return new AuditEvent
        {
            EventType = "TokenValidationFailed",
            Severity = "Warning",
            Success = false,
            ErrorMessage = reason,
            Metadata = new Dictionary<string, object>
            {
                ["IpAddress"] = ipAddress ?? "unknown",
                ["Reason"] = reason,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event for an AI generation attempt.
    /// NO PHI — only generation type, model, and note identity.
    /// </summary>
    public static AuditEvent AiGenerationAttempt(Guid noteId, string generationType, string model, Guid? userId)
    {
        return new AuditEvent
        {
            EventType = "AiGenerationAttempt",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["GenerationType"] = generationType,
                ["Model"] = model,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event when a clinician accepts AI-generated content.
    /// NO PHI — only generation type and note identity.
    /// </summary>
    public static AuditEvent AiGenerationAccepted(Guid noteId, string generationType, Guid? userId)
    {
        return new AuditEvent
        {
            EventType = "AiGenerationAccepted",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["GenerationType"] = generationType,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }
}
