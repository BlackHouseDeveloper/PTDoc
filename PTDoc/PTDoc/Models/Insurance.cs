namespace PTDoc.Models;

/// <summary>
/// Represents insurance information for a patient.
/// </summary>
public sealed class Insurance : Entity
{
    /// <summary>
    /// Gets or sets the patient identifier associated with this insurance.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Gets or sets the insurance provider name.
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy number.
    /// </summary>
    public string PolicyNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the group number.
    /// </summary>
    public string? GroupNumber { get; set; }

    /// <summary>
    /// Gets or sets the subscriber name.
    /// </summary>
    public string? SubscriberName { get; set; }

    /// <summary>
    /// Gets or sets the effective date of the insurance.
    /// </summary>
    public DateTime? EffectiveDate { get; set; }

    /// <summary>
    /// Gets or sets the expiration date of the insurance.
    /// </summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets the insurance type (Primary, Secondary, Other).
    /// </summary>
    public string InsuranceType { get; set; } = "Primary";

    /// <summary>
    /// Gets or sets the patient associated with this insurance.
    /// </summary>
    public Patient Patient { get; set; } = null!;
}
