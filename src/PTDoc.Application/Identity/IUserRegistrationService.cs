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

    Task<PendingRegistrationsPage> GetPendingRegistrationsAsync(
        PendingRegistrationsQuery query,
        CancellationToken cancellationToken = default);

    Task<PendingUserDetail?> GetPendingRegistrationAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> UpdatePendingRegistrationAsync(
        Guid userId,
        AdminRegistrationUpdateRequest request,
        Guid editedBy,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> ApproveRegistrationAsync(
        Guid userId,
        Guid approvedBy,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> RejectRegistrationAsync(
        Guid userId,
        Guid rejectedBy,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> HoldRegistrationAsync(
        Guid userId,
        Guid heldBy,
        CancellationToken cancellationToken = default);

    Task<RegistrationResult> CancelRegistrationAsync(
        Guid userId,
        Guid cancelledBy,
        CancellationToken cancellationToken = default);
}
