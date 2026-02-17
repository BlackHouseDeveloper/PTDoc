namespace PTDoc.Core.Enums;

/// <summary>
/// Represents the role of a user in the system for RBAC.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Physical Therapist - full privileges including signature and oversight.
    /// </summary>
    PT = 0,
    
    /// <summary>
    /// Physical Therapist Assistant - can create daily notes requiring PT co-signature.
    /// </summary>
    PTA = 1,
    
    /// <summary>
    /// Administrator - system configuration and user management.
    /// </summary>
    Admin = 2,
    
    /// <summary>
    /// Aide - limited documentation and scheduling privileges.
    /// </summary>
    Aide = 3
}
