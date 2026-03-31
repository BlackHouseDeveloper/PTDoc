namespace PTDoc.Application.Identity;

public enum RegistrationStatus
{
    PendingApproval,
    Succeeded,
    EmailAlreadyExists,
    UsernameCollision,
    InvalidPin,
    InvalidLicenseData,
    ClinicNotFound,
    ServerError
}

public sealed record RegistrationResult(
    RegistrationStatus Status,
    Guid? UserId,
    string? Error)
{
    public bool IsPending => Status == RegistrationStatus.PendingApproval;

    public bool Succeeded => Status == RegistrationStatus.PendingApproval || Status == RegistrationStatus.Succeeded;
}

public sealed record UserRegistrationRequest(
    string FullName,
    string Email,
    DateTime DateOfBirth,
    string RoleKey,
    Guid? ClinicId,
    string Pin,
    string? LicenseType,
    string? LicenseNumber,
    string? LicenseState);

public sealed record ClinicSummary(
    Guid Id,
    string Name);

public sealed record RoleSummary(
    string Key,
    string DisplayName);

public sealed record PendingUserSummary(
    Guid Id,
    string FullName,
    string Email,
    string Status,
    string RoleKey,
    Guid? ClinicId,
    string? ClinicName,
    DateTime RequestedAtUtc);

public sealed record PendingRegistrationsQuery(
    string? Search,
    string? Status,
    string? Role,
    string? Clinic,
    DateTime? FromDate,
    DateTime? ToDate,
    string? SortBy,
    int Page = 1,
    int PageSize = 25);

public sealed record PendingRegistrationsPage(
    IReadOnlyList<PendingUserSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);
