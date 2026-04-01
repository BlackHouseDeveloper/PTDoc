namespace PTDoc.Core.Models;

public sealed class ExternalIdentityMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Provider { get; set; } = string.Empty;

    public string ExternalSubject { get; set; } = string.Empty;

    public string PrincipalType { get; set; } = string.Empty;

    public Guid InternalEntityId { get; set; }

    public Guid? TenantId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}