using PTDoc.Core.Enums;

namespace PTDoc.Application.Identity;

/// <summary>
/// Abstraction for identity providers (local PIN, Azure AD, EMR SSO, etc.).
/// </summary>
public interface IIdentityProvider
{
    /// <summary>
    /// Authenticates a user with provided credentials.
    /// </summary>
    /// <returns>Authentication result with user information if successful.</returns>
    Task<AuthenticationResult> AuthenticateAsync(AuthenticationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates an existing session token.
    /// </summary>
    Task<SessionValidationResult> ValidateSessionAsync(string sessionToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an expired session using a refresh token.
    /// </summary>
    Task<AuthenticationResult> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Logs out a user by invalidating their session.
    /// </summary>
    Task LogoutAsync(string sessionToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for authentication.
/// </summary>
public class AuthenticationRequest
{
    public string Username { get; set; } = string.Empty;
    public string? PIN { get; set; }
    public string? ExternalToken { get; set; }
    public string? DeviceIdentifier { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// Result of authentication attempt.
/// </summary>
public class AuthenticationResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public UserRole? Role { get; set; }
    public string? SessionToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? SessionExpiresUtc { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LockedOutUntilUtc { get; set; }
}

/// <summary>
/// Result of session validation.
/// </summary>
public class SessionValidationResult
{
    public bool IsValid { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public UserRole? Role { get; set; }
    public bool RequiresRenewal { get; set; }
    public bool IsExpired { get; set; }
}
