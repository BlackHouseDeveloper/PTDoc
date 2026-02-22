using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Application.Intake;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Production implementation of <see cref="IIntakeInviteService"/> that uses HMAC-SHA256 signed JWTs
/// for invite tokens and access tokens, and per-request cryptographically random OTPs.
/// </summary>
/// <remarks>
/// <para>
/// OTP delivery (SMS / email) must be wired to a real notification service before going to production.
/// Until then, this implementation logs that delivery is pending but does NOT log the OTP value itself.
/// </para>
/// <para>
/// The OTP and revocation stores are in-memory and scoped to a single application instance.
/// In multi-instance or containerised deployments, replace them with a distributed cache
/// (e.g., Redis or a database-backed store) before going to production to ensure correctness.
/// </para>
/// </remarks>
public sealed class JwtIntakeInviteService : IIntakeInviteService
{
    private const string Issuer = "PTDoc.IntakeInvite";
    private const string InviteAudience = "ptdoc_invite";
    private const string AccessAudience = "ptdoc_intake";
    private const string InviteTypeClaim = "intake_invite";
    private const string AccessTypeClaim = "intake_access";
    private const string TokenTypeClaim = "typ";

    // Rate-limit: at most 3 OTP requests per contact per sliding window.
    private const int MaxOtpRequestsPerWindow = 3;
    private static readonly TimeSpan OtpRateLimitWindow = TimeSpan.FromHours(1);

    // Verification lockout: at most 5 failed attempts per contact before lockout.
    private const int MaxFailedVerifyAttempts = 5;

    private readonly IntakeInviteOptions _options;
    private readonly ILogger<JwtIntakeInviteService> _logger;

    /// <summary>
    /// In-memory OTP store: contact → (hashed OTP, expiry).
    /// WARNING: Not shared across application instances. Replace with a distributed cache for production.
    /// </summary>
    private readonly ConcurrentDictionary<string, (string OtpHash, DateTimeOffset Expiry)> _pendingOtps = new();

    /// <summary>
    /// Per-contact OTP send rate tracking: contact → (count, window start).
    /// WARNING: Not shared across application instances. Replace with a distributed cache for production.
    /// </summary>
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _otpSendRates = new();

    /// <summary>
    /// Per-contact failed verification attempts: contact → (count, last failure).
    /// WARNING: Not shared across application instances. Replace with a distributed cache for production.
    /// </summary>
    private readonly ConcurrentDictionary<string, (int FailCount, DateTimeOffset LastAttempt)> _verifyFailures = new();

