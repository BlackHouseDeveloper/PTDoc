using System.ComponentModel.DataAnnotations;

namespace PTDoc.Application.DTOs;

// ─── Intake Request DTOs ──────────────────────────────────────────────────────

/// <summary>Request DTO for creating an intake response per TDD §5.2.</summary>
public sealed class CreateIntakeRequest
{
    [Required]
    public Guid PatientId { get; set; }

    /// <summary>Body-region pain map data as a JSON string. Maps to TDD PainMapData.</summary>
    public string PainMapData { get; set; } = "{}";

    /// <summary>Patient consents (HIPAA, treatment) as a JSON string. Maps to TDD Consents.</summary>
    public string Consents { get; set; } = "{}";

    /// <summary>Optional full response payload as JSON.</summary>
    public string ResponseJson { get; set; } = "{}";

    /// <summary>Template version identifier.</summary>
    [MaxLength(50)]
    public string TemplateVersion { get; set; } = "1.0";
}

/// <summary>Request DTO for updating an existing intake draft.</summary>
public sealed class UpdateIntakeRequest
{
    /// <summary>Body-region pain map data as a JSON string.</summary>
    public string PainMapData { get; set; } = "{}";

    /// <summary>Patient consents as a JSON string.</summary>
    public string Consents { get; set; } = "{}";

    /// <summary>Optional full response payload as JSON.</summary>
    public string ResponseJson { get; set; } = "{}";

    /// <summary>Template version identifier.</summary>
    [MaxLength(50)]
    public string TemplateVersion { get; set; } = "1.0";
}

/// <summary>Request DTO for revoking one or more intake consent authorizations based on written notice.</summary>
public sealed class RevokeIntakeConsentRequest
{
    /// <summary>Consent keys to revoke (for example: hipaaAcknowledged, communicationEmailConsent).</summary>
    [Required]
    public List<string> ConsentKeys { get; set; } = new();

    /// <summary>Must be true to confirm written revocation was received before applying changes.</summary>
    public bool WrittenRevocationReceived { get; set; }

    /// <summary>Optional non-PHI reference for the written revocation artifact (ticket/document id).</summary>
    [MaxLength(100)]
    public string? WrittenRequestReference { get; set; }
}

// ─── Intake Response DTOs ─────────────────────────────────────────────────────

/// <summary>Response DTO for an intake form (aligned with TDD IntakeResponse contract).</summary>
public sealed class IntakeResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PainMapData { get; set; } = "{}";
    public string Consents { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";

    /// <summary>True when the intake is locked after initial evaluation.</summary>
    public bool Locked { get; set; }

    public string TemplateVersion { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
    public Guid? ClinicId { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>Response DTO describing current intake consent revocation state and revocation audit history.</summary>
public sealed class IntakeConsentRevocationHistoryResponse
{
    public Guid IntakeId { get; set; }
    public bool WrittenRevocationReceived { get; set; }
    public DateTime? LastRevocationAtUtc { get; set; }
    public List<string> RevokedConsentKeys { get; set; } = new();
    public List<IntakeConsentRevocationAuditEntryResponse> AuditEntries { get; set; } = new();
}

/// <summary>One revocation audit log entry for intake consents.</summary>
public sealed class IntakeConsentRevocationAuditEntryResponse
{
    public DateTime TimestampUtc { get; set; }
    public Guid? UserId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public bool HasWrittenReference { get; set; }
    public List<string> ConsentKeys { get; set; } = new();
}

/// <summary>Paged response for intake consent revocation audit history.</summary>
public sealed class IntakeConsentRevocationTimelineResponse
{
    public Guid IntakeId { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public string? CorrelationId { get; set; }
    public List<IntakeConsentRevocationAuditEntryResponse> Entries { get; set; } = new();
}

/// <summary>Current communication channel consent eligibility derived from intake consent state.</summary>
public sealed class IntakeCommunicationConsentEligibilityResponse
{
    public Guid IntakeId { get; set; }
    public bool CallAllowed { get; set; }
    public bool TextAllowed { get; set; }
    public bool EmailAllowed { get; set; }
    public bool AnyChannelAllowed { get; set; }
    public string? CommunicationPhoneNumber { get; set; }
    public string? CommunicationEmail { get; set; }
    public DateTime? LastRevocationAtUtc { get; set; }
    public List<string> RevokedConsentKeys { get; set; } = new();
}

/// <summary>Current specialty-treatment consent eligibility derived from intake consent state.</summary>
public sealed class IntakeSpecialtyConsentEligibilityResponse
{
    public Guid IntakeId { get; set; }
    public bool DryNeedlingAllowed { get; set; }
    public bool PelvicFloorAllowed { get; set; }
    public bool AnySpecialtyAllowed { get; set; }
    public DateTime? LastRevocationAtUtc { get; set; }
    public List<string> RevokedConsentKeys { get; set; } = new();
}

/// <summary>PHI release authorization eligibility derived from intake consent state.</summary>
public sealed class IntakePhiReleaseEligibilityResponse
{
    public Guid IntakeId { get; set; }
    public bool PhiReleaseAllowed { get; set; }
    public DateTime? LastRevocationAtUtc { get; set; }
    public List<string> RevokedConsentKeys { get; set; } = new();
}

/// <summary>Credit card authorization eligibility derived from intake consent state.</summary>
public sealed class IntakeCreditCardAuthorizationEligibilityResponse
{
    public Guid IntakeId { get; set; }
    public bool CreditCardAuthorizationAllowed { get; set; }
    public DateTime? LastRevocationAtUtc { get; set; }
    public List<string> RevokedConsentKeys { get; set; } = new();
}

/// <summary>Per-consent readiness entry for the completeness check.</summary>
public sealed class IntakeConsentCompletenessItemResponse
{
    public string ConsentKey { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public bool Revoked { get; set; }
    public bool Ready => Accepted && !Revoked;
}

/// <summary>Structured completeness report: all required consents present and not revoked before treatment.</summary>
public sealed class IntakeConsentCompletenessResponse
{
    public Guid IntakeId { get; set; }
    /// <summary>True when every required consent is accepted and not revoked.</summary>
    public bool IsComplete { get; set; }
    public List<string> MissingConsentKeys { get; set; } = new();
    public List<string> RevokedConsentKeys { get; set; } = new();
    public List<IntakeConsentCompletenessItemResponse> Items { get; set; } = new();
}
