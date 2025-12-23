using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PTDoc.Models;

public class SOAPNote
{
    [Key]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Patient))]
    public int PatientId { get; set; }

    [Required]
    public DateTime VisitDate { get; set; } = DateTime.UtcNow;

    // Subjective
    [MaxLength(2000)]
    public string? Subjective { get; set; }

    // Objective
    [MaxLength(2000)]
    public string? Objective { get; set; }

    // Assessment
    [MaxLength(2000)]
    public string? Assessment { get; set; }

    // Plan
    [MaxLength(2000)]
    public string? Plan { get; set; }

    [MaxLength(100)]
    public string? DiagnosisCode { get; set; }

    [MaxLength(100)]
    public string? TreatmentCode { get; set; }

    public int? DurationMinutes { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastModifiedDate { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public bool IsCompleted { get; set; } = false;

    // Navigation property
    public Patient Patient { get; set; } = null!;
}
