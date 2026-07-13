namespace PTDoc.Application.Intake;

public sealed class ValidateIntakeInviteRequest
{
    public string InviteToken { get; set; } = string.Empty;
}

public sealed class SendIntakeOtpRequest
{
    public string InviteToken { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public OtpChannel Channel { get; set; }
}

public sealed class SendIntakeOtpResponse
{
    public bool Success { get; set; }

    public string RequestId { get; set; } = string.Empty;
}

public enum IntakeOtpSendOutcome
{
    Delivered,
    InviteInvalid,
    ContactInvalid,
    ContactMismatch,
    IntakeUnavailable,
    RateLimited,
    ProviderRejected,
    ProviderOutage
}

public readonly record struct IntakeOtpSendResult(
    bool Success,
    string RequestId,
    IntakeOtpSendOutcome Outcome);

public sealed class VerifyIntakeOtpRequest
{
    public string InviteToken { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public OtpChannel Channel { get; set; }
    public string OtpCode { get; set; } = string.Empty;
}

public sealed class IntakeAccessTokenRequest
{
    public string AccessToken { get; set; } = string.Empty;
}

public sealed class IntakeAccessTokenValidationResponse
{
    public bool IsValid { get; set; }
}
