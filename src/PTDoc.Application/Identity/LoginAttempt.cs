namespace PTDoc.Application.Identity;

/// <summary>
/// Represents a login attempt for security monitoring.
/// Used to detect brute force attacks and suspicious activity.
/// </summary>
public class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Username attempted (may not exist)
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// User ID if login was successful
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Timestamp of the attempt
    /// </summary>
    public DateTime AttemptedAt { get; set; }
    
    /// <summary>
    /// Whether the login was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// IP address of the client (for security monitoring)
    /// </summary>
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Failure reason if login failed
    /// </summary>
    public string? FailureReason { get; set; }
}
