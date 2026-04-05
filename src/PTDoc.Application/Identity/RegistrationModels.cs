namespace PTDoc.Application.Identity;

public enum RegistrationStatus
{
    PendingApproval,
    Succeeded,
    ValidationFailed,
    NotFound,
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
    string? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public bool IsPending => Status == RegistrationStatus.PendingApproval;

    public bool Succeeded => Status == RegistrationStatus.Succeeded;
}

public sealed record UserRegistrationRequest(
    string FullName,
    string Email,
    DateTime DateOfBirth,
    string RoleKey,
    Guid? ClinicId,
    string Pin,
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
    DateTime RequestedAtUtc,
    bool CredentialsComplete,
    IReadOnlyList<string> MissingFields,
    string? LicenseNumber,
    string? LicenseState,
    string? ReviewedBy);

public sealed record PendingUserDetail(
    Guid Id,
    string Username,
    string FullName,
    string Email,
    DateTime? DateOfBirth,
    string Status,
    string RoleKey,
    Guid? ClinicId,
    string? ClinicName,
    DateTime RequestedAtUtc,
    bool CredentialsComplete,
    IReadOnlyList<string> MissingFields,
    string? LicenseNumber,
    string? LicenseState,
    string? ReviewedBy);

public sealed record AdminRegistrationUpdateRequest(
    string FullName,
    string Email,
    DateTime? DateOfBirth,
    string RoleKey,
    string? LicenseNumber,
    string? LicenseState);

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
