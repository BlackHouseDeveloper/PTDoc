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
using PTDoc.Application.Communication;
using PTDoc.Application.Intake;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Production implementation of <see cref="IIntakeInviteService"/> that uses signed JWT invite links,
/// short-lived access tokens, and canonical outbound communication delivery.
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
    private readonly ICommunicationService _communicationService;
    private readonly IContactNormalizer _contactNormalizer;
    private readonly IAuditService _auditService;
    private readonly ILogger<JwtIntakeInviteService> _logger;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public JwtIntakeInviteService(
        IOptions<IntakeInviteOptions> options,
        ApplicationDbContext db,
        ICommunicationService communicationService,
        IContactNormalizer contactNormalizer,
        IAuditService auditService,
        ILogger<JwtIntakeInviteService> logger)
    {
        _options = options.Value;
        _db = db;
        _communicationService = communicationService;
        _contactNormalizer = contactNormalizer;
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
        var baseUrl = NormalizePublicWebBaseUrl(_options.PublicWebBaseUrl);
        if (!string.IsNullOrWhiteSpace(intake.InviteToken) &&
            intake.ExpiresAt.HasValue &&
            intake.ExpiresAt.Value > now.UtcDateTime &&
            !string.IsNullOrWhiteSpace(intake.AccessToken))
        {
            return new IntakeInviteLinkResult(
                true,
                intake.Id,
                intake.PatientId,
                BuildInviteUrl(baseUrl, intake.PatientId, intake.InviteToken),
                new DateTimeOffset(DateTime.SpecifyKind(intake.ExpiresAt.Value, DateTimeKind.Utc)),
                null);
        }

        var expiry = now.AddMinutes(Math.Max(1, _options.InviteExpiryMinutes));
        var rawSecret = GenerateInviteSecret();

        intake.AccessToken = HashInviteSecret(rawSecret);
        intake.ExpiresAt = expiry.UtcDateTime;

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

        intake.InviteToken = token;
        await _db.SaveChangesAsync(cancellationToken);

        var inviteUrl = BuildInviteUrl(baseUrl, intake.PatientId, token);

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

    public async Task<bool> SendOtpAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateInviteOtpContextAsync(inviteToken, contact, channel, cancellationToken);
        if (!validation.Success)
        {
            return false;
        }

        var normalizedContact = validation.NormalizedContact;
        var deliveryChannel = ToDeliveryChannel(channel);
        var now = DateTimeOffset.UtcNow;
        var contactHash = HashOtpContact(validation.IntakeId, deliveryChannel, normalizedContact);
        var challenge = await _db.IntakeOtpChallenges
            .FirstOrDefaultAsync(item =>
                item.IntakeId == validation.IntakeId &&
                item.Channel == deliveryChannel &&
                item.ContactHash == contactHash,
                cancellationToken);

        if (challenge is not null && now - challenge.WindowStartUtc < OtpRateLimitWindow)
        {
            if (challenge.SendCount >= MaxOtpRequestsPerWindow)
            {
                await LogOtpEventAsync("IntakeOtpDeliveryFailed", channel, normalizedContact, success: false, provider: null, providerMessageId: null, error: "RateLimitExceeded", cancellationToken);
                _logger.LogWarning("OTP send rate limit exceeded for intake contact.");
                return false;
            }

            challenge.SendCount += 1;
        }
        else
        {
            challenge ??= new IntakeOtpChallenge
            {
                Id = Guid.NewGuid(),
                IntakeId = validation.IntakeId,
                PatientId = validation.PatientId,
                ClinicId = validation.ClinicId,
                Channel = deliveryChannel,
                ContactHash = contactHash,
                CreatedAtUtc = now,
                CorrelationId = null
            };

            challenge.WindowStartUtc = now;
            challenge.SendCount = 1;
        }

        var otp = GenerateOtp();
        challenge.PatientId = validation.PatientId;
        challenge.ClinicId = validation.ClinicId;
        challenge.OtpHash = HashOtp(otp);
        challenge.ExpiresAtUtc = now.AddMinutes(_options.OtpExpiryMinutes);
        challenge.UpdatedAtUtc = now;
        challenge.FailedVerifyCount = 0;
        challenge.LastFailedVerifyAtUtc = null;
        challenge.ConsumedAtUtc = null;

        if (_db.Entry(challenge).State == EntityState.Detached)
        {
            _db.IntakeOtpChallenges.Add(challenge);
        }

        await _db.SaveChangesAsync(cancellationToken);

        (bool Success, string Provider, string? ProviderMessageId, string? ErrorMessage) delivery;
        try
        {
            delivery = await DeliverOtpAsync(normalizedContact, otp, channel, validation.PatientId, validation.ClinicId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OTP delivery failed before provider acceptance. Channel={Channel}", channel);
            delivery = (false, "InternalDeliveryException", null, "Unable to send a verification code right now.");
        }

        if (!delivery.Success)
        {
            await MarkOtpChallengeDeliveryFailedAsync(challenge, cancellationToken);
            await LogOtpEventAsync("IntakeOtpDeliveryFailed", channel, normalizedContact, success: false, provider: delivery.Provider, providerMessageId: delivery.ProviderMessageId, error: delivery.ErrorMessage, cancellationToken);
            return false;
        }

        await LogOtpEventAsync("IntakeOtpDelivered", channel, normalizedContact, success: true, provider: delivery.Provider, providerMessageId: delivery.ProviderMessageId, error: null, cancellationToken);
        return true;
    }

    public Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        string otpCode,
        CancellationToken cancellationToken = default)
        => VerifyOtpAndIssueAccessTokenCoreAsync(inviteToken, contact, channel, otpCode, cancellationToken);

    private async Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenCoreAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        string otpCode,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateInviteOtpContextAsync(inviteToken, contact, channel, cancellationToken);
        if (!validation.Success)
        {
            return new IntakeInviteResult(false, null, null, validation.Error ?? "Invalid or expired invite link.");
        }

        var normalizedContact = validation.NormalizedContact;
        var deliveryChannel = ToDeliveryChannel(channel);
        var contactHash = HashOtpContact(validation.IntakeId, deliveryChannel, normalizedContact);
        var challenge = await _db.IntakeOtpChallenges
            .FirstOrDefaultAsync(item =>
                item.IntakeId == validation.IntakeId &&
                item.Channel == deliveryChannel &&
                item.ContactHash == contactHash,
                cancellationToken);

        if (challenge is not null && challenge.FailedVerifyCount >= MaxFailedVerifyAttempts)
        {
            return new IntakeInviteResult(false, null, null, "Too many incorrect attempts. Please request a new code.");
        }

        if (challenge is null || challenge.ConsumedAtUtc.HasValue || challenge.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return new IntakeInviteResult(false, null, null, "Invalid or expired code.");
        }

        if (!VerifyOtp(otpCode, challenge.OtpHash))
        {
            await IncrementOtpFailureAsync(
                validation.IntakeId,
                deliveryChannel,
                contactHash,
                challenge.OtpHash,
                DateTimeOffset.UtcNow,
                cancellationToken);

            return new IntakeInviteResult(false, null, null, "Incorrect code. Please try again.");
        }

        var consumed = await TryConsumeOtpChallengeAsync(
            validation.IntakeId,
            deliveryChannel,
            contactHash,
            challenge.OtpHash,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (!consumed)
        {
            return new IntakeInviteResult(false, null, null, "Invalid or expired code.");
        }

        return IssueAccessToken(patientId: validation.PatientId, intakeId: validation.IntakeId, contact: normalizedContact);
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

    private async Task<bool> TryConsumeOtpChallengeAsync(
        Guid intakeId,
        DeliveryChannel channel,
        string contactHash,
        string otpHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            var sql = BuildOtpConsumeSql();
            var updated = await _db.Database.ExecuteSqlRawAsync(
                sql,
                [
                    now,
                    intakeId,
                    channel.ToString(),
                    contactHash,
                    otpHash,
                    MaxFailedVerifyAttempts
                ],
                cancellationToken);

            return updated == 1;
        }

        var challenge = await _db.IntakeOtpChallenges
            .FirstOrDefaultAsync(item =>
                item.IntakeId == intakeId &&
                item.Channel == channel &&
                item.ContactHash == contactHash &&
                item.OtpHash == otpHash,
                cancellationToken);

        if (challenge is null ||
            challenge.ConsumedAtUtc.HasValue ||
            challenge.ExpiresAtUtc < now ||
            challenge.FailedVerifyCount >= MaxFailedVerifyAttempts)
        {
            return false;
        }

        challenge.ConsumedAtUtc = now;
        challenge.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task IncrementOtpFailureAsync(
        Guid intakeId,
        DeliveryChannel channel,
        string contactHash,
        string otpHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_db.Database.IsRelational())
        {
            var sql = BuildOtpFailureIncrementSql();
            await _db.Database.ExecuteSqlRawAsync(
                sql,
                [
                    now,
                    intakeId,
                    channel.ToString(),
                    contactHash,
                    otpHash,
                    MaxFailedVerifyAttempts
                ],
                cancellationToken);
            return;
        }

        var challenge = await _db.IntakeOtpChallenges
            .FirstOrDefaultAsync(item =>
                item.IntakeId == intakeId &&
                item.Channel == channel &&
                item.ContactHash == contactHash &&
                item.OtpHash == otpHash &&
                item.ConsumedAtUtc == null &&
                item.ExpiresAtUtc >= now &&
                item.FailedVerifyCount < MaxFailedVerifyAttempts,
                cancellationToken);

        if (challenge is null)
        {
            return;
        }

        challenge.FailedVerifyCount += 1;
        challenge.LastFailedVerifyAtUtc = now;
        challenge.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkOtpChallengeDeliveryFailedAsync(
        IntakeOtpChallenge challenge,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        challenge.ConsumedAtUtc = now;
        challenge.ExpiresAtUtc = now;
        challenge.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private string BuildOtpConsumeSql()
    {
        var names = SqlNames(_db.Database.ProviderName);
        return "UPDATE " + names.Table + " " +
            "SET " + names.ConsumedAtUtc + " = {0}, " + names.UpdatedAtUtc + " = {0} " +
            "WHERE " + names.IntakeId + " = {1} " +
            "AND " + names.Channel + " = {2} " +
            "AND " + names.ContactHash + " = {3} " +
            "AND " + names.OtpHash + " = {4} " +
            "AND " + names.ConsumedAtUtc + " IS NULL " +
            "AND " + names.FailedVerifyCount + " < {5}";
    }

    private string BuildOtpFailureIncrementSql()
    {
        var names = SqlNames(_db.Database.ProviderName);
        return "UPDATE " + names.Table + " " +
            "SET " + names.FailedVerifyCount + " = " + names.FailedVerifyCount + " + 1, " +
            names.LastFailedVerifyAtUtc + " = {0}, " +
            names.UpdatedAtUtc + " = {0} " +
            "WHERE " + names.IntakeId + " = {1} " +
            "AND " + names.Channel + " = {2} " +
            "AND " + names.ContactHash + " = {3} " +
            "AND " + names.OtpHash + " = {4} " +
            "AND " + names.ConsumedAtUtc + " IS NULL " +
            "AND " + names.FailedVerifyCount + " < {5}";
    }

    private static OtpSqlNames SqlNames(string? providerName)
    {
        if (providerName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new(
                "[IntakeOtpChallenges]",
                "[IntakeId]",
                "[Channel]",
                "[ContactHash]",
                "[OtpHash]",
                "[ConsumedAtUtc]",
                "[ExpiresAtUtc]",
                "[FailedVerifyCount]",
                "[LastFailedVerifyAtUtc]",
                "[UpdatedAtUtc]");
        }

        return new(
            "\"IntakeOtpChallenges\"",
            "\"IntakeId\"",
            "\"Channel\"",
            "\"ContactHash\"",
            "\"OtpHash\"",
            "\"ConsumedAtUtc\"",
            "\"ExpiresAtUtc\"",
            "\"FailedVerifyCount\"",
            "\"LastFailedVerifyAtUtc\"",
            "\"UpdatedAtUtc\"");
    }

    private async Task<OtpContextValidation> ValidateInviteOtpContextAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inviteToken))
        {
            return OtpContextValidation.Fail("A valid invite link is required before sending a code.");
        }

        if (channel is not (OtpChannel.Email or OtpChannel.Sms))
        {
            return OtpContextValidation.Fail("Choose email or SMS delivery.");
        }

        var normalizedContact = channel == OtpChannel.Email
            ? _contactNormalizer.NormalizeEmail(contact)
            : _contactNormalizer.NormalizePhone(contact);

        if (!normalizedContact.Succeeded)
        {
            return OtpContextValidation.Fail(normalizedContact.SafeErrorMessage ?? "Enter a valid contact method.");
        }

        try
        {
            var principal = ValidateJwt(inviteToken, InviteAudience, InviteTypeClaim, out _);
            var intakeId = ReadGuidClaim(principal, IntakeIdClaim);
            var patientId = ReadGuidClaim(principal, PatientIdClaim);
            var rawSecret = ReadClaimValue(principal, InviteSecretClaim);

            if (intakeId == Guid.Empty || patientId == Guid.Empty || string.IsNullOrWhiteSpace(rawSecret))
            {
                return OtpContextValidation.Fail("Invite link is invalid or has expired.");
            }

            var intake = await _db.IntakeForms
                .Include(form => form.Patient)
                .AsNoTracking()
                .FirstOrDefaultAsync(form => form.Id == intakeId, cancellationToken);

            if (intake is null || intake.PatientId != patientId || intake.Patient is null)
            {
                return OtpContextValidation.Fail("Invite link is invalid or has expired.");
            }

            if (intake.IsLocked || intake.SubmittedAt.HasValue)
            {
                return OtpContextValidation.Fail("This intake has already been submitted.");
            }

            if (intake.ExpiresAt.HasValue && intake.ExpiresAt.Value < DateTime.UtcNow)
            {
                return OtpContextValidation.Fail("Invite link is invalid or has expired.");
            }

            if (!VerifyInviteSecret(rawSecret, intake.AccessToken))
            {
                return OtpContextValidation.Fail("Invite link is invalid or has expired.");
            }

            var expectedContact = channel == OtpChannel.Email
                ? _contactNormalizer.NormalizeEmail(intake.Patient.Email)
                : _contactNormalizer.NormalizePhone(intake.Patient.Phone);

            if (!expectedContact.Succeeded ||
                !string.Equals(expectedContact.NormalizedValue, normalizedContact.NormalizedValue, StringComparison.Ordinal))
            {
                return OtpContextValidation.Fail("Unable to send a code for that contact method.");
            }

            return new OtpContextValidation(
                true,
                intake.Id,
                intake.PatientId,
                intake.ClinicId,
                normalizedContact.NormalizedValue,
                null);
        }
        catch (SecurityTokenException)
        {
            return OtpContextValidation.Fail("Invite link is invalid or has expired.");
        }
    }

    private async Task<(bool Success, string Provider, string? ProviderMessageId, string? ErrorMessage)> DeliverOtpAsync(
        string contact,
        string otp,
        OtpChannel channel,
        Guid patientId,
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        var request = new IntakeOtpDeliveryRequest
        {
            PatientId = patientId,
            ClinicId = clinicId,
            Recipient = contact,
            OtpCode = otp,
            ExpiresInMinutes = _options.OtpExpiryMinutes
        };

        if (channel == OtpChannel.Email)
        {
            var result = await _communicationService.SendIntakeOtpEmailAsync(request, cancellationToken);
            return (result.Succeeded, result.Provider ?? "Unknown", result.ProviderMessageId, result.SafeErrorMessage);
        }

        var smsResult = await _communicationService.SendIntakeOtpSmsAsync(request, cancellationToken);
        return (smsResult.Succeeded, smsResult.Provider ?? "Unknown", smsResult.ProviderMessageId, smsResult.SafeErrorMessage);
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
            throw new InvalidOperationException(
                "IntakeInvite:PublicWebBaseUrl is not configured. " +
                "Set an absolute HTTPS URL (e.g., https://app.example.com) before generating invite links.");
        }

        var trimmed = value.TrimEnd('/');

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"IntakeInvite:PublicWebBaseUrl '{trimmed}' is not a valid absolute URL.");
        }

        // Allow HTTP only for loopback hosts (development: localhost, 127.0.0.1, ::1).
        // All non-loopback URLs must use HTTPS to protect patient data in transit.
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"IntakeInvite:PublicWebBaseUrl '{trimmed}' must use HTTPS for non-localhost hosts. " +
                "HTTP invite links would expose patient data over an insecure channel.");
        }

        return trimmed;
    }

    private static string BuildInviteUrl(string baseUrl, Guid patientId, string token)
        => $"{baseUrl}/intake/{patientId:D}?mode=patient&invite={Uri.EscapeDataString(token)}";

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

    private string HashOtpContact(Guid intakeId, DeliveryChannel channel, string normalizedContact)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_options.SigningKey);
        var value = $"{intakeId:N}|{channel}|{normalizedContact}";
        var hash = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static DeliveryChannel ToDeliveryChannel(OtpChannel channel)
        => channel == OtpChannel.Email ? DeliveryChannel.Email : DeliveryChannel.Sms;

    private readonly record struct OtpContextValidation(
        bool Success,
        Guid IntakeId,
        Guid PatientId,
        Guid? ClinicId,
        string NormalizedContact,
        string? Error)
    {
        public static OtpContextValidation Fail(string error)
            => new(false, Guid.Empty, Guid.Empty, null, string.Empty, error);
    }

    private readonly record struct OtpSqlNames(
        string Table,
        string IntakeId,
        string Channel,
        string ContactHash,
        string OtpHash,
        string ConsumedAtUtc,
        string ExpiresAtUtc,
        string FailedVerifyCount,
        string LastFailedVerifyAtUtc,
        string UpdatedAtUtc);
}
