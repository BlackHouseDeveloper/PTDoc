namespace PTDoc.Application.Configuration;

/// <summary>
/// Configuration options for patient record retention policy.
/// Binds to the "Retention" section in appsettings.
/// </summary>
public class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>
    /// Default number of years patient records are retained before archival review.
    /// Can be overridden per-patient via Patient.RetentionYears.
    /// Defaults to 7 years per HIPAA minimum requirements.
    /// </summary>
    public int DefaultRetentionYears { get; set; } = 7;
}
