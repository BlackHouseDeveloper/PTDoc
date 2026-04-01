using System.Security.Claims;
using PTDoc.Application.Identity;

namespace PTDoc.Web.Auth;

public static class LogoutSessionClassifier
{
    private const string EntraAuthenticationType = "entra_jwt";

    public static bool RequiresExternalProviderSignOut(
        ClaimsPrincipal principal,
        bool externalIdentityEnabled)
    {
        if (!externalIdentityEnabled || principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var authenticationType = principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value;
        if (!string.IsNullOrWhiteSpace(authenticationType))
        {
            return string.Equals(authenticationType, EntraAuthenticationType, StringComparison.OrdinalIgnoreCase);
        }

        // Fallback for older sessions that may not carry the explicit auth type claim.
        return principal.HasClaim(static claim => claim.Type == PTDocClaimTypes.ExternalProvider);
    }
}
