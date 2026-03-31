namespace PTDoc.Core.Models;

/// <summary>
/// Persisted refresh token record for durable JWT session management.
/// The raw token value is never stored — only its SHA-256 hash.
/// This prevents token theft even if the database is compromised.
/// HIPAA: Token records must be retained per audit requirements; use soft revocation.
/// </summary>
public class StoredRefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>SHA-256 hex-encoded hash of the raw refresh token string.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Subject (internal user ID) the token was issued to.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Claims serialized as a JSON array of {Type, Value} objects.</summary>
    public string ClaimsJson { get; set; } = "[]";

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>True when the token has been explicitly revoked (logout, rotation, or security event).</summary>
    public bool IsRevoked { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}
