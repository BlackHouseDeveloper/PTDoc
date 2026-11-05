namespace PTDoc.Api.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using Microsoft.EntityFrameworkCore;
using PTDoc.Infrastructure.Data;
using System.Globalization;

public sealed class JwtTokenIssuer
{
    private readonly JwtOptions options;
    private readonly IRefreshTokenStore refreshTokenStore;
    private readonly TimeProvider timeProvider;
    private readonly ApplicationDbContext? db;
    private readonly IConfiguration? configuration;

    public JwtTokenIssuer(
        IOptions<JwtOptions> options,
        IRefreshTokenStore refreshTokenStore,
        TimeProvider timeProvider,
        ApplicationDbContext? db = null,
        IConfiguration? configuration = null)
    {
        this.options = options.Value;
        this.refreshTokenStore = refreshTokenStore;
        this.timeProvider = timeProvider;
        this.db = db;
        this.configuration = configuration;
    }

    public async Task<TokenResponse> IssueAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(options.AccessTokenMinutes);

        foreach (var claim in identity.FindAll(PTDocClaimTypes.SubscriptionPaidThrough).ToArray()) identity.RemoveClaim(claim);
        if (db is not null && Guid.TryParse(identity.FindFirst("clinic_id")?.Value, out var clinicId))
        {
            var subscription = await db.IndividualSubscriptions.AsNoTracking().SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
            if (subscription is not null)
            {
                var through = subscription.Environment == configuration?["Subscriptions:AuthorizeNet:Environment"]
                    ? subscription.PaidThroughUtc : null;
                identity.AddClaim(new Claim(PTDocClaimTypes.SubscriptionPaidThrough,
                    new DateTimeOffset(DateTime.SpecifyKind(through ?? DateTime.MinValue, DateTimeKind.Utc)).ToString("O", CultureInfo.InvariantCulture)));
            }
        }

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
        var subject = identity.FindFirst(PTDocClaimTypes.InternalUserId)?.Value
                  ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
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
