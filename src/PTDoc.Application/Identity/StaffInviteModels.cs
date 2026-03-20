namespace PTDoc.Application.Identity;

public sealed record StaffInviteCreateRequest(
    string Email,
    Guid ClinicId,
    string Role,
    Guid InvitedByUserId,
    bool RequiresExternalIdentityLink,
    string? LicenseType,
    string? LicenseNumber,
    string? LicenseState);

public sealed record StaffInviteSummary(
    Guid InviteId,
    string Email,
    Guid ClinicId,
    string Role,
    bool RequiresExternalIdentityLink,
    DateTimeOffset ExpiresAtUtc,
    string Status);

public sealed record StaffInviteValidationResult(
    bool IsValid,
    StaffInviteSummary? Invite,
    string? Error);

public sealed record StaffInviteAcceptRequest(
    Guid InviteId,
    string FullName,
    DateTime DateOfBirth,
    string Email,
    string LicenseType,
    string LicenseNumber,
    string LicenseState,
    string? Username,
    string? Pin,
    string? ExternalSubject,
    string? ExternalProvider);

public sealed record StaffInviteAcceptanceResult(
    bool Succeeded,
    Guid? UserId,
    string? Error);