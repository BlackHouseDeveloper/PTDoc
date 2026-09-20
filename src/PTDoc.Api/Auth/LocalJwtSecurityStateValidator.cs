using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Auth;

internal sealed class LocalJwtSecurityStateValidator(
    ApplicationDbContext context,
    TimeProvider timeProvider)
{
    internal async Task<bool> IsValidAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value,
                "pin_step_up_jwt",
                StringComparison.Ordinal))
        {
            return true;
        }

        var userIdValue = principal.FindFirst(PTDocClaimTypes.InternalUserId)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return false;
        }

        var user = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.MustChangePin)
        {
            return false;
        }

        if (user.ClinicId is not { } clinicId)
        {
            return true;
        }

        if (!Guid.TryParse(
                principal.FindFirst(HttpTenantContextAccessor.ClinicIdClaimType)?.Value,
                out var claimedClinicId)
            || claimedClinicId != clinicId)
        {
            return false;
        }

        var tokenHasMfaAssurance = principal.FindAll("amr").Any(claim =>
            string.Equals(claim.Value, "mfa", StringComparison.OrdinalIgnoreCase));
        var policy = await context.ClinicSecurityPolicies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        var mfaIsRequired = MfaPolicyRules.RequiresMfa(
            policy,
            timeProvider.GetUtcNow().UtcDateTime);

        if (!tokenHasMfaAssurance && !mfaIsRequired)
        {
            return true;
        }

        var credentialIsActive = await context.UserMfaCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.IsActive, cancellationToken);
        return tokenHasMfaAssurance && credentialIsActive;
    }
}
