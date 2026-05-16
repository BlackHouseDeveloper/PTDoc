namespace PTDoc.Application.Communication;

public sealed class CommunicationOptions
{
    public const string SectionName = "Communication";

    public string PublicBaseUrl { get; init; } = "http://localhost:5000";
    public string RecipientHashSalt { get; init; } = string.Empty;
    public TokenExpiryOptions TokenExpiryMinutes { get; init; } = new();
    public AzureCommunicationOptions Azure { get; init; } = new();
}

public sealed class TokenExpiryOptions
{
    public int PasswordReset { get; init; } = 30;
    public int Intake { get; init; } = 10080;
}

public sealed class AzureCommunicationOptions
{
    public string ConnectionString { get; init; } = string.Empty;
    public string EmailFromAddress { get; init; } = string.Empty;
    public string SmsFromPhoneNumber { get; init; } = string.Empty;
}
