namespace PTDoc.Core.Models;

public sealed class StaffInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public Guid ClinicId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? LicenseType { get; set; }

    public string? LicenseNumber { get; set; }

    public string? LicenseState { get; set; }

    public Guid InvitedByUserId { get; set; }

    public Guid? LinkedUserId { get; set; }

    public bool RequiresExternalIdentityLink { get; set; }

    public StaffInviteStatus Status { get; set; } = StaffInviteStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? AcceptedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
}

public enum StaffInviteStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3
}