namespace PTDoc.Application.Services;

/// <summary>Channel used to deliver a one-time passcode to the patient.</summary>
public enum OtpChannel { Sms, Email }

/// <summary>Result returned when validating an invite token or verifying an OTP.</summary>
public record IntakeInviteResult(bool IsValid, string? AccessToken, DateTimeOffset? ExpiresAt, string? Error);

/// <summary>Represents a short-lived intake session access token stored on the client.</summary>
public record IntakeSessionToken(string Token, DateTimeOffset ExpiresAt);
