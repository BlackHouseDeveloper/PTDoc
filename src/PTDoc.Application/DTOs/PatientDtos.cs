using System.ComponentModel.DataAnnotations;

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
    public DateTime DateOfBirth { get; set; }
}
