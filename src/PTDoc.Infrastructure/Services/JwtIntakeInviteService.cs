using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PTDoc.Application.Compliance;
using PTDoc.Application.Integrations;
using PTDoc.Application.Intake;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Production implementation of <see cref="IIntakeInviteService"/> that uses signed JWT invite links,
/// short-lived access tokens, and shared outbound email/SMS delivery services.
/// </summary>
public sealed class JwtIntakeInviteService : IIntakeInviteService
{
    private const string Issuer = "PTDoc.IntakeInvite";
    private const string InviteAudience = "ptdoc_invite";
    private const string AccessAudience = "ptdoc_intake";
    private const string InviteTypeClaim = "intake_invite";
    private const string AccessTypeClaim = "intake_access";
    private const string TokenTypeClaim = "typ";
    private const string IntakeIdClaim = "intake_id";
    private const string PatientIdClaim = "patient_id";
    private const string ContactClaim = "contact";
    private const string InviteSecretClaim = "invite_secret";

    private const int MaxOtpRequestsPerWindow = 3;
    private static readonly TimeSpan OtpRateLimitWindow = TimeSpan.FromHours(1);
    private const int MaxFailedVerifyAttempts = 5;

    private readonly IntakeInviteOptions _options;
    private readonly ApplicationDbContext _db;
    private readonly IEmailDeliveryService _emailDeliveryService;
    private readonly ISmsDeliveryService _smsDeliveryService;
    private readonly IAuditService _auditService;
    private readonly ILogger<JwtIntakeInviteService> _logger;

