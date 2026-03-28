namespace PTDoc.Application.Identity;

public interface IUserRegistrationService
{
    Task<RegistrationResult> RegisterAsync(
        UserRegistrationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClinicSummary>> GetActiveClinicListAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleSummary>> GetRegisterableRolesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingUserSummary>> GetPendingRegistrationsAsync(
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> ApproveRegistrationAsync(
        Guid userId,
        Guid approvedBy,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> RejectRegistrationAsync(
        Guid userId,
        Guid rejectedBy,
        CancellationToken cancellationToken = default);
}
