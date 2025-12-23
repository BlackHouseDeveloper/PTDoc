using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PTDoc.Models;

public class Insurance
{
    [Key]
    public int Id { get; set; }

    [Required]
    [ForeignKey(nameof(Patient))]
    public int PatientId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProviderName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string PolicyNumber { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? GroupNumber { get; set; }

    [MaxLength(200)]
    public string? SubscriberName { get; set; }

    public DateTime? EffectiveDate { get; set; }

    public DateTime? ExpirationDate { get; set; }

    [MaxLength(50)]
    public string InsuranceType { get; set; } = "Primary"; // Primary, Secondary, Other

    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime? LastModifiedDate { get; set; }

    // Navigation property
    public Patient Patient { get; set; } = null!;
}
