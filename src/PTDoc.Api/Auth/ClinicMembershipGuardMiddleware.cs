using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Auth;

/// <summary>Rechecks live membership for all authentication schemes before authorization.</summary>
public sealed class ClinicMembershipGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, PrincipalRecordResolver resolver, ApplicationDbContext db)
    {
        if (http.User.Identity?.IsAuthenticated != true || http.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(http);
            return;
        }
        var principal = resolver.GetProvisioningResult();
        if (principal.InternalUserId is { } userId)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, http.RequestAborted);
            var role = user is null ? null : await ClinicMembershipAccess.ResolveRoleAsync(db, user, principal.ClinicId, http.RequestAborted);
            if (role is null)
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (principal.ClinicId == Guid.Empty
                && !http.Request.Path.StartsWithSegments("/api/v1/organizations")
                && !http.Request.Path.StartsWithSegments("/api/v1/admin/registrations")
                && !http.Request.Path.StartsWithSegments("/api/v1/subscriptions"))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            // Entra role claims also cannot confer more authority than the selected
            // clinic's current membership. Never merge organization roles into this list.
            foreach (var identity in http.User.Identities)
                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToArray()) identity.RemoveClaim(claim);
            var primaryIdentity = (ClaimsIdentity)http.User.Identity;
            primaryIdentity.AddClaim(new Claim(primaryIdentity.RoleClaimType, role));
        }
        await next(http);
    }
}
