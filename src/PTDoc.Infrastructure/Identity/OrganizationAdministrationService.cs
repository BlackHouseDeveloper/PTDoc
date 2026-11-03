using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Settings;

namespace PTDoc.Infrastructure.Identity;

public sealed class OrganizationAdministrationService(
    ApplicationDbContext db,
    IIdentityContextAccessor identity,
    TimeProvider clock) : IOrganizationAdministrationService
{
    private static readonly string[] ClinicRoles =
        [Roles.Owner, Roles.Admin, Roles.PT, Roles.PTA, Roles.FrontDesk, Roles.Billing, Roles.Aide, Roles.PracticeManager];

    public async Task<IReadOnlyList<OrganizationSummary>> GetOrganizationsAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await (from membership in db.OrganizationMemberships.AsNoTracking()
                      join organization in db.Organizations on membership.OrganizationId equals organization.Id
                      where membership.UserId == actor.Id && membership.IsActive && organization.IsActive
                      orderby organization.Name
                      select new OrganizationSummary(organization.Id, organization.Name, membership.Role))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationClinic>> GetClinicsAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, false, cancellationToken);
        return await db.Clinics.AsNoTracking().Where(item => item.OrganizationId == organizationId && item.IsActive)
            .OrderBy(item => item.Name).Select(item => new OrganizationClinic(item.Id, item.Name)).ToListAsync(cancellationToken);
    }

    public async Task<OrganizationClinic> CreateClinicAsync(Guid organizationId, CreateOrganizationClinic request, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
            || string.IsNullOrWhiteSpace(request.Slug) || request.Slug.Length > 100
            || request.Slug.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A name and a slug containing letters, numbers or hyphens are required.");
        if (string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 100
            || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId, out _))
            throw new ArgumentException("Choose a valid time zone.");
        var slug = request.Slug.ToLowerInvariant();
        if (await db.Clinics.AnyAsync(item => item.Slug == slug, cancellationToken))
            throw new OrganizationConflictException("That clinic slug is already in use.");
        var clinic = new Clinic { OrganizationId = organizationId, Name = request.Name.Trim(), Slug = slug, TimeZoneId = request.TimeZoneId };
        db.Clinics.Add(clinic);
        await SaveAsync(organizationId, "OrganizationClinicCreated", clinic.Id, cancellationToken);
        return new OrganizationClinic(clinic.Id, clinic.Name);
    }

    public async Task<IReadOnlyList<OrganizationStaff>> GetStaffAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, false, cancellationToken);
        return await (from membership in db.OrganizationMemberships.AsNoTracking()
                      join user in db.Users on membership.UserId equals user.Id
                      where membership.OrganizationId == organizationId
                      orderby user.LastName, user.FirstName
                      select new OrganizationStaff(user.Id, user.FirstName + " " + user.LastName, user.Email,
                          membership.Role, membership.IsActive, membership.Version)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicAccess>> GetStaffClinicsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, false, cancellationToken);
        await RequireStaffAsync(organizationId, userId, cancellationToken);
        return await (from membership in db.ClinicMemberships.AsNoTracking()
                      join clinic in db.Clinics on membership.ClinicId equals clinic.Id
                      where membership.UserId == userId && clinic.OrganizationId == organizationId
                      select new ClinicAccess(clinic.Id, clinic.Name, membership.Role, membership.IsActive, membership.Version))
            .ToListAsync(cancellationToken);
    }

    public async Task SetOrganizationMembershipAsync(Guid organizationId, Guid userId, OrganizationMembershipChange request, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, true, cancellationToken);
        if (!Enum.IsDefined(request.Role)) throw new ArgumentException("Invalid organization role.");
        var membership = await RequireStaffAsync(organizationId, userId, cancellationToken);
        if (membership.Version != request.ExpectedVersion) throw new OrganizationConflictException("Reload the staff list before saving.");
        if (membership.Role == OrganizationRole.Owner && membership.IsActive
            && (!request.IsActive || request.Role != OrganizationRole.Owner)
            && !await db.OrganizationMemberships.AnyAsync(item => item.OrganizationId == organizationId
                && item.UserId != userId && item.IsActive && item.Role == OrganizationRole.Owner
                && db.Users.Any(user => user.Id == item.UserId && user.IsActive), cancellationToken))
            throw new OrganizationConflictException("An organization must retain an active owner.");
        membership.Role = request.Role;
        membership.IsActive = request.IsActive;
        membership.Version++;
        // Retain clinic grants for audit/reinstatement, but every clinical request checks
        // active organization membership too. Changing this role never adds a clinic grant.
        await SaveAsync(organizationId, "OrganizationMembershipChanged", userId, cancellationToken);
    }

    public async Task SetClinicMembershipAsync(Guid organizationId, Guid clinicId, Guid userId, ClinicMembershipChange request, CancellationToken cancellationToken = default)
    {
        var actorRole = await AuthorizeAsync(organizationId, false, cancellationToken);
        var staff = await RequireStaffAsync(organizationId, userId, cancellationToken);
        if (!staff.IsActive) throw new OrganizationAccessException("The staff membership is inactive.");
        if (!ClinicRoles.Contains(request.Role, StringComparer.Ordinal)) throw new ArgumentException("Invalid clinic role.");
        if (!await db.Clinics.AnyAsync(item => item.Id == clinicId && item.OrganizationId == organizationId && item.IsActive, cancellationToken))
            throw new OrganizationAccessException("Clinic is not available in this organization.");
        var membership = await db.ClinicMemberships.SingleOrDefaultAsync(item => item.ClinicId == clinicId && item.UserId == userId, cancellationToken);
        if (actorRole != OrganizationRole.Owner
            && (staff.Role == OrganizationRole.Owner || request.Role is Roles.Owner or Roles.Admin || membership?.Role is Roles.Owner or Roles.Admin))
            throw new OrganizationAccessException("Only an organization owner can manage privileged clinic grants.");
        if ((membership?.Version ?? 0) != request.ExpectedVersion) throw new OrganizationConflictException("Reload clinic access before saving.");
        if (membership is null)
        {
            membership = new ClinicMembership { ClinicId = clinicId, UserId = userId };
            db.ClinicMemberships.Add(membership);
        }
        else membership.Version++;
        membership.Role = request.Role;
        membership.IsActive = request.IsActive;
        await SaveAsync(organizationId, "ClinicMembershipChanged", userId, cancellationToken);
    }

    public async Task<OrganizationInviteResult> InviteAsync(Guid organizationId, string email, CancellationToken cancellationToken = default)
    {
        await AuthorizeAsync(organizationId, false, cancellationToken);
        if (string.IsNullOrWhiteSpace(email) || email.Length > 255 || !new EmailAddressAttribute().IsValid(email))
            throw new ArgumentException("A valid staff email address is required.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId, Email = email.Trim().ToLowerInvariant(), TokenHash = Hash(token),
            ExpiresAtUtc = clock.GetUtcNow().UtcDateTime.AddHours(72), InvitedByUserId = identity.GetCurrentUserId()
        };
        db.OrganizationInvitations.Add(invitation);
        await SaveAsync(organizationId, "OrganizationInvitationCreated", invitation.Id, cancellationToken);
        return new OrganizationInviteResult(token, invitation.ExpiresAtUtc);
    }

    public async Task AcceptInvitationAsync(string token, CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token) || token.Length != 64) throw new OrganizationAccessException("Invitation is invalid or expired.");
        var hash = Hash(token);
        var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(item => item.TokenHash == hash, cancellationToken);
        if (invitation is null || invitation.AcceptedAtUtc.HasValue || invitation.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime
            || !string.Equals(invitation.Email, actor.Email?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new OrganizationAccessException("Invitation is invalid or expired, or belongs to another account.");
        var organization = await db.Organizations.SingleOrDefaultAsync(item => item.Id == invitation.OrganizationId && item.IsActive, cancellationToken);
        var inviterStillAuthorized = await db.OrganizationMemberships.AnyAsync(item => item.OrganizationId == invitation.OrganizationId
            && item.UserId == invitation.InvitedByUserId && item.IsActive && item.Role != OrganizationRole.Member
            && db.Users.Any(user => user.Id == item.UserId && user.IsActive), cancellationToken);
        if (organization is null || !inviterStillAuthorized) throw new OrganizationAccessException("Invitation is no longer available.");
        var membership = await db.OrganizationMemberships.SingleOrDefaultAsync(item => item.OrganizationId == organization.Id && item.UserId == actor.Id, cancellationToken);
        if (membership is not null) throw new OrganizationConflictException("You already have a membership. Ask the owner to manage it.");
        db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organization.Id, UserId = actor.Id });
        invitation.AcceptedAtUtc = clock.GetUtcNow().UtcDateTime;
        invitation.Version++;
        await SaveAsync(organization.Id, "OrganizationInvitationAccepted", invitation.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<ClinicAccess>> GetMyClinicsAsync(CancellationToken cancellationToken = default)
    {
        var actor = await ActorAsync(cancellationToken);
        return await (from membership in db.ClinicMemberships.AsNoTracking()
                      join clinic in db.Clinics on membership.ClinicId equals clinic.Id
                      join organization in db.Organizations on clinic.OrganizationId equals organization.Id
                      join staff in db.OrganizationMemberships on new { OrganizationId = organization.Id, membership.UserId }
                          equals new { staff.OrganizationId, staff.UserId }
                      where membership.UserId == actor.Id && membership.IsActive && clinic.IsActive && organization.IsActive && staff.IsActive
                      orderby clinic.Name
                      select new ClinicAccess(clinic.Id, clinic.Name, membership.Role, true, membership.Version)).ToListAsync(cancellationToken);
    }

    private async Task<User> ActorAsync(CancellationToken cancellationToken)
    {
        var actorId = identity.TryGetCurrentUserId();
        if (!actorId.HasValue) throw new OrganizationAccessException("Sign in to continue.");
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == actorId && item.IsActive, cancellationToken)
            ?? throw new OrganizationAccessException("Account is unavailable.");
    }

    private async Task<OrganizationRole> AuthorizeAsync(Guid organizationId, bool ownerOnly, CancellationToken cancellationToken)
    {
        var actor = await ActorAsync(cancellationToken);
        // Track the root version before reading authorization. SaveAsync updates it, so
        // concurrent access revocation or last-owner changes cause an atomic conflict.
        var organization = await db.Organizations.SingleOrDefaultAsync(item => item.Id == organizationId && item.IsActive, cancellationToken);
        var membership = await db.OrganizationMemberships.AsNoTracking().SingleOrDefaultAsync(item => item.OrganizationId == organizationId
            && item.UserId == actor.Id && item.IsActive, cancellationToken);
        if (organization is null || membership is null || membership.Role == OrganizationRole.Member
            || (ownerOnly && membership.Role != OrganizationRole.Owner))
            throw new OrganizationAccessException("Organization administration is not permitted.");
        return membership.Role;
    }

    private async Task<OrganizationMembership> RequireStaffAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
        => await db.OrganizationMemberships.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.UserId == userId, cancellationToken)
            ?? throw new OrganizationAccessException("Staff member is not in this organization.");

    private async Task SaveAsync(Guid organizationId, string eventType, Guid entityId, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleAsync(item => item.Id == organizationId, cancellationToken);
        organization.Version++;
        db.AuditLogs.Add(new AuditLog
        {
            EventType = eventType, UserId = identity.GetCurrentUserId(), EntityType = nameof(Organization), EntityId = entityId,
            TimestampUtc = clock.GetUtcNow().UtcDateTime, MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { organizationId })
        });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || AppointmentCommunicationProcessor.IsUniqueConstraintViolation(exception))
        {
            db.ChangeTracker.Clear();
            throw new OrganizationConflictException("Organization access changed. Reload and retry.");
        }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
