namespace PTDoc.Core.Models;

/// <summary>
/// Represents an active user session with timeout enforcement.
/// Sessions expire after 15 minutes of inactivity or 8 hours absolute.
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
