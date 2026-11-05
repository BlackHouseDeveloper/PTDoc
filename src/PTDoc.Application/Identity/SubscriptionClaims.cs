using System.Globalization;
using System.Security.Claims;

namespace PTDoc.Application.Identity;

public static class SubscriptionClaims
{
    public static DateTimeOffset? PaidThrough(ClaimsPrincipal principal)
    {
        var claims = principal.FindAll(PTDocClaimTypes.SubscriptionPaidThrough).ToArray();
        if (claims.Length == 0) return null; // Existing organization accounts are unchanged.
        return claims.Length == 1 && DateTimeOffset.TryParseExact(claims[0].Value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var value) ? value : DateTimeOffset.MinValue;
    }
}
