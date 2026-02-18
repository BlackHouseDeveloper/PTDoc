namespace PTDoc.Core.Models;

/// <summary>
/// Represents a user in the PTDoc system (PT, PTA, Admin, etc.).
/// Credentials are hashed using BCrypt or Argon2.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    // Credentials
    public string Username { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty; // BCrypt/Argon2 hash
    
    // Profile
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    
    // Role-based access
    public string Role { get; set; } = string.Empty; // "PT", "PTA", "Admin", "Aide"
    
    // License (for PTs and PTAs)
    public string? LicenseNumber { get; set; }
    public string? LicenseState { get; set; }
    public DateTime? LicenseExpirationDate { get; set; }
    
    // Status
    public bool IsActive { get; set; } = true;
    
    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    
    // Navigation properties
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
}
