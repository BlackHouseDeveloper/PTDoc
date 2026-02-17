namespace PTDoc.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;

    // Enterprise security: Short-lived access tokens for HIPAA compliance
    public int AccessTokenMinutes { get; init; } = 15;

    // Refresh tokens expire after 7 days (reduced from 30 for healthcare security)
    public int RefreshTokenDays { get; init; } = 7;
}