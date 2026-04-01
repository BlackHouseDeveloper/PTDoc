namespace PTDoc.Application.Intake;

/// <summary>Canonical invite link minted for a single intake record.</summary>
public record IntakeInviteLinkResult(
    bool Success,
    Guid IntakeId,
    Guid PatientId,
    string? InviteUrl,
    DateTimeOffset? ExpiresAt,
    string? Error);

/// <summary>Channel used to deliver a one-time passcode to the patient.</summary>
public enum OtpChannel { Sms, Email }

/// <summary>Result returned when validating an invite token or verifying an OTP.</summary>
public record IntakeInviteResult(bool IsValid, string? AccessToken, DateTimeOffset? ExpiresAt, string? Error);
