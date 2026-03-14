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
}

/// <summary>Request DTO for updating a draft clinical note.</summary>
public sealed class UpdateNoteRequest
{
    /// <summary>Updated SOAP note content as a JSON string.</summary>
    public string? ContentJson { get; set; }

    public DateTime? DateOfService { get; set; }

    /// <summary>CPT codes as a JSON array string.</summary>
    public string? CptCodesJson { get; set; }
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
    public IEnumerable<ObjectiveMetricResponse> ObjectiveMetrics { get; set; } = Enumerable.Empty<ObjectiveMetricResponse>();
}
