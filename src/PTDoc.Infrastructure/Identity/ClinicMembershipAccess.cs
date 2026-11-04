using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Identity;

public static class ClinicMembershipAccess
{
    public static IQueryable<ClinicMembership> ActiveMemberships(ApplicationDbContext db, Guid clinicId)
        => db.ClinicMemberships.Where(member => member.ClinicId == clinicId && member.IsActive
            && db.Clinics.Any(clinic => clinic.Id == clinicId && clinic.IsActive
                && db.Organizations.Any(organization => organization.Id == clinic.OrganizationId && organization.IsActive)
                && db.OrganizationMemberships.Any(staff => staff.OrganizationId == clinic.OrganizationId
                    && staff.UserId == member.UserId && staff.IsActive)));

    public static async Task<string?> ResolveRoleAsync(ApplicationDbContext db, User user, Guid? clinicId, CancellationToken cancellationToken = default)
    {
        if (!user.IsActive) return null;
        if (clinicId == Guid.Empty)
            return await db.OrganizationMemberships.AnyAsync(member => member.UserId == user.Id && member.IsActive
                && db.Organizations.Any(organization => organization.Id == member.OrganizationId && organization.IsActive), cancellationToken)
                ? "OrganizationMember" : null;
        if (!clinicId.HasValue) return user.ClinicId is null ? user.Role : null;
        var clinic = await db.Clinics.AsNoTracking().SingleOrDefaultAsync(item => item.Id == clinicId, cancellationToken);
        if (clinic is null || !clinic.IsActive) return null;
        // Unmigrated synthetic/legacy records retain only their original clinic scope.
        if (clinic.OrganizationId is null)
            return user.ClinicId == clinicId ? user.Role : null;
        return await (from membership in db.ClinicMemberships.AsNoTracking()
                      join staff in db.OrganizationMemberships on new { OrganizationId = clinic.OrganizationId.Value, membership.UserId }
                          equals new { staff.OrganizationId, staff.UserId }
                      join organization in db.Organizations on staff.OrganizationId equals organization.Id
                      where membership.ClinicId == clinicId && membership.UserId == user.Id && membership.IsActive
                          && staff.IsActive && organization.IsActive
                      select membership.Role).SingleOrDefaultAsync(cancellationToken);
    }
}
