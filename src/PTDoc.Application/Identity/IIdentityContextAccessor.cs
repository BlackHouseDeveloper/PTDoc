namespace PTDoc.Application.Identity;

/// <summary>
/// Provides access to the current authenticated user context.
/// Used for automatically stamping ModifiedByUserId on entities.
/// </summary>
public interface IIdentityContextAccessor
{
    /// <summary>
    /// Gets the current authenticated user's ID.
    /// Returns null if no user is authenticated.
    /// </summary>
    Guid? GetCurrentUserId();
    
    /// <summary>
    /// Gets the current authenticated username.
    /// Returns null if no user is authenticated.
    /// </summary>
    string? GetCurrentUsername();
    
    /// <summary>
    /// Gets the current authenticated user's role.
    /// Returns null if no user is authenticated.
    /// </summary>
    string? GetCurrentUserRole();
    
    /// <summary>
    /// Determines if a user is currently authenticated.
    /// </summary>
    bool IsAuthenticated();
}
