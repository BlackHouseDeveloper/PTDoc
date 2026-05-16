using PTDoc.Core.Communication;

namespace PTDoc.Core.Models;

/// <summary>
/// Single-use password/PIN reset token record. The raw token is never stored.
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DeliveryChannel Channel { get; set; }
    public string RecipientHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public string? CorrelationId { get; set; }

    public User? User { get; set; }
}
