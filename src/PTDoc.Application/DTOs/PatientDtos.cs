using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PTDoc.Application.DTOs;

// ─── Patient Request DTOs ─────────────────────────────────────────────────────

/// <summary>Request DTO for creating a new patient.</summary>
public sealed class CreatePatientRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required]
    public DateTime DateOfBirth { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? AddressLine1 { get; set; }

    [MaxLength(200)]
    public string? AddressLine2 { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? ZipCode { get; set; }

    [MaxLength(50)]
    public string? MedicalRecordNumber { get; set; }

    /// <summary>Payer/insurance information as a JSON string.</summary>
    public string? PayerInfoJson { get; set; }

    // ─── New clinical fields ───────────────────────────────────────────────────
    public string? ReferringPhysician { get; set; }

    [MaxLength(10)]
    public string? PhysicianNpi { get; set; }

    public DateTime? DateOfOnset { get; set; }
    public string? AuthorizationNumber { get; set; }
    public string? EmergencyContactName { get; set; }

    [MaxLength(20)]
    public string? EmergencyContactPhone { get; set; }

    public bool ConsentSigned { get; set; }
    public DateTime? ConsentSignedDate { get; set; }
}

/// <summary>Request DTO for updating an existing patient.</summary>
public sealed class UpdatePatientRequest
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? AddressLine1 { get; set; }

    [MaxLength(200)]
    public string? AddressLine2 { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? ZipCode { get; set; }

    [MaxLength(50)]
    public string? MedicalRecordNumber { get; set; }

    /// <summary>Payer/insurance information as a JSON string.</summary>
    public string? PayerInfoJson { get; set; }

    public string? ReferringPhysician { get; set; }

    [MaxLength(10)]
    public string? PhysicianNpi { get; set; }

    public DateTime? DateOfOnset { get; set; }
    public string? AuthorizationNumber { get; set; }
    public string? EmergencyContactName { get; set; }

    [MaxLength(20)]
    public string? EmergencyContactPhone { get; set; }

    public bool? ConsentSigned { get; set; }
    public DateTime? ConsentSignedDate { get; set; }

    public bool? IsArchived { get; set; }
}

// ─── Patient Response DTOs ────────────────────────────────────────────────────

/// <summary>Response DTO representing a patient record.</summary>
public sealed class PatientResponse
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string PayerInfoJson { get; set; } = "{}";
    public string? ReferringPhysician { get; set; }
    public string? PhysicianNpi { get; set; }
    public DateTime? DateOfOnset { get; set; }
    public string? AuthorizationNumber { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public bool ConsentSigned { get; set; }
    public DateTime? ConsentSignedDate { get; set; }
    public string DiagnosisCodesJson { get; set; } = "[]";
    public bool IsArchived { get; set; }
    public Guid? ClinicId { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

/// <summary>Lightweight patient projection for clinician selection UIs.</summary>
public sealed class PatientListItemResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public bool IsArchived { get; set; }
}

// ─── Diagnosis DTOs ───────────────────────────────────────────────────────────

/// <summary>
/// Represents a single ICD-10 diagnosis code attached to a patient.
/// Stored as a JSON array in Patient.DiagnosisCodesJson.
/// </summary>
public sealed class PatientDiagnosisDto
{
    [JsonPropertyName("code")]
    public string IcdCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}
