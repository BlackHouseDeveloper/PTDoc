namespace PTDoc.Application.Integrations;

/// <summary>
/// Configuration for the Wibbi/Physiotec HEP integration.
/// </summary>
public sealed class WibbiHepOptions
{
    public const string SectionName = "Integrations:Hep";

    public bool Enabled { get; set; }

    public bool PatientLaunchEnabled { get; set; }

    public bool ClinicianAssignmentEnabled { get; set; }

    public string BaseUrl { get; set; } = "https://v4.api.wibbi.com";

    public string ApiUsername { get; set; } = string.Empty;

    public string ApiPassword { get; set; } = string.Empty;

    public string Entity { get; set; } = string.Empty;

    public string ClinicLicenseId { get; set; } = string.Empty;

    public bool AllowCredentialBearingRedirects { get; set; }

    public TimeSpan TokenRefreshSkew { get; set; } = TimeSpan.FromMinutes(1);
}
