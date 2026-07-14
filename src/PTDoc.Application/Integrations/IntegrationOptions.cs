namespace PTDoc.Application.Integrations;

public sealed class IntegrationFeatureOptions
{
    public const string SectionName = "Integrations:Features";

    public bool EnableHumbleFax { get; set; }
    public bool EnableHumbleInboundFax { get; set; }
    public bool EnableWibbiProvisioning { get; set; }
    public bool EnableWibbiProgramPublishing { get; set; }
    public bool EnableWibbiTrackingSync { get; set; }
}

public sealed class HumbleFaxOptions
{
    public const string SectionName = "Integrations:Fax";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.humblefax.com";
    public int MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    public int RateLimitInstanceBudget { get; set; } = 4;
}

public sealed class IntegrationDocumentStoreOptions
{
    public const string SectionName = "Integrations:DocumentStore";

    public string ContainerName { get; set; } = "integration-documents";
    public string? DevelopmentPath { get; set; }
    public int MaxFileBytes { get; set; } = 50 * 1024 * 1024;
}

public sealed class IntegrationWorkerOptions
{
    public const string SectionName = "Integrations:Worker";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 10;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
}

public sealed class IntegrationDocumentScannerOptions
{
    public const string SectionName = "Integrations:DocumentScanner";

    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3310;
    public int MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);
}
