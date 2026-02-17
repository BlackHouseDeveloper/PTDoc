namespace PTDoc.Application.Identity;

/// <summary>
/// Service for authentication operations.
/// Handles PIN-based login, session management, and audit logging.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a user with their username and PIN.
    /// Creates a new session and returns session token.
    /// Logs the attempt for security monitoring.
    /// </summary>
    /// <param name="username">Username</param>
    /// <param name="pin">PIN (will be hashed and compared)</param>
    /// <param name="ipAddress">Client IP address for audit</param>
    /// <param name="userAgent">Client user agent for audit</param>
    /// <returns>Session token if successful, null if authentication failed</returns>
    Task<AuthResult?> AuthenticateAsync(
        string username, 
        string pin, 
        string? ipAddress = null, 
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a session token and updates last activity timestamp.
    /// Returns null if session is invalid or expired.
    /// </summary>
    Task<SessionInfo?> ValidateSessionAsync(
        string token, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out a user by revoking their session.
    /// </summary>
    Task LogoutAsync(
        string token, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user information from a valid session token.
    /// </summary>
    Task<UserInfo?> GetCurrentUserAsync(
        string token, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired sessions (should be called periodically).
    /// </summary>
    Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an authentication attempt
/// </summary>
public class AuthResult
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string Token { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required string Role { get; init; }
}

/// <summary>
/// Information about a valid session
/// </summary>
public class SessionInfo
{
    public required Guid UserId { get; init; }
    public required string Username { get; init; }
    public required string Role { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public required DateTime LastActivityAt { get; init; }
}

/// <summary>
/// User information for current user endpoint
/// </summary>
public class UserInfo
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Role { get; init; }
    public required bool IsActive { get; init; }
}
