using PTDoc.Core.Enums;
using PTDoc.Core.Interfaces;

namespace PTDoc.Core.Models;

/// <summary>
/// Represents a user/clinician in the system with authentication and authorization.
/// </summary>
public class User : ISyncTrackedEntity
{
    public Guid Id { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }
    
    /// <summary>
    /// Username for login (e.g., email or username).
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// First name of the user.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;
    
    /// <summary>
    /// Last name of the user.
    /// </summary>
    public string LastName { get; set; } = string.Empty;
    
    /// <summary>
    /// Email address for the user.
    /// </summary>
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Hashed PIN for local authentication (salted, iterated hash).
    /// </summary>
    public string? PinHash { get; set; }
    
    /// <summary>
    /// Role of the user for RBAC.
    /// </summary>
    public UserRole Role { get; set; }
    
    /// <summary>
    /// Professional credentials (e.g., "PT, DPT").
    /// </summary>
    public string? Credentials { get; set; }
    
    /// <summary>
    /// License number for clinical documentation.
    /// </summary>
    public string? LicenseNumber { get; set; }
    
    /// <summary>
    /// NPI (National Provider Identifier) for billing.
    /// </summary>
    public string? NPI { get; set; }
    
    /// <summary>
    /// Indicates if the user account is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Number of failed login attempts (for rate limiting/lockout).
    /// </summary>
    public int FailedLoginAttempts { get; set; }
    
    /// <summary>
    /// UTC timestamp when the account was locked out due to failed attempts.
    /// Null if not locked.
    /// </summary>
    public DateTime? LockedOutUntilUtc { get; set; }
    
    /// <summary>
    /// Full name for display purposes.
    /// </summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
    
    /// <summary>
    /// Full name with credentials for clinical documents.
    /// </summary>
    public string FullNameWithCredentials => 
        string.IsNullOrEmpty(Credentials) ? FullName : $"{FullName}, {Credentials}";
}
