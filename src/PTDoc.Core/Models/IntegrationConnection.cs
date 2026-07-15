namespace PTDoc.Core.Models;

public static class IntegrationProviders
{
    public const string HumbleFax = "HumbleFax";
    public const string Wibbi = "Wibbi";
}

/// <summary>
/// Clinic-scoped configuration for an external integration. SecretReference points to
/// runtime configuration/secret-manager keys; credential values are never persisted.
/// </summary>
public sealed class IntegrationConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string SecretReference { get; set; } = string.Empty;
    public string? WebhookTokenHash { get; set; }
    public DateTime? ComplianceApprovedAtUtc { get; set; }
    public Guid? ComplianceApprovedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastVerifiedAtUtc { get; set; }
    public string? LastHealthCode { get; set; }

    public Clinic? Clinic { get; set; }
}

/// <summary>
/// Maps provider objects to PTDoc objects within one clinic connection.
/// </summary>
public sealed class IntegrationExternalMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid InternalEntityId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAtUtc { get; set; }

    public Clinic? Clinic { get; set; }
    public IntegrationConnection? IntegrationConnection { get; set; }
}
