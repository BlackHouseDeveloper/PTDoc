namespace PTDoc.Core.Models;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// These roles administer staff metadata; they never authorize clinical requests.
public enum OrganizationRole { Member = 0, Administrator = 1, Owner = 2 }

public sealed class OrganizationMembership
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class ClinicMembership
{
    public Guid ClinicId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
}

public sealed class OrganizationInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid InvitedByUserId { get; set; }
    public long Version { get; set; } = 1;
}
