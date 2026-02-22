namespace PTDoc.Application.Intake;

/// <summary>
/// Manages invite-based access control for the standalone patient intake flow.
/// Validates signed invite tokens, issues OTP challenges, and grants short-lived
/// access sessions after successful verification.
/// </summary>
public interface IIntakeInviteService
{
    /// <summary>
    /// Validates a signed invite token embedded in the intake URL.
    /// Returns an access session if the token is valid and not yet redeemed; otherwise null.
    /// </summary>
    Task<IntakeAccessSession?> ValidateInviteTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a one-time passcode to the patient's contact (SMS or email)
    /// and returns a challenge that must be verified via <see cref="VerifyOtpAsync"/>.
    /// </summary>
    Task<IntakeOtpChallenge> RequestOtpAsync(string contact, OtpContactType contactType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the OTP code against the pending challenge and, on success,
    /// returns a new access session. Returns null if the code is wrong or expired.
    /// </summary>
    Task<IntakeAccessSession?> VerifyOtpAsync(string challengeId, string otpCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an active session identified by <paramref name="sessionId"/> is
    /// still valid (not expired and not revoked).
    /// </summary>
    Task<bool> IsSessionValidAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an active session, e.g. after the intake is submitted or the link is consumed.
    /// </summary>
    Task RevokeSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
