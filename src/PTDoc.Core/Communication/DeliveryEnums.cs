namespace PTDoc.Core.Communication;

public enum DeliveryStatus
{
    Unknown = 0,
    Sent = 1,
    Failed = 2,
    RateLimited = 3,
    Skipped = 4,
    ConfigurationError = 5
}

public enum DeliveryChannel
{
    Email = 0,
    Sms = 1
}

public enum DeliveryPurpose
{
    PasswordReset = 0,
    IntakeLink = 1,
    IntakeOtp = 2
}
