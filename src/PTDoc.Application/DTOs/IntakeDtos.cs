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
