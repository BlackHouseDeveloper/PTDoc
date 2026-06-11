using System.ComponentModel.DataAnnotations;

namespace PTDoc.Application.DTOs;

public sealed class PatientDocumentResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? Notes { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; }
}

public sealed class UploadPatientDocumentRequest
{
    [Required]
    [MaxLength(80)]
    public string DocumentType { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? ContentType { get; set; }

    [Required]
    public string Base64Content { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

public sealed class PatientCommunicationLogEntryResponse
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? ContactName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}

public sealed class CreatePatientCommunicationLogEntryRequest
{
    [Required]
    [MaxLength(40)]
    public string Channel { get; set; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string Direction { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Summary { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Details { get; set; }

    [MaxLength(120)]
    public string? ContactName { get; set; }

    public DateTime? OccurredAtUtc { get; set; }
}
