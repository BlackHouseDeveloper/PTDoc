namespace PTDoc.Application.Intake;

/// <summary>
/// Represents a short-lived, verified access session for a standalone patient intake.
/// Issued after the patient successfully verifies their identity via OTP.
/// </summary>
public sealed record IntakeAccessSession(
    string SessionId,
    DateTimeOffset ExpiresAtUtc,
    string? InviteToken = null);

/// <summary>
/// Represents a pending OTP challenge issued to a patient contact (SMS or email).
/// </summary>
public sealed record IntakeOtpChallenge(
    string ChallengeId,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The contact method used when requesting an OTP for standalone intake access.
/// </summary>
public enum OtpContactType
{
    Sms,
    Email
}
