using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents an active user session for authentication.
/// </summary>
public class UserSession : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public Enums.SyncState SyncState { get; set; }
    
    /// <summary>
    /// User associated with this session.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Navigation property to the user.
    /// </summary>
    public User? User { get; set; }
    
    /// <summary>
    /// Secure token identifying this session.
    /// </summary>
    public string SessionToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Refresh token for obtaining new session tokens.
    /// </summary>
    public string? RefreshToken { get; set; }
    
    /// <summary>
    /// UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; }
    
    /// <summary>
    /// UTC timestamp when the session expires (absolute timeout, e.g., 8 hours).
    /// </summary>
    public DateTime ExpiresUtc { get; set; }
    
    /// <summary>
    /// UTC timestamp of the last activity (for inactivity timeout, e.g., 15 minutes).
    /// </summary>
    public DateTime LastActivityUtc { get; set; }
    
    /// <summary>
    /// Device/platform identifier for multi-device tracking.
    /// </summary>
    public string? DeviceIdentifier { get; set; }
    
    /// <summary>
    /// IP address from which the session was created (audit purposes).
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// Indicates if the session is still active.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
