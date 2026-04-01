namespace PTDoc.Application.Intake;

public sealed class ValidateIntakeInviteRequest
{
    public string InviteToken { get; set; } = string.Empty;
}

public sealed class SendIntakeOtpRequest
{
    public string Contact { get; set; } = string.Empty;
    public OtpChannel Channel { get; set; }
}

public sealed class SendIntakeOtpResponse
{
    public bool Success { get; set; }
}

public sealed class VerifyIntakeOtpRequest
{
    public string Contact { get; set; } = string.Empty;
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
