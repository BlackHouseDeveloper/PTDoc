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
    /// Logs a legal signature event.
    /// </summary>
    Task LogSignatureEventAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Logs a signature verification event.
    /// </summary>
    Task LogSignatureVerificationAsync(AuditEvent auditEvent, CancellationToken ct = default);

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

    /// <summary>
    /// Logs an intake workflow event (submitted, locked, or clinician reviewed).
    /// NO PHI — only intake identity and action metadata.
    /// </summary>
    Task LogIntakeEventAsync(AuditEvent auditEvent, CancellationToken ct = default);
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

    public static AuditEvent OverrideApplied(Guid noteId, ComplianceRuleType ruleType, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "OVERRIDE_APPLIED",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["ruleType"] = ruleType.ToString(),
                ["timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent HardStopTriggered(Guid noteId, ComplianceRuleType ruleType, Guid? userId)
    {
        return new AuditEvent
        {
            EventType = "HARD_STOP_TRIGGERED",
            Severity = "Warning",
            Success = false,
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["ruleType"] = ruleType.ToString(),
                ["timestamp"] = DateTime.UtcNow
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

    public static AuditEvent SignatureAction(string eventType, Guid noteId, Guid? userId, bool success = true, string? errorMessage = null)
    {
        return new AuditEvent
        {
            EventType = eventType,
            UserId = userId,
            Success = success,
            ErrorMessage = errorMessage,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["Action"] = eventType,
                ["NoteId"] = noteId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent AddendumCreated(Guid noteId, Guid addendumId, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "ADDENDUM_CREATE",
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["AddendumId"] = addendumId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    public static AuditEvent EditBlockedSignedNote(Guid noteId, Guid? userId, string source)
    {
        return new AuditEvent
        {
            EventType = "EDIT_BLOCKED_SIGNED_NOTE",
            Severity = "Warning",
            Success = false,
            UserId = userId,
            EntityType = "ClinicalNote",
            EntityId = noteId,
            Metadata = new Dictionary<string, object>
            {
                ["NoteId"] = noteId,
                ["Source"] = source,
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
    /// NO PHI — only generation type, model, and (optionally) note identity.
    /// </summary>
    /// <param name="noteId">Note being authored. Pass <c>null</c> when no note ID is available for the endpoint.</param>
    /// <param name="generationType">Short label such as "Assessment", "Plan", or "Goals".</param>
    /// <param name="model">AI model name, e.g. "gpt-4".</param>
    /// <param name="userId">Authenticated user performing the generation.</param>
    /// <param name="success">Whether the generation succeeded.</param>
    /// <param name="errorMessage">Optional error description when <paramref name="success"/> is false.</param>
    public static AuditEvent AiGenerationAttempt(
        Guid? noteId,
        string generationType,
        string model,
        Guid? userId,
        bool success = true,
        string? errorMessage = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["GenerationType"] = generationType,
            ["Model"] = model,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (noteId.HasValue)
        {
            metadata["NoteId"] = noteId.Value;
        }

        return new AuditEvent
        {
            EventType = "AiGenerationAttempt",
            Severity = success ? "Info" : "Warning",
            Success = success,
            ErrorMessage = errorMessage,
            UserId = userId,
            EntityType = noteId.HasValue ? "ClinicalNote" : null,
            EntityId = noteId,
            Metadata = metadata
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

    /// <summary>
    /// Creates an audit event when a patient or staff member submits an intake form.
    /// NO PHI — only intake identity and submitter identity.
    /// </summary>
    public static AuditEvent IntakeSubmitted(Guid intakeId, Guid userId, Dictionary<string, object>? additionalMetadata = null)
    {
        var metadata = new Dictionary<string, object>
        {
            ["IntakeId"] = intakeId,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (additionalMetadata is not null)
        {
            foreach (var pair in additionalMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        return new AuditEvent
        {
            EventType = "IntakeSubmitted",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "IntakeForm",
            EntityId = intakeId,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates an audit event when an intake form is locked by staff.
    /// NO PHI — only intake identity and actor identity.
    /// </summary>
    public static AuditEvent IntakeLocked(Guid intakeId, Guid userId)
    {
        return new AuditEvent
        {
            EventType = "IntakeLocked",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "IntakeForm",
            EntityId = intakeId,
            Metadata = new Dictionary<string, object>
            {
                ["IntakeId"] = intakeId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event when a clinician reviews a submitted intake form.
    /// NO PHI — only intake identity and reviewer identity.
    /// </summary>
    public static AuditEvent IntakeReviewed(Guid intakeId, Guid reviewerId)
    {
        return new AuditEvent
        {
            EventType = "IntakeReviewed",
            Severity = "Info",
            Success = true,
            UserId = reviewerId,
            EntityType = "IntakeForm",
            EntityId = intakeId,
            Metadata = new Dictionary<string, object>
            {
                ["IntakeId"] = intakeId,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }

    /// <summary>
    /// Creates an audit event when one or more intake consent permissions are revoked in writing.
    /// NO PHI — metadata includes consent key names and reference presence only.
    /// </summary>
    public static AuditEvent IntakeConsentRevoked(
        Guid intakeId,
        Guid userId,
        IReadOnlyCollection<string> consentKeys,
        bool hasWrittenReference)
    {
        return new AuditEvent
        {
            EventType = "IntakeConsentRevoked",
            Severity = "Info",
            Success = true,
            UserId = userId,
            EntityType = "IntakeForm",
            EntityId = intakeId,
            Metadata = new Dictionary<string, object>
            {
                ["IntakeId"] = intakeId,
                ["ConsentKeys"] = consentKeys.ToArray(),
                ["ConsentKeyCount"] = consentKeys.Count,
                ["HasWrittenReference"] = hasWrittenReference,
                ["Timestamp"] = DateTime.UtcNow
            }
        };
    }
}
