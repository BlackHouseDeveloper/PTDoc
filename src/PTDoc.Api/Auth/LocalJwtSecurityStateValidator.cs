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

        var roleClaims = principal.FindAll(ClaimTypes.Role)
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Value))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roleClaims.Length != 1
            || !string.Equals(roleClaims[0], user.Role, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var clinicClaims = principal.FindAll(HttpTenantContextAccessor.ClinicIdClaimType)
            .Where(claim => !string.IsNullOrWhiteSpace(claim.Value))
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (user.ClinicId is not { } clinicId)
        {
            return clinicClaims.Length == 0;
        }

        if (clinicClaims.Length != 1
            || !Guid.TryParse(clinicClaims[0], out var claimedClinicId)
            || claimedClinicId != clinicId)
        {
            return false;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!user.PinChangedAtUtc.HasValue
            && (!user.LegacyPinGraceEndsAtUtc.HasValue || user.LegacyPinGraceEndsAtUtc <= nowUtc))
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
            nowUtc);

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
