namespace PTDoc.Core.Models;

/// <summary>
/// Represents an active user session with timeout enforcement. A revoked row with no activity or
/// revocation timestamp is reserved as a short-lived, non-authenticated MFA completion claim and
/// is deleted atomically before the real session is issued.
/// Active sessions expire after the clinic inactivity timeout or 8 hours absolute.
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // User association
    public Guid UserId { get; set; }

    // Token
    public string TokenHash { get; set; } = string.Empty; // SHA-256 hash

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Revocation
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }

    // Navigation properties
    public User? User { get; set; }
}
