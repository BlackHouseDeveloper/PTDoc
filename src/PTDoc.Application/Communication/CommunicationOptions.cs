namespace PTDoc.Application.Communication;

public sealed class CommunicationOptions
{
    public const string SectionName = "Communication";

    public string PublicBaseUrl { get; init; } = "http://localhost:5000";
    public string RecipientHashSalt { get; init; } = string.Empty;
    public TokenExpiryOptions TokenExpiryMinutes { get; init; } = new();
    public CommunicationRetentionOptions Retention { get; init; } = new();
    public CommunicationRateLimitOptions RateLimits { get; init; } = new();
    public AzureCommunicationOptions Azure { get; init; } = new();
}

public sealed class TokenExpiryOptions
{
    public int PasswordReset { get; init; } = 30;
    public int Intake { get; init; } = 10080;
}

public sealed class CommunicationRetentionOptions
{
    public int ResetTokensDays { get; init; } = 30;
    public int DeliveryLogsDays { get; init; } = 2190;
}

public sealed class CommunicationRateLimitOptions
{
    public int PasswordResetMaxPerWindow { get; init; } = 3;
    public int PasswordResetWindowMinutes { get; init; } = 15;
    public int IntakeMaxPerDay { get; init; } = 5;
}

public sealed class AzureCommunicationOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string EmailFromAddress { get; init; } = string.Empty;
    public string SmsFromPhoneNumber { get; init; } = string.Empty;
}
