namespace PTDoc.Application.Services;

/// <summary>Configuration options for the intake invite and access token service.</summary>
public sealed class IntakeInviteOptions
{
    public const string SectionName = "IntakeInvite";

    /// <summary>
    /// HMAC-SHA256 signing key used to sign and validate intake invite tokens and session access tokens.
    /// Must be at least 32 characters. Must not use the placeholder value in production.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Minutes before a one-time passcode expires. Defaults to 10.</summary>
    public int OtpExpiryMinutes { get; init; } = 10;

    /// <summary>Minutes before a session access token expires. Defaults to 120 (2 hours).</summary>
    public int AccessTokenExpiryMinutes { get; init; } = 120;
}
