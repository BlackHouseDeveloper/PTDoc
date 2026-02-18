namespace PTDoc.Application.Identity;

/// <summary>
/// Provides access to the current user context for audit trail stamping.
/// Used by infrastructure to stamp ModifiedByUserId on all entity changes.
/// </summary>
public interface IIdentityContextAccessor
{
    /// <summary>
    /// Gets the current authenticated user's ID.
    /// Returns system user ID if no user is authenticated (e.g., background jobs, migrations).
    /// </summary>
    Guid GetCurrentUserId();

    /// <summary>
    /// Gets the current authenticated user's ID if available.
    /// Returns null if no user is authenticated.
    /// </summary>
    Guid? TryGetCurrentUserId();

    /// <summary>
    /// Gets the current authenticated user's username.
    /// Returns "System" if no user is authenticated.
    /// </summary>
    string GetCurrentUsername();

    /// <summary>
    /// Gets the current authenticated user's role.
    /// Returns null if no user is authenticated.
    /// </summary>
    string? GetCurrentUserRole();

    /// <summary>
    /// The system user ID used for background jobs and migrations.
    /// </summary>
    static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
}
