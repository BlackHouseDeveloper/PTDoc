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
    public bool MustChangePin { get; set; }
    public DateTime? PinChangedAtUtc { get; set; }
    public DateTime? LegacyPinGraceEndsAtUtc { get; set; }

    // Profile
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NormalizedPhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }

    // Role-based access
    public string Role { get; set; } = string.Empty; // "PT", "PTA", "Admin", "Aide"

    // License (for PTs and PTAs)
    public string? LicenseNumber { get; set; }
    public string? LicenseState { get; set; }
    public DateTime? LicenseExpirationDate { get; set; }

    // Tenant / clinic scoping (Sprint J)
    /// <summary>
    /// Legacy home clinic, retained for registration and account credential administration.
    /// Clinical access requires an active ClinicMembership in the selected session clinic.
    /// Null is retained for system-level users and unassigned legacy accounts.
    /// </summary>
    public Guid? ClinicId { get; set; }

    // Status
    public bool IsActive { get; set; } = true;

    // Audit
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Navigation properties
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public Clinic? Clinic { get; set; }
    public UserMfaCredential? MfaCredential { get; set; }
}
