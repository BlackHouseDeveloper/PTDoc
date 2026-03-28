using System.ComponentModel.DataAnnotations;
using PTDoc.Core.Models;

namespace PTDoc.Application.DTOs;

// ─── Note Request DTOs ────────────────────────────────────────────────────────

/// <summary>Request DTO for creating a clinical note.</summary>
public sealed class CreateNoteRequest
{
    [Required]
    public Guid PatientId { get; set; }

    public Guid? AppointmentId { get; set; }

    [Required]
    public NoteType NoteType { get; set; }

    /// <summary>SOAP note content as a JSON string.</summary>
    public string ContentJson { get; set; } = "{}";

    [Required]
    public DateTime DateOfService { get; set; }

    /// <summary>CPT codes as a JSON array string.</summary>
    public string CptCodesJson { get; set; } = "[]";

    /// <summary>
    /// Total treatment time in minutes. When provided together with timed CPT codes, the
    /// 8-minute rule is validated and any advisory warning is included in the response.
    /// Omitting this field skips 8-minute rule validation.
    /// Sprint S: Used by the compliance rules engine to enforce Medicare billing requirements.
    /// </summary>
    public int? TotalMinutes { get; set; }
}

/// <summary>Request DTO for updating a draft clinical note.</summary>
public sealed class UpdateNoteRequest
{
    /// <summary>Updated SOAP note content as a JSON string.</summary>
    public string? ContentJson { get; set; }

    public DateTime? DateOfService { get; set; }

    /// <summary>CPT codes as a JSON array string.</summary>
    public string? CptCodesJson { get; set; }

    /// <summary>
    /// Total treatment time in minutes. When provided together with timed CPT codes, the
    /// 8-minute rule is validated and any advisory warning is included in the response.
    /// Omitting this field skips 8-minute rule validation.
    /// Sprint S: Used by the compliance rules engine to enforce Medicare billing requirements.
    /// </summary>
    public int? TotalMinutes { get; set; }
}

// ─── Objective Metric DTOs ────────────────────────────────────────────────────

/// <summary>Request DTO for creating an objective metric.</summary>
public sealed class CreateObjectiveMetricRequest
{
    [Required]
    public BodyPart BodyPart { get; set; }

    [Required]
    public MetricType MetricType { get; set; }

    [Required]
    [MaxLength(200)]
    public string Value { get; set; } = string.Empty;

    public bool IsWNL { get; set; }
}

/// <summary>Response DTO for an objective metric.</summary>
public sealed class ObjectiveMetricResponse
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }
    public BodyPart BodyPart { get; set; }
    public MetricType MetricType { get; set; }
    public string Value { get; set; } = string.Empty;
    public bool IsWNL { get; set; }
}

// ─── Note Response DTOs ───────────────────────────────────────────────────────

/// <summary>Response DTO for a clinical note.</summary>
public sealed class NoteResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    public NoteType NoteType { get; set; }
    public string ContentJson { get; set; } = "{}";
    public DateTime DateOfService { get; set; }
    public string? SignatureHash { get; set; }
    public DateTime? SignedUtc { get; set; }
    public Guid? SignedByUserId { get; set; }
    public string CptCodesJson { get; set; } = "[]";
    public Guid? ClinicId { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public IReadOnlyCollection<ObjectiveMetricResponse> ObjectiveMetrics { get; set; } = Array.Empty<ObjectiveMetricResponse>();
}

/// <summary>
/// Advisory compliance warning surfaced alongside a note operation.
/// Returned when a compliance rule fires at <c>Warning</c> severity (e.g., 8-minute rule).
/// A non-null ComplianceWarning does NOT block the operation — it is informational.
/// </summary>
public sealed class ComplianceWarning
{
    public string RuleId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}

/// <summary>
/// Unified response envelope for create and update note operations.
/// <c>ComplianceWarning</c> is non-null only when an advisory rule fired (e.g., 8-minute rule).
/// Sprint S: Replaces inconsistent anonymous wrapper shapes with a typed contract.
/// </summary>
public sealed class NoteOperationResponse
{
    public NoteResponse Note { get; set; } = null!;
    public ComplianceWarning? ComplianceWarning { get; set; }
}

/// <summary>
/// Lightweight list-item projection returned by GET /api/v1/notes.
/// Omits ContentJson and full ObjectiveMetrics to keep list responses small.
/// </summary>
public sealed class NoteListItemApiResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string NoteType { get; set; } = string.Empty;
    public bool IsSigned { get; set; }
    public DateTime DateOfService { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public string CptCodesJson { get; set; } = "[]";
}

/// <summary>
/// Request DTO for accepting AI-generated content into a specific section of a draft note.
///
/// This is the explicit clinician acceptance gate required by Sprint UC-Gamma:
/// AI output is NEVER persisted automatically — a clinician must call this endpoint
/// to write generated text into the note record.
/// </summary>
public sealed class AiSuggestionAcceptanceRequest
{
    /// <summary>
    /// The SOAP section to update (e.g., "assessment", "plan", "subjective", "objective").
    /// Case-insensitive; stored as lower-case in ContentJson.
    /// </summary>
    [Required]
    public string Section { get; set; } = string.Empty;

    /// <summary>
    /// The AI-generated text the clinician has reviewed and chosen to accept.
    /// </summary>
    [Required]
    public string GeneratedText { get; set; } = string.Empty;

    /// <summary>
    /// The type of generation (e.g., "Assessment", "Plan", "Goals").
    /// Used for audit logging only — not persisted in the note.
    /// </summary>
    [Required]
    public string GenerationType { get; set; } = string.Empty;
}
