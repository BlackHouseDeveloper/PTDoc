namespace PTDoc.Application.Identity;

public enum AuthStatus
{
    Success,
    InvalidCredentials,
    PendingApproval,
    AccountLocked,
    RequiresPinChange,
    RequiresMfaEnrollment,
    RequiresMfaVerification
}

public enum AuthSessionMode
{
    Stateful,
    IdentityOnly
}

/// <summary>
/// Service for authentication operations.
/// Handles PIN-based login, session management, and audit logging.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates a user with their username and PIN.
    /// Creates a stateful session by default, or returns verified identity data without a session for JWT issuance.
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
        CancellationToken cancellationToken = default,
        AuthSessionMode sessionMode = AuthSessionMode.Stateful);

    Task<AuthResult?> CompletePinChangeAsync(
        string challengeToken,
        string newPin,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default,
        AuthSessionMode sessionMode = AuthSessionMode.Stateful);

    Task<AuthResult?> CompleteMfaAsync(
        string completionToken,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default,
        AuthSessionMode sessionMode = AuthSessionMode.Stateful);

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
/// Result of an authentication attempt.
/// Fields other than <see cref="Status"/> are only populated when
/// <see cref="Status"/> is <see cref="AuthStatus.Success"/>.
/// </summary>
public class AuthResult
{
    public AuthStatus Status { get; init; } = AuthStatus.Success;

    /// <summary>Identifier of the authenticated user. Only set on <see cref="AuthStatus.Success"/>.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Username of the authenticated user. Only set on <see cref="AuthStatus.Success"/>.</summary>
    public string? Username { get; init; }

    /// <summary>Email claim carried into issued JWTs when the user has an email address.</summary>
    public string? Email { get; init; }

    /// <summary>Session token issued for a stateful authentication. Omitted for identity-only JWT authentication.</summary>
    public string? Token { get; init; }

    /// <summary>Expiration timestamp of a stateful session token. Omitted for identity-only JWT authentication.</summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>Role of the authenticated user. Only set on <see cref="AuthStatus.Success"/>.</summary>
    public string? Role { get; init; }

    /// <summary>Clinic the user belongs to, or null for system accounts. Only set on <see cref="AuthStatus.Success"/>.</summary>
    public Guid? ClinicId { get; init; }

    /// <summary>Short-lived purpose-bound token used before a session is issued.</summary>
    public string? ChallengeToken { get; init; }

    /// <summary>Clinic-specific minimum PIN length for a PIN-policy validation response.</summary>
    public int? MinimumPinLength { get; init; }
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
    /// <summary>Clinic the user belongs to, or null for system accounts.</summary>
    public Guid? ClinicId { get; init; }
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
    /// <summary>Clinic the user belongs to, or null for system accounts.</summary>
    public Guid? ClinicId { get; init; }
}