    /// <summary>
    /// In-memory revocation list: jti → token expiry.
    /// WARNING: Not shared across application instances. Replace with a distributed cache for production.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public JwtIntakeInviteService(IOptions<IntakeInviteOptions> options, ILogger<JwtIntakeInviteService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<IntakeInviteResult> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteToken))
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invalid invite token."));
        }

        var key = GetSigningKey();
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var principal = handler.ValidateToken(inviteToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = InviteAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);

            var typClaim = principal.FindFirst(TokenTypeClaim)?.Value;
            if (!string.Equals(typClaim, InviteTypeClaim, StringComparison.Ordinal))
            {
                return Task.FromResult(new IntakeInviteResult(false, null, null, "Token type is not valid for intake."));
            }
        }
        catch (SecurityTokenException)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired."));
        }

        return Task.FromResult(IssueAccessToken());
    }

    /// <inheritdoc/>
    public Task<bool> SendOtpAsync(string contact, OtpChannel channel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return Task.FromResult(false);
        }

        // Rate-limit OTP requests to prevent contact spam.
        var now = DateTimeOffset.UtcNow;
        var rateEntry = _otpSendRates.GetOrAdd(contact, _ => (0, now));

        if (now - rateEntry.WindowStart < OtpRateLimitWindow)
        {
            if (rateEntry.Count >= MaxOtpRequestsPerWindow)
            {
                _logger.LogWarning("OTP send rate limit exceeded for contact (not logged for privacy).");
                return Task.FromResult(false);
            }

            _otpSendRates[contact] = (rateEntry.Count + 1, rateEntry.WindowStart);
        }
        else
        {
            // New rate-limit window.
            _otpSendRates[contact] = (1, now);
        }

        var otp = GenerateOtp();
        var hash = HashOtp(otp);
        var expiry = now.AddMinutes(_options.OtpExpiryMinutes);
        _pendingOtps[contact] = (hash, expiry);

        // Reset failed attempt counter on new OTP issuance.
        _verifyFailures.TryRemove(contact, out _);

        // TODO: Replace this placeholder with an actual SMS/email delivery service.
        // The OTP value is intentionally not logged to prevent credential exposure.
        _logger.LogWarning(
            "OTP delivery via {Channel} is pending real notification service integration. " +
            "Delivery not performed.",
            channel);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(
        string contact,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        // Check if contact is locked out due to too many failed attempts.
        if (_verifyFailures.TryGetValue(contact, out var failure) && failure.FailCount >= MaxFailedVerifyAttempts)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null,
                "Too many incorrect attempts. Please request a new code."));
        }

        if (!_pendingOtps.TryGetValue(contact, out var pending) || pending.Expiry < DateTimeOffset.UtcNow)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invalid or expired code."));
        }

        if (!VerifyOtp(otpCode, pending.OtpHash))
        {
            // Increment the failed attempt count.
            _verifyFailures.AddOrUpdate(
                contact,
                _ => (1, DateTimeOffset.UtcNow),
                (_, prev) => (prev.FailCount + 1, DateTimeOffset.UtcNow));

            return Task.FromResult(new IntakeInviteResult(false, null, null, "Incorrect code. Please try again."));
        }

        _pendingOtps.TryRemove(contact, out _);
        _verifyFailures.TryRemove(contact, out _);

        return Task.FromResult(IssueAccessToken(contact));
    }

    /// <inheritdoc/>
    public Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Task.FromResult(false);
        }

        var key = GetSigningKey();
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var principal = handler.ValidateToken(accessToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = AccessAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out var validatedToken);

            var typClaim = principal.FindFirst(TokenTypeClaim)?.Value;
            if (!string.Equals(typClaim, AccessTypeClaim, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            // Check revocation list.
            var jti = validatedToken is JwtSecurityToken jwt ? jwt.Id : null;
            if (jti is not null && _revokedTokens.ContainsKey(jti))
            {
                return Task.FromResult(false);
            }
        }
        catch (SecurityTokenException)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task RevokeAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Task.CompletedTask;
        }

        var handler = new JwtSecurityTokenHandler();
        if (handler.CanReadToken(accessToken))
        {
            var token = handler.ReadJwtToken(accessToken);
            if (!string.IsNullOrEmpty(token.Id))
            {
                _revokedTokens[token.Id] = token.ValidTo;
                PurgeExpiredRevocations();
            }
        }

        return Task.CompletedTask;
    }

    private IntakeInviteResult IssueAccessToken(string? subject = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(_options.AccessTokenExpiryMinutes);
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(TokenTypeClaim, AccessTypeClaim),
            new(JwtRegisteredClaimNames.Jti, jti)
        };

        if (!string.IsNullOrEmpty(subject))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, subject));
        }

        var key = GetSigningKey();
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: AccessAudience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiry.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new IntakeInviteResult(true, accessToken, expiry, null);
    }

    /// <summary>Removes expired entries from the revocation list to prevent unbounded memory growth.</summary>
    private void PurgeExpiredRevocations()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _revokedTokens.Keys.ToList())
        {
            if (_revokedTokens.TryGetValue(key, out var expiry) && expiry < now)
            {
                _revokedTokens.TryRemove(key, out _);
            }
        }
    }

    private SymmetricSecurityKey GetSigningKey()
        => new(Encoding.UTF8.GetBytes(_options.SigningKey));

    /// <summary>Generates a zero-padded 6-digit random OTP using a cryptographically secure RNG.</summary>
    private static string GenerateOtp()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    /// <summary>Returns a SHA-256 HMAC of the OTP using the signing key to avoid rainbow-table attacks.</summary>
    private string HashOtp(string otp)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var otpBytes = Encoding.UTF8.GetBytes(otp);
        var hash = HMACSHA256.HashData(keyBytes, otpBytes);
        return Convert.ToBase64String(hash);
    }

    private bool VerifyOtp(string candidate, string storedHash)
    {
        var candidateHash = HashOtp(candidate);

        // Constant-time comparison to prevent timing attacks.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(storedHash));
    }
}

