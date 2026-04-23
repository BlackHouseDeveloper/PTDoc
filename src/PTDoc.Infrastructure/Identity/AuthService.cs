using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// Implementation of IAuthService using EF Core and BCrypt for PIN hashing.
/// Handles session management with 15-minute inactivity timeout.
/// </summary>
public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AuthService> _logger;
    private readonly IAuditService _auditService;

    // HIPAA-compliant session timeouts
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(8);

    public AuthService(ApplicationDbContext context, ILogger<AuthService> logger, IAuditService auditService)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<AuthResult?> AuthenticateAsync(
        string username,
        string pin,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var attemptedAt = DateTime.UtcNow;
        var normalizedIdentifier = username.Trim();
        var normalizedIdentifierLower = normalizedIdentifier.ToLowerInvariant();
        var identifierCandidates = normalizedIdentifier.Equals(normalizedIdentifierLower, StringComparison.Ordinal)
            ? [normalizedIdentifierLower]
            : new[] { normalizedIdentifier, normalizedIdentifierLower };

        try
        {
            // Query exact identifier candidates so the username/email indexes remain usable.
            var user = await _context.Users
                .Where(u =>
                    identifierCandidates.Contains(u.Username)
                    || (u.Email != null && identifierCandidates.Contains(u.Email)))
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                // Legacy fallback for rows that predate save-time identifier normalization.
                user = await _context.Users
                    .Where(u =>
                        u.Username.ToLower() == normalizedIdentifierLower
                        || (u.Email != null && u.Email.ToLower() == normalizedIdentifierLower))
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (user == null)
            {
                // Log failed attempt - user not found
                await LogLoginAttemptAsync(normalizedIdentifier, null, false, ipAddress, userAgent,
                    "User not found", attemptedAt, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                // Emit audit event (no username logged to avoid enumeration info leakage)
                await _auditService.LogAuthEventAsync(
                    AuditEvent.LoginFailed(ipAddress, "UserNotFound"), cancellationToken);
                return null;
            }

            if (!user.IsActive)
            {
                // Log failed attempt - user inactive
                await LogLoginAttemptAsync(normalizedIdentifier, user.Id, false, ipAddress, userAgent,
                    "User account is inactive", attemptedAt, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await _auditService.LogAuthEventAsync(
                    AuditEvent.LoginFailed(ipAddress, "AccountInactive"), cancellationToken);

                return new AuthResult
                {
                    Status = AuthStatus.PendingApproval,
                    UserId = user.Id,
                    Username = user.Username,
                    Token = string.Empty,
                    ExpiresAt = DateTime.UtcNow,
                    Role = user.Role,
                    ClinicId = user.ClinicId
                };
            }

            // Verify PIN using BCrypt
            bool isValidPin = BCrypt.Net.BCrypt.Verify(pin, user.PinHash);

            if (!isValidPin)
            {
                // Log failed attempt - invalid PIN
                await LogLoginAttemptAsync(normalizedIdentifier, user.Id, false, ipAddress, userAgent,
                    "Invalid PIN", attemptedAt, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await _auditService.LogAuthEventAsync(
                    AuditEvent.LoginFailed(ipAddress, "InvalidCredentials"), cancellationToken);
                return null;
            }

            // Generate session token
            var token = GenerateSecureToken();
            var tokenHash = HashToken(token);
            var now = DateTime.UtcNow;

            // Create session
            var session = new Session
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = tokenHash,
                CreatedAt = now,
                LastActivityAt = now,
                ExpiresAt = now + AbsoluteTimeout,
                IsRevoked = false
            };

            _context.Sessions.Add(session);

            // Update user last login
            user.LastLoginAt = now;

            // Log successful attempt
            await LogLoginAttemptAsync(normalizedIdentifier, user.Id, true, ipAddress, userAgent,
                null, attemptedAt, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User {UserId} logged in successfully", user.Id);

            // Emit structured audit event for successful login
            await _auditService.LogAuthEventAsync(
                AuditEvent.LoginSuccess(user.Id, ipAddress), cancellationToken);

            return new AuthResult
            {
                Status = AuthStatus.Success,
                UserId = user.Id,
                Username = user.Username,
                Token = token,
                ExpiresAt = session.ExpiresAt,
                Role = user.Role,
                ClinicId = user.ClinicId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for user {Username}", normalizedIdentifier);
            throw;
        }
    }

    public async Task<SessionInfo?> ValidateSessionAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;

        var session = await _context.Sessions
            .Include(s => s.User)
            .Where(s => s.TokenHash == tokenHash
                && !s.IsRevoked
                && s.ExpiresAt > now)
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null || session.User == null)
        {
            return null;
        }

        // Check inactivity timeout
        var lastActivity = session.LastActivityAt ?? session.CreatedAt;
        if (now - lastActivity > InactivityTimeout)
        {
            // Session expired due to inactivity
            session.IsRevoked = true;
            session.RevokedAt = now;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Session expired due to inactivity for user {Username}", session.User.Username);
            return null;
        }

        // Update last activity
        session.LastActivityAt = now;
        await _context.SaveChangesAsync(cancellationToken);

        return new SessionInfo
        {
            UserId = session.UserId,
            Username = session.User.Username,
            Role = session.User.Role,
            ExpiresAt = session.ExpiresAt,
            LastActivityAt = session.LastActivityAt ?? session.CreatedAt,
            ClinicId = session.User.ClinicId
        };
    }

    public async Task LogoutAsync(string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(token);

        var session = await _context.Sessions
            .Where(s => s.TokenHash == tokenHash && !s.IsRevoked)
            .FirstOrDefaultAsync(cancellationToken);

        if (session != null)
        {
            session.IsRevoked = true;
            session.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User {UserId} logged out, session revoked", session.UserId);

            // Emit structured audit event for logout
            await _auditService.LogAuthEventAsync(
                AuditEvent.Logout(session.UserId), cancellationToken);
        }
    }

    public async Task<Application.Identity.UserInfo?> GetCurrentUserAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var sessionInfo = await ValidateSessionAsync(token, cancellationToken);
        if (sessionInfo == null)
        {
            return null;
        }

        var user = await _context.Users
            .Where(u => u.Id == sessionInfo.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return null;
        }

        return new Application.Identity.UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            ClinicId = user.ClinicId
        };
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var expiredSessions = await _context.Sessions
            .Where(s => !s.IsRevoked && s.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        foreach (var session in expiredSessions)
        {
            session.IsRevoked = true;
            session.RevokedAt = now;
        }

        if (expiredSessions.Any())
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
        }
    }

    private Task LogLoginAttemptAsync(
        string username,
        Guid? userId,
        bool success,
        string? ipAddress,
        string? userAgent,
        string? failureReason,
        DateTime attemptedAt,
        CancellationToken cancellationToken)
    {
        var attempt = new LoginAttempt
        {
            Id = Guid.NewGuid(),
            Username = username,
            UserId = userId,
            Success = success,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            FailureReason = failureReason,
            AttemptedAt = attemptedAt
        };

        _context.LoginAttempts.Add(attempt);

        // Note: SaveChanges will be called by the caller
        return Task.CompletedTask;
    }

    private static string GenerateSecureToken()
    {
        var tokenBytes = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Helper method to hash a PIN for storage.
    /// Should be used when creating/updating users.
    /// </summary>
    public static string HashPin(string pin)
    {
        return BCrypt.Net.BCrypt.HashPassword(pin, BCrypt.Net.BCrypt.GenerateSalt(12));
    }
}
