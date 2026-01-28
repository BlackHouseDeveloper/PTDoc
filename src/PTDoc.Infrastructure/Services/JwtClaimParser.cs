namespace PTDoc.Infrastructure.Services;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

public static class JwtClaimParser
{
    public static ClaimsPrincipal CreatePrincipal(string token)
    {
        var claims = ParseClaims(token);
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    public static IReadOnlyCollection<Claim> ParseClaims(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var claims = jwt.Claims.ToList();

            if (!claims.Any(c => c.Type == ClaimTypes.Name) &&
                claims.FirstOrDefault(c => c.Type == "name") is { } nameClaim)
            {
                claims.Add(new Claim(ClaimTypes.Name, nameClaim.Value));
            }

            if (!claims.Any(c => c.Type == ClaimTypes.Email) &&
                claims.FirstOrDefault(c => c.Type == "email") is { } emailClaim)
            {
                claims.Add(new Claim(ClaimTypes.Email, emailClaim.Value));
            }

            if (!claims.Any(c => c.Type == ClaimTypes.NameIdentifier) &&
                claims.FirstOrDefault(c => c.Type == "sub") is { } subClaim)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, subClaim.Value));
            }

            return claims;
        }
        catch
        {
            return Array.Empty<Claim>();
        }
    }
}