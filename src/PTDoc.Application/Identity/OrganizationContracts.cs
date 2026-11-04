using PTDoc.Core.Models;

namespace PTDoc.Application.Identity;

public sealed record OrganizationSummary(Guid Id, string Name, OrganizationRole Role);
public sealed record OrganizationClinic(Guid Id, string Name);
public sealed record OrganizationStaff(Guid UserId, string Name, string? Email, OrganizationRole Role, bool IsActive, long Version);
public sealed record ClinicAccess(Guid ClinicId, string ClinicName, string Role, bool IsActive, long Version);
public sealed record ClinicMembershipChange(string Role, bool IsActive, long ExpectedVersion);
public sealed record OrganizationMembershipChange(OrganizationRole Role, bool IsActive, long ExpectedVersion);
public sealed record OrganizationInviteResult(string Token, DateTime ExpiresAtUtc);
public sealed record CreateOrganizationClinic(string Name, string Slug, string TimeZoneId);
public sealed record InviteOrganizationStaff(string Email);
public sealed record AcceptOrganizationInvitation(string Token);

public interface IOrganizationAdministrationService
{
    Task<IReadOnlyList<OrganizationSummary>> GetOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationClinic>> GetClinicsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationClinic> CreateClinicAsync(Guid organizationId, CreateOrganizationClinic request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationStaff>> GetStaffAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClinicAccess>> GetStaffClinicsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
    Task SetOrganizationMembershipAsync(Guid organizationId, Guid userId, OrganizationMembershipChange request, CancellationToken cancellationToken = default);
    Task SetClinicMembershipAsync(Guid organizationId, Guid clinicId, Guid userId, ClinicMembershipChange request, CancellationToken cancellationToken = default);
    Task<OrganizationInviteResult> InviteAsync(Guid organizationId, string email, CancellationToken cancellationToken = default);
    Task AcceptInvitationAsync(string token, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClinicAccess>> GetMyClinicsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Request-scoped, server-selected authentication target. Never used as a data-access grant.</summary>
public sealed class ClinicSignInContext
{
    public Guid? ClinicId { get; set; }
    public string? Role { get; set; }
}

public sealed class OrganizationAccessException(string message) : Exception(message);
public sealed class OrganizationConflictException(string message) : Exception(message);
