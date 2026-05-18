using PTDoc.Core.Communication;

namespace PTDoc.Core.Models;

/// <summary>
/// Durable one-time passcode state for standalone intake access.
/// Stores hashed codes and hashed contacts only; raw OTP values are never persisted.
/// </summary>
public class IntakeOtpChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntakeId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ClinicId { get; set; }
    public DeliveryChannel Channel { get; set; }
    public string ContactHash { get; set; } = string.Empty;
    public string OtpHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset WindowStartUtc { get; set; }
    public int SendCount { get; set; }
    public int FailedVerifyCount { get; set; }
    public DateTimeOffset? LastFailedVerifyAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public string? CorrelationId { get; set; }
}