    private readonly ConcurrentDictionary<string, (string OtpHash, DateTimeOffset Expiry)> _pendingOtps = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset WindowStart)> _otpSendRates = new();
    private readonly ConcurrentDictionary<string, (int FailCount, DateTimeOffset LastAttempt)> _verifyFailures = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public JwtIntakeInviteService(
        IOptions<IntakeInviteOptions> options,
        ApplicationDbContext db,
        IEmailDeliveryService emailDeliveryService,
        ISmsDeliveryService smsDeliveryService,
        IAuditService auditService,
        ILogger<JwtIntakeInviteService> logger)
    {
        _options = options.Value;
        _db = db;
        _emailDeliveryService = emailDeliveryService;
        _smsDeliveryService = smsDeliveryService;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<IntakeInviteLinkResult> CreateInviteAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var intake = await _db.IntakeForms
            .Include(form => form.Patient)
            .FirstOrDefaultAsync(form => form.Id == intakeId, cancellationToken);

        if (intake is null)
        {
            return new IntakeInviteLinkResult(false, intakeId, Guid.Empty, null, null, "Intake record was not found.");
        }

        if (intake.PatientId == Guid.Empty)
        {
            return new IntakeInviteLinkResult(false, intake.Id, Guid.Empty, null, null, "Intake record is missing its patient association.");
        }

        if (intake.IsLocked || intake.SubmittedAt.HasValue)
        {
            return new IntakeInviteLinkResult(false, intake.Id, intake.PatientId, null, null, "Submitted or locked intakes cannot receive invite links.");
        }

        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(Math.Max(1, _options.InviteExpiryMinutes));
        var rawSecret = GenerateInviteSecret();

        intake.AccessToken = HashInviteSecret(rawSecret);
        intake.ExpiresAt = expiry.UtcDateTime;
        await _db.SaveChangesAsync(cancellationToken);

        var token = WriteJwt(
            audience: InviteAudience,
            tokenType: InviteTypeClaim,
            expiresAt: expiry,
            claims:
            [
                new Claim(IntakeIdClaim, intake.Id.ToString()),
                new Claim(PatientIdClaim, intake.PatientId.ToString()),
                new Claim(InviteSecretClaim, rawSecret)
            ]);

        var baseUrl = NormalizePublicWebBaseUrl(_options.PublicWebBaseUrl);
        var inviteUrl = $"{baseUrl}/intake/{intake.PatientId:D}?mode=patient&invite={Uri.EscapeDataString(token)}";

        return new IntakeInviteLinkResult(true, intake.Id, intake.PatientId, inviteUrl, expiry, null);
    }

    public async Task<IntakeInviteResult> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inviteToken))
        {
            return new IntakeInviteResult(false, null, null, "Invalid invite token.");
        }

        try
        {
            var principal = ValidateJwt(inviteToken, InviteAudience, InviteTypeClaim, out _);
            var intakeId = ReadGuidClaim(principal, IntakeIdClaim);
            var patientId = ReadGuidClaim(principal, PatientIdClaim);
            var rawSecret = ReadClaimValue(principal, InviteSecretClaim);

            if (intakeId == Guid.Empty || patientId == Guid.Empty || string.IsNullOrWhiteSpace(rawSecret))
            {
                return new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired.");
            }

            var intake = await _db.IntakeForms
                .AsNoTracking()
                .FirstOrDefaultAsync(form => form.Id == intakeId, cancellationToken);

            if (intake is null || intake.PatientId != patientId)
            {
                return new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired.");
            }

            if (intake.IsLocked || intake.SubmittedAt.HasValue)
            {
                return new IntakeInviteResult(false, null, null, "This intake has already been submitted.");
            }

            if (intake.ExpiresAt.HasValue && intake.ExpiresAt.Value < DateTime.UtcNow)
            {
                return new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired.");
            }

            if (!VerifyInviteSecret(rawSecret, intake.AccessToken))
            {
                return new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired.");
            }

            return IssueAccessToken(patientId: patientId, intakeId: intakeId);
        }
        catch (SecurityTokenException)
        {
            return new IntakeInviteResult(false, null, null, "Invite link is invalid or has expired.");
        }
    }

    public async Task<bool> SendOtpAsync(string contact, OtpChannel channel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return false;
        }

        var normalizedContact = contact.Trim();
        var now = DateTimeOffset.UtcNow;
        var rateEntry = _otpSendRates.GetOrAdd(normalizedContact, _ => (0, now));

        if (now - rateEntry.WindowStart < OtpRateLimitWindow)
        {
            if (rateEntry.Count >= MaxOtpRequestsPerWindow)
            {
                await LogOtpEventAsync("IntakeOtpDeliveryFailed", channel, normalizedContact, success: false, provider: null, providerMessageId: null, error: "RateLimitExceeded", cancellationToken);
                _logger.LogWarning("OTP send rate limit exceeded for intake contact.");
                return false;
            }

            _otpSendRates[normalizedContact] = (rateEntry.Count + 1, rateEntry.WindowStart);
        }
        else
        {
            _otpSendRates[normalizedContact] = (1, now);
        }

        var otp = GenerateOtp();
        _pendingOtps[normalizedContact] = (HashOtp(otp), now.AddMinutes(_options.OtpExpiryMinutes));
        _verifyFailures.TryRemove(normalizedContact, out _);

        var delivery = await DeliverOtpAsync(normalizedContact, otp, channel, cancellationToken);
        if (!delivery.Success)
        {
            await LogOtpEventAsync("IntakeOtpDeliveryFailed", channel, normalizedContact, success: false, provider: delivery.Provider, providerMessageId: delivery.ProviderMessageId, error: delivery.ErrorMessage, cancellationToken);
            return false;
        }

        await LogOtpEventAsync("IntakeOtpDelivered", channel, normalizedContact, success: true, provider: delivery.Provider, providerMessageId: delivery.ProviderMessageId, error: null, cancellationToken);
        return true;
    }

    public Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(
        string contact,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedContact = contact?.Trim() ?? string.Empty;

        if (_verifyFailures.TryGetValue(normalizedContact, out var failure) && failure.FailCount >= MaxFailedVerifyAttempts)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Too many incorrect attempts. Please request a new code."));
        }

        if (!_pendingOtps.TryGetValue(normalizedContact, out var pending) || pending.Expiry < DateTimeOffset.UtcNow)
        {
            return Task.FromResult(new IntakeInviteResult(false, null, null, "Invalid or expired code."));
        }

        if (!VerifyOtp(otpCode, pending.OtpHash))
        {
            _verifyFailures.AddOrUpdate(
                normalizedContact,
                _ => (1, DateTimeOffset.UtcNow),
                (_, previous) => (previous.FailCount + 1, DateTimeOffset.UtcNow));

            return Task.FromResult(new IntakeInviteResult(false, null, null, "Incorrect code. Please try again."));
        }

        _pendingOtps.TryRemove(normalizedContact, out _);
        _verifyFailures.TryRemove(normalizedContact, out _);

        return Task.FromResult(IssueAccessToken(contact: normalizedContact));
    }

    public Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Task.FromResult(false);
        }

        try
        {
            var principal = ValidateJwt(accessToken, AccessAudience, AccessTypeClaim, out var validatedToken);
            var tokenId = validatedToken is JwtSecurityToken jwt ? jwt.Id : null;

            if (!string.IsNullOrWhiteSpace(tokenId) && _revokedTokens.ContainsKey(tokenId))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(principal.Identity?.IsAuthenticated == true);
        }
        catch (SecurityTokenException)
        {
            return Task.FromResult(false);
        }
    }

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
            if (!string.IsNullOrWhiteSpace(token.Id))
            {
                _revokedTokens[token.Id] = token.ValidTo;
                PurgeExpiredRevocations();
            }
        }

        return Task.CompletedTask;
    }

    private async Task<(bool Success, string Provider, string? ProviderMessageId, string? ErrorMessage)> DeliverOtpAsync(
        string contact,
        string otp,
        OtpChannel channel,
        CancellationToken cancellationToken)
    {
        var message = $"Your PTDoc intake verification code is {otp}. It expires in {_options.OtpExpiryMinutes} minutes.";

        if (channel == OtpChannel.Email)
        {
            var result = await _emailDeliveryService.SendAsync(new EmailDeliveryRequest
            {
                ToAddress = contact,
                Subject = "Your PTDoc intake verification code",
                PlainTextBody = message
            }, cancellationToken);

            return (result.Success, "SendGrid", result.ProviderMessageId, result.ErrorMessage);
        }

        var smsResult = await _smsDeliveryService.SendAsync(new SmsDeliveryRequest
        {
            ToNumber = contact,
            Message = message
        }, cancellationToken);

        return (smsResult.Success, "Twilio", smsResult.ProviderMessageId, smsResult.ErrorMessage);
    }

    private async Task LogOtpEventAsync(
        string eventType,
        OtpChannel channel,
        string contact,
        bool success,
        string? provider,
        string? providerMessageId,
        string? error,
        CancellationToken cancellationToken)
    {
        await _auditService.LogIntakeEventAsync(new AuditEvent
        {
            EventType = eventType,
            Success = success,
            ErrorMessage = error,
            Metadata = new Dictionary<string, object>
            {
                ["Channel"] = channel.ToString(),
                ["DestinationMasked"] = MaskDestination(contact),
                ["Provider"] = provider ?? "unknown",
                ["ProviderMessageId"] = providerMessageId ?? string.Empty,
                ["TimestampUtc"] = DateTime.UtcNow
            }
        }, cancellationToken);
    }

    private ClaimsPrincipal ValidateJwt(
        string token,
        string audience,
        string expectedType,
        out SecurityToken validatedToken)
    {
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = GetSigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        }, out validatedToken);

        var type = ReadClaimValue(principal, TokenTypeClaim);
        if (!string.Equals(type, expectedType, StringComparison.Ordinal))
        {
            throw new SecurityTokenException("Unexpected token type.");
        }

        return principal;
    }

    private IntakeInviteResult IssueAccessToken(Guid? patientId = null, Guid? intakeId = null, string? contact = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddMinutes(_options.AccessTokenExpiryMinutes);
        var claims = new List<Claim>();

        if (patientId.HasValue)
        {
            claims.Add(new Claim(PatientIdClaim, patientId.Value.ToString()));
        }

        if (intakeId.HasValue)
        {
            claims.Add(new Claim(IntakeIdClaim, intakeId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(contact))
        {
            claims.Add(new Claim(ContactClaim, contact));
        }

        var accessToken = WriteJwt(AccessAudience, AccessTypeClaim, expiry, claims);
        return new IntakeInviteResult(true, accessToken, expiry, null);
    }

    private string WriteJwt(
        string audience,
        string tokenType,
        DateTimeOffset expiresAt,
        IEnumerable<Claim> claims)
    {
        var now = DateTimeOffset.UtcNow;
        var jwtClaims = new List<Claim>(claims)
        {
            new(TokenTypeClaim, tokenType),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var subjectClaim = jwtClaims.FirstOrDefault(claim => claim.Type == PatientIdClaim || claim.Type == ContactClaim);
        if (subjectClaim is not null)
        {
            jwtClaims.Add(new Claim(JwtRegisteredClaimNames.Sub, subjectClaim.Value));
        }

        var jwt = new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: jwtClaims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(GetSigningKey(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

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

    private static string? ReadClaimValue(ClaimsPrincipal principal, string claimType)
        => principal.FindFirst(claimType)?.Value;

    private static Guid ReadGuidClaim(ClaimsPrincipal principal, string claimType)
        => Guid.TryParse(ReadClaimValue(principal, claimType), out var value) ? value : Guid.Empty;

    private static string NormalizePublicWebBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "http://localhost:5000";
        }

        return value.TrimEnd('/');
    }

    private static string GenerateInviteSecret()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string HashInviteSecret(string secret)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool VerifyInviteSecret(string rawSecret, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var candidateHash = HashInviteSecret(rawSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    private static string GenerateOtp()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

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
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidateHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    private static string MaskDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        var trimmed = destination.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex > 1)
        {
            return $"{trimmed[0]}***{trimmed[(atIndex - 1)..]}";
        }

        if (trimmed.Length <= 4)
        {
            return new string('*', trimmed.Length);
        }

        return $"{new string('*', trimmed.Length - 4)}{trimmed[^4..]}";
    }
}
