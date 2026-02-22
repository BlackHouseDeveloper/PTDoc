namespace PTDoc.Application.Services;

/// <summary>Manages secure invite tokens and OTP verification for the standalone patient intake flow.</summary>
public interface IIntakeInviteService
{
    /// <summary>Validates a signed invite token from the URL and issues a short-lived intake access token.</summary>
    Task<IntakeInviteResult> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-time passcode to the specified contact via the selected channel.</summary>
    Task<bool> SendOtpAsync(string contact, OtpChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Verifies the OTP and issues a short-lived intake access token bound to the session.</summary>
    Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(string contact, string otpCode, CancellationToken cancellationToken = default);

    /// <summary>Validates whether an existing intake session access token is still active.</summary>
    Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the intake access token after submission or on session end.</summary>
    Task RevokeAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
