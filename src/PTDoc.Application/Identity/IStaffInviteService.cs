namespace PTDoc.Application.Identity;

/// <summary>
/// Coordinates invite-based staff onboarding while preserving PTDoc ownership of clinic and role assignments.
/// </summary>
public interface IStaffInviteService
{
    Task<StaffInviteSummary> CreateInviteAsync(
        StaffInviteCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<StaffInviteValidationResult> ValidateInviteAsync(
        string inviteToken,
        CancellationToken cancellationToken = default);

    Task<StaffInviteAcceptanceResult> AcceptInviteAsync(
        StaffInviteAcceptRequest request,
        CancellationToken cancellationToken = default);

    Task RevokeInviteAsync(
        Guid inviteId,
        CancellationToken cancellationToken = default);
}