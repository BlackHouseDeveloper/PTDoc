using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Settings;
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
    private readonly IMfaAuthenticationService? _mfaAuthenticationService;
    private readonly TimeProvider _timeProvider;

    // HIPAA-compliant session timeouts
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromHours(8);
    private static readonly TimeSpan AuthenticationChallengeLifetime = TimeSpan.FromMinutes(5);

    public AuthService(
        ApplicationDbContext context,
        ILogger<AuthService> logger,
        IAuditService auditService,
        IMfaAuthenticationService? mfaAuthenticationService = null,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _mfaAuthenticationService = mfaAuthenticationService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

            return await ContinueAfterPrimaryAsync(user, pin, ipAddress, userAgent, attemptedAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication.");
            throw;
        }
    }

    public async Task<AuthResult?> CompletePinChangeAsync(
        string challengeToken,
        string newPin,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        if (_mfaAuthenticationService is null || !_mfaAuthenticationService.TryValidateChallenge(
                challengeToken,
                MfaChallengePurpose.PinChange,
                AuthenticationChallengeLifetime,
                out var principal))
        {
            return null;
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == principal.UserId && item.IsActive, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var policy = await GetSecurityPolicyAsync(user.ClinicId, cancellationToken);
        if (newPin.Length < policy.MinimumPinLength || newPin.Length > 12 || newPin.Any(character => !char.IsDigit(character)))
        {
            return new AuthResult
            {
                Status = AuthStatus.RequiresPinChange,
                UserId = user.Id,
                Username = user.Username,
                Role = user.Role,
                ClinicId = user.ClinicId,
                ChallengeToken = challengeToken
            };
        }

        user.PinHash = HashPin(newPin);
        user.MustChangePin = false;
        user.PinChangedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        user.LegacyPinGraceEndsAtUtc = null;
        await _context.SaveChangesAsync(cancellationToken);

        await _auditService.LogAuthEventAsync(new AuditEvent
        {
            EventType = "PinChanged",
            UserId = user.Id,
            EntityType = nameof(User),
            EntityId = user.Id,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = user.ClinicId?.ToString() ?? "none",
                ["reasonCode"] = "authentication_required_change"
            }
        }, cancellationToken);

        return await ContinueAfterPinComplianceAsync(user, policy, ipAddress, userAgent, cancellationToken);
    }

    public async Task<AuthResult?> CompleteMfaAsync(
        string completionToken,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        if (_mfaAuthenticationService is null || !_mfaAuthenticationService.TryValidateChallenge(
                completionToken,
                MfaChallengePurpose.AuthenticationCompletion,
                AuthenticationChallengeLifetime,
                out var principal))
        {
            return null;
        }

        var user = await _context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == principal.UserId && item.IsActive, cancellationToken);
        return user is null
            ? null
            : await IssueSessionAsync(user, ipAddress, userAgent, cancellationToken);
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

        var inactivityMinutes = session.User.ClinicId.HasValue
            ? await _context.ClinicSecurityPolicies
                .Where(item => item.ClinicId == session.User.ClinicId.Value)
                .Select(item => (int?)item.SessionInactivityMinutes)
                .SingleOrDefaultAsync(cancellationToken) ?? 15
            : 15;

        // Check clinic-configured inactivity timeout
        var lastActivity = session.LastActivityAt ?? session.CreatedAt;
        if (now - lastActivity > TimeSpan.FromMinutes(Math.Clamp(inactivityMinutes, 5, 60)))
        {
            // Session expired due to inactivity
            session.IsRevoked = true;
            session.RevokedAt = now;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Session expired due to inactivity for user {UserId}", session.UserId);
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

    private async Task<AuthResult> ContinueAfterPrimaryAsync(
        User user,
        string suppliedPin,
        string? ipAddress,
        string? userAgent,
        DateTime attemptedAt,
        CancellationToken cancellationToken)
    {
        var policy = await GetSecurityPolicyAsync(user.ClinicId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (suppliedPin.Length < policy.MinimumPinLength)
        {
            if (!user.LegacyPinGraceEndsAtUtc.HasValue)
            {
                user.LegacyPinGraceEndsAtUtc = now.AddDays(14);
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (user.LegacyPinGraceEndsAtUtc <= now)
            {
                user.MustChangePin = true;
            }
        }

        if (user.MustChangePin)
        {
            return ChallengeResult(user, AuthStatus.RequiresPinChange, MfaChallengePurpose.PinChange);
        }

        return await ContinueAfterPinComplianceAsync(user, policy, ipAddress, userAgent, cancellationToken, attemptedAt);
    }

    private async Task<AuthResult> ContinueAfterPinComplianceAsync(
        User user,
        ClinicSecurityPolicy policy,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        DateTime? attemptedAt = null)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var isMfaEnforced = policy.MfaEnforcementMode == MfaEnforcementMode.Enforced
            && policy.MfaEffectiveAtUtc.HasValue
            && policy.MfaEffectiveAtUtc.Value <= now;
        if (isMfaEnforced)
        {
            var enrolled = await _context.UserMfaCredentials
                .AnyAsync(item => item.UserId == user.Id && item.IsActive, cancellationToken);
            return ChallengeResult(
                user,
                enrolled ? AuthStatus.RequiresMfaVerification : AuthStatus.RequiresMfaEnrollment,
                enrolled ? MfaChallengePurpose.Verification : MfaChallengePurpose.Enrollment);
        }

        return await IssueSessionAsync(user, ipAddress, userAgent, cancellationToken, attemptedAt);
    }

    private AuthResult ChallengeResult(User user, AuthStatus status, MfaChallengePurpose purpose) => new()
    {
        Status = status,
        UserId = user.Id,
        Username = user.Username,
        Role = user.Role,
        ClinicId = user.ClinicId,
        ChallengeToken = _mfaAuthenticationService?.CreateChallenge(user.Id, purpose)
    };

    private async Task<AuthResult> IssueSessionAsync(
        User user,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken,
        DateTime? attemptedAt = null)
    {
        var token = GenerateSecureToken();
        var tokenHash = HashToken(token);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
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
        user.LastLoginAt = now;
        await LogLoginAttemptAsync(user.Username, user.Id, true, ipAddress, userAgent, null, attemptedAt ?? now, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("User {UserId} logged in successfully", user.Id);
        await _auditService.LogAuthEventAsync(AuditEvent.LoginSuccess(user.Id, ipAddress), cancellationToken);

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

    private async Task<ClinicSecurityPolicy> GetSecurityPolicyAsync(Guid? clinicId, CancellationToken cancellationToken)
    {
        if (clinicId.HasValue)
        {
            var stored = await _context.ClinicSecurityPolicies
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.ClinicId == clinicId.Value, cancellationToken);
            if (stored is not null) return stored;
        }

        return new ClinicSecurityPolicy
        {
            MinimumPinLength = 8,
            SessionInactivityMinutes = 15,
            MfaEnforcementMode = MfaEnforcementMode.Off
        };
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
