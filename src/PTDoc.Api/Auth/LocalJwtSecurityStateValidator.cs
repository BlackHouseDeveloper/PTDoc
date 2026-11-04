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
        var authenticationType = principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value;
        var isLocalJwt = string.Equals(authenticationType, "pin_step_up_jwt", StringComparison.Ordinal)
            || string.Equals(authenticationType, "legacy_jwt", StringComparison.Ordinal);
        if (!isLocalJwt)
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

        var pinBindings = principal.FindAll(PTDocClaimTypes.PinCredentialBinding).ToArray();
        if (pinBindings.Length != 1 || pinBindings[0].Value != PinCredentialBinding.Create(user.PinHash))
        {
            return false;
        }

        var clinicClaims = principal.FindAll(HttpTenantContextAccessor.ClinicIdClaimType).ToArray();
        Guid? selectedClinicId = null;
        if (clinicClaims.Length > 1) return false;
        if (clinicClaims.Length == 1)
        {
            if (!Guid.TryParse(clinicClaims[0].Value, out var parsed)) return false;
            selectedClinicId = parsed;
        }
        var role = await ClinicMembershipAccess.ResolveRoleAsync(context, user, selectedClinicId, cancellationToken);
        var roleClaims = principal.FindAll(ClaimTypes.Role).ToArray();
        if (role is null || roleClaims.Length != 1 || roleClaims[0].Value != role) return false;
        var policyClinicId = selectedClinicId == Guid.Empty ? user.ClinicId : selectedClinicId;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (user.ClinicId.HasValue && !user.PinChangedAtUtc.HasValue
            && (!user.LegacyPinGraceEndsAtUtc.HasValue || user.LegacyPinGraceEndsAtUtc <= nowUtc))
        {
            return false;
        }

        var tokenHasMfaAssurance = principal.Claims.Any(claim =>
            (claim.Type == "amr" || claim.Type == PTDocClaimTypes.MappedAuthenticationMethodsReference)
            && string.Equals(claim.Value, "mfa", StringComparison.OrdinalIgnoreCase));
        var policy = policyClinicId.HasValue
            ? await context.ClinicSecurityPolicies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.ClinicId == policyClinicId.Value, cancellationToken)
            : null;
        var mfaIsRequired = policyClinicId.HasValue && MfaPolicyRules.RequiresMfa(
            policy,
            nowUtc);

        if (!tokenHasMfaAssurance && !mfaIsRequired)
        {
            return true;
        }

        var credential = await context.UserMfaCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.IsActive, cancellationToken);
        var bindings = principal.FindAll(PTDocClaimTypes.MfaCredentialBinding).ToArray();
        return tokenHasMfaAssurance && credential is not null && bindings.Length == 1
            && bindings[0].Value == MfaCredentialBinding.Create(credential.Id, credential.EncryptedSecret);
    }
}
