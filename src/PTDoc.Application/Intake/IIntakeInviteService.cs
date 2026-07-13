namespace PTDoc.Application.Intake;

/// <summary>Manages secure invite tokens and OTP verification for the standalone patient intake flow.</summary>
public interface IIntakeInviteService
{
    /// <summary>Creates or rotates the secure invite link for a specific intake record.</summary>
    Task<IntakeInviteLinkResult> CreateInviteAsync(Guid intakeId, CancellationToken cancellationToken = default);

    /// <summary>Validates a signed invite token from the URL without issuing intake access.</summary>
    Task<IntakeInviteValidationResponse> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default);

    /// <summary>Sends a one-time passcode to the specified contact via the selected channel for a signed invite.</summary>
    Task<bool> SendOtpAsync(string inviteToken, string contact, OtpChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an OTP and returns a non-PHI diagnostic outcome. Implementations that do not provide
    /// detailed diagnostics retain the existing boolean behavior through this default implementation.
    /// </summary>
    async Task<IntakeOtpSendResult> SendOtpWithDiagnosticsAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var success = await SendOtpAsync(inviteToken, contact, channel, cancellationToken);
        return new IntakeOtpSendResult(
            success,
            requestId,
            success ? IntakeOtpSendOutcome.Delivered : IntakeOtpSendOutcome.ProviderRejected);
    }

    /// <summary>Verifies the OTP and issues a short-lived intake access token bound to the signed invite.</summary>
    Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(string inviteToken, string contact, OtpChannel channel, string otpCode, CancellationToken cancellationToken = default);

    /// <summary>Validates whether an existing intake session access token is still active.</summary>
    Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes the intake access token after submission or on session end.</summary>
    Task RevokeAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default);
}
