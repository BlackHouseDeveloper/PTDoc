using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    
    // HIPAA-compliant session timeouts
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(8);

    public AuthService(ApplicationDbContext context, ILogger<AuthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<AuthResult?> AuthenticateAsync(
        string username, 
        string pin, 
        string? ipAddress = null, 
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        var attemptedAt = DateTime.UtcNow;
        
        try
        {
            // Find user by username
            var user = await _context.Users
                .Where(u => u.Username == username)
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                // Log failed attempt - user not found
                await LogLoginAttemptAsync(username, null, false, ipAddress, userAgent, 
                    "User not found", attemptedAt, cancellationToken);
                return null;
            }

            if (!user.IsActive)
            {
                // Log failed attempt - user inactive
                await LogLoginAttemptAsync(username, user.Id, false, ipAddress, userAgent, 
                    "User account is inactive", attemptedAt, cancellationToken);
                return null;
            }

            // Verify PIN using BCrypt
            bool isValidPin = BCrypt.Net.BCrypt.Verify(pin, user.PinHash);
            
            if (!isValidPin)
            {
                // Log failed attempt - invalid PIN
                await LogLoginAttemptAsync(username, user.Id, false, ipAddress, userAgent, 
                    "Invalid PIN", attemptedAt, cancellationToken);
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
            await LogLoginAttemptAsync(username, user.Id, true, ipAddress, userAgent, 
                null, attemptedAt, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User {Username} logged in successfully", username);

            return new AuthResult
            {
                UserId = user.Id,
                Username = user.Username,
                Token = token,
                ExpiresAt = session.ExpiresAt,
                Role = user.Role
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for user {Username}", username);
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
            LastActivityAt = session.LastActivityAt ?? session.CreatedAt
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
            
            _logger.LogInformation("User logged out, session revoked");
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
            IsActive = user.IsActive
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

    private async Task LogLoginAttemptAsync(
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
