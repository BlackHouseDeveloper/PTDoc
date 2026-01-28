namespace PTDoc.Api.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Application.Auth;

public sealed class JwtTokenIssuer
{
    private readonly JwtOptions options;
    private readonly IRefreshTokenStore refreshTokenStore;
    private readonly TimeProvider timeProvider;

    public JwtTokenIssuer(
        IOptions<JwtOptions> options,
        IRefreshTokenStore refreshTokenStore,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.refreshTokenStore = refreshTokenStore;
        this.timeProvider = timeProvider;
    }

    public async Task<TokenResponse> IssueAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(options.AccessTokenMinutes);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: identity.Claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);

        var refreshToken = GenerateRefreshToken();
        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? identity.FindFirst(ClaimTypes.Name)?.Value
                      ?? "unknown";

        await refreshTokenStore.StoreAsync(
            refreshToken,
            new RefreshTokenRecord(
                subject,
                identity.Claims.ToList(),
                now.AddDays(options.RefreshTokenDays)),
            cancellationToken);

        return new TokenResponse(accessToken, refreshToken, expires);
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}