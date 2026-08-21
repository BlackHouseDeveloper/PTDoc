using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using QRCoder;

namespace PTDoc.Infrastructure.Settings;

public sealed class MfaAuthenticationService(
    ApplicationDbContext context,
    ISettingsSecretProtector protector,
    IAuditService auditService,
    TimeProvider timeProvider) : IMfaAuthenticationService
{
    private const string ChallengeProtectionPurpose = "mfa-challenge";
    private const string SecretProtectionPurpose = "totp-secret";
    private const int TotpPeriodSeconds = 30;
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LoginChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EnrollmentChallengeLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    public string CreateChallenge(Guid userId, MfaChallengePurpose purpose) =>
        ProtectChallenge(new ChallengePayload(userId, purpose, null));

    public bool TryValidateChallenge(
        string challengeToken,
        MfaChallengePurpose purpose,
        TimeSpan maximumAge,
        out MfaChallengePrincipal principal)
    {
        principal = default!;
        if (!TryReadChallenge(challengeToken, maximumAge, out var payload)
            || payload.Purpose != purpose
            || payload.UserId == Guid.Empty)
        {
            return false;
        }

        principal = new MfaChallengePrincipal(payload.UserId, payload.Purpose);
        return true;
    }

    public async Task<SettingsOperationResult<MfaEnrollmentStart>> BeginEnrollmentAsync(
        string loginChallengeToken,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadChallenge(loginChallengeToken, LoginChallengeLifetime, out var loginChallenge)
            || loginChallenge.Purpose != MfaChallengePurpose.Enrollment)
        {
            return SettingsOperationResult<MfaEnrollmentStart>.Forbidden("invalid_or_expired_challenge");
        }

        var user = await context.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.Id == loginChallenge.UserId && item.IsActive, cancellationToken);
        if (user is null)
        {
            return SettingsOperationResult<MfaEnrollmentStart>.NotFound();
        }

        var credential = await context.UserMfaCredentials
            .SingleOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (credential?.IsActive == true)
        {
            return SettingsOperationResult<MfaEnrollmentStart>.Forbidden("mfa_already_enrolled");
        }

        var secret = RandomNumberGenerator.GetBytes(20);
        var manualKey = Base32Encode(secret);
        if (credential is null)
        {
            credential = new UserMfaCredential { UserId = user.Id };
            context.UserMfaCredentials.Add(credential);
        }
        else
        {
            context.UserMfaRecoveryCodes.RemoveRange(
                context.UserMfaRecoveryCodes.Where(item => item.UserMfaCredentialId == credential.Id));
        }

        credential.EncryptedSecret = protector.Protect(SecretProtectionPurpose, Convert.ToBase64String(secret));
        credential.IsActive = false;
        credential.LastAcceptedTimeStep = -1;
        credential.FailedAttemptCount = 0;
        credential.LockedUntilUtc = null;
        credential.CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        credential.ActivatedAtUtc = null;
        credential.ResetAtUtc = null;
        credential.ResetByUserId = null;
        await context.SaveChangesAsync(cancellationToken);

        var accountLabel = Uri.EscapeDataString(user.Username);
        var issuer = Uri.EscapeDataString("PTDoc");
        var uri = $"otpauth://totp/PTDoc:{accountLabel}?secret={manualKey}&issuer={issuer}&algorithm=SHA1&digits=6&period={TotpPeriodSeconds}";
        var enrollmentChallenge = ProtectChallenge(new ChallengePayload(user.Id, MfaChallengePurpose.Enrollment, credential.Id));
        using var qrData = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var qrSvg = new SvgQRCode(qrData).GetGraphic(4);

        await AuditAsync("MfaEnrollmentStarted", user, credential.Id, cancellationToken);
        return SettingsOperationResult<MfaEnrollmentStart>.Success(
            new MfaEnrollmentStart(manualKey, uri, qrSvg, enrollmentChallenge));
    }

    public async Task<SettingsOperationResult<MfaEnrollmentCompletion>> VerifyEnrollmentAsync(
        string enrollmentChallengeToken,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadChallenge(enrollmentChallengeToken, EnrollmentChallengeLifetime, out var challenge)
            || challenge.Purpose != MfaChallengePurpose.Enrollment
            || challenge.CredentialId is null)
        {
            return SettingsOperationResult<MfaEnrollmentCompletion>.Forbidden("invalid_or_expired_challenge");
        }

        var credential = await context.UserMfaCredentials
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == challenge.CredentialId && item.UserId == challenge.UserId, cancellationToken);
        if (credential?.User is null || credential.IsActive)
        {
            return SettingsOperationResult<MfaEnrollmentCompletion>.Forbidden("invalid_enrollment_state");
        }

        if (!TryVerifyCode(credential, code, out var acceptedTimeStep))
        {
            await RegisterFailureAsync(credential, cancellationToken);
            return SettingsOperationResult<MfaEnrollmentCompletion>.Validation(
                new Dictionary<string, string[]> { ["code"] = ["The authenticator code is invalid or expired."] });
        }

        credential.IsActive = true;
        credential.ActivatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        credential.LastAcceptedTimeStep = acceptedTimeStep;
        credential.FailedAttemptCount = 0;
        credential.LockedUntilUtc = null;

        var recoveryCodes = GenerateRecoveryCodes();
        foreach (var recoveryCode in recoveryCodes)
        {
            context.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
            {
                UserMfaCredentialId = credential.Id,
                CodeHash = BCrypt.Net.BCrypt.HashPassword(NormalizeRecoveryCode(recoveryCode), 12),
                CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync("MfaEnrollmentCompleted", credential.User, credential.Id, cancellationToken);
        var completion = CreateChallenge(credential.UserId, MfaChallengePurpose.AuthenticationCompletion);
        return SettingsOperationResult<MfaEnrollmentCompletion>.Success(new MfaEnrollmentCompletion(recoveryCodes, completion));
    }

    public Task<MfaVerificationResult> VerifyAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default) =>
        VerifyCoreAsync(challengeToken, code, useRecoveryCode: false, cancellationToken);

    public Task<MfaVerificationResult> RecoverAsync(
        string challengeToken,
        string recoveryCode,
        CancellationToken cancellationToken = default) =>
        VerifyCoreAsync(challengeToken, recoveryCode, useRecoveryCode: true, cancellationToken);

    public async Task<SettingsOperationResult<MfaRecoveryCodeSet>> RegenerateRecoveryCodesAsync(
        Guid userId,
        string currentTotpCode,
        CancellationToken cancellationToken = default)
    {
        var credential = await context.UserMfaCredentials
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.IsActive, cancellationToken);
        if (credential?.User is null)
        {
            return SettingsOperationResult<MfaRecoveryCodeSet>.NotFound();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (credential.LockedUntilUtc > now)
        {
            return SettingsOperationResult<MfaRecoveryCodeSet>.Forbidden("mfa_temporarily_locked");
        }

        if (!TryVerifyCode(credential, currentTotpCode, out var acceptedTimeStep)
            || !AcceptTimeStep(credential, acceptedTimeStep))
        {
            await RegisterFailureAsync(credential, cancellationToken);
            await AuditAsync("MfaRecoveryCodesRegenerationFailed", credential.User, credential.Id, cancellationToken);
            return SettingsOperationResult<MfaRecoveryCodeSet>.Validation(
                new Dictionary<string, string[]> { ["code"] = ["The authenticator code is invalid or expired."] });
        }

        var priorCodes = await context.UserMfaRecoveryCodes
            .Where(item => item.UserMfaCredentialId == credential.Id)
            .ToListAsync(cancellationToken);
        context.UserMfaRecoveryCodes.RemoveRange(priorCodes);

        var recoveryCodes = GenerateRecoveryCodes();
        foreach (var recoveryCode in recoveryCodes)
        {
            context.UserMfaRecoveryCodes.Add(new UserMfaRecoveryCode
            {
                UserMfaCredentialId = credential.Id,
                CodeHash = BCrypt.Net.BCrypt.HashPassword(NormalizeRecoveryCode(recoveryCode), 12),
                CreatedAtUtc = now
            });
        }

        credential.FailedAttemptCount = 0;
        credential.LockedUntilUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync("MfaRecoveryCodesRegenerated", credential.User, credential.Id, cancellationToken);
        return SettingsOperationResult<MfaRecoveryCodeSet>.Success(new MfaRecoveryCodeSet(recoveryCodes));
    }

    private async Task<MfaVerificationResult> VerifyCoreAsync(
        string challengeToken,
        string suppliedCode,
        bool useRecoveryCode,
        CancellationToken cancellationToken)
    {
        if (!TryReadChallenge(challengeToken, LoginChallengeLifetime, out var challenge)
            || challenge.Purpose != MfaChallengePurpose.Verification)
        {
            return new MfaVerificationResult(false, null, "invalid_or_expired_challenge");
        }

        var credential = await context.UserMfaCredentials
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.UserId == challenge.UserId && item.IsActive, cancellationToken);
        if (credential?.User is null)
        {
            return new MfaVerificationResult(false, null, "mfa_not_enrolled");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (credential.LockedUntilUtc > now)
        {
            return new MfaVerificationResult(false, null, "mfa_temporarily_locked");
        }

        var accepted = useRecoveryCode
            ? await ConsumeRecoveryCodeAsync(credential, suppliedCode, cancellationToken)
            : TryVerifyCode(credential, suppliedCode, out var acceptedTimeStep)
                && AcceptTimeStep(credential, acceptedTimeStep);
        if (!accepted)
        {
            await RegisterFailureAsync(credential, cancellationToken);
            await AuditAsync(useRecoveryCode ? "MfaRecoveryFailed" : "MfaVerificationFailed", credential.User, credential.Id, cancellationToken);
            return new MfaVerificationResult(false, null, "invalid_code");
        }

        credential.FailedAttemptCount = 0;
        credential.LockedUntilUtc = null;
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync(useRecoveryCode ? "MfaRecoveryUsed" : "MfaVerified", credential.User, credential.Id, cancellationToken);

        var completion = CreateChallenge(credential.UserId, MfaChallengePurpose.AuthenticationCompletion);
        return new MfaVerificationResult(true, completion);
    }

    private bool TryVerifyCode(UserMfaCredential credential, string code, out long acceptedTimeStep)
    {
        acceptedTimeStep = -1;
        if (code is null || code.Length != 6 || code.Any(character => !char.IsDigit(character)))
        {
            return false;
        }

        if (!protector.TryUnprotect(SecretProtectionPurpose, credential.EncryptedSecret, TimeSpan.FromDays(3650), out var protectedSecret))
        {
            return false;
        }

        byte[] secret;
        try { secret = Convert.FromBase64String(protectedSecret); }
        catch (FormatException) { return false; }

        var currentStep = timeProvider.GetUtcNow().ToUnixTimeSeconds() / TotpPeriodSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var candidateStep = currentStep + offset;
            if (candidateStep <= credential.LastAcceptedTimeStep)
            {
                continue;
            }

            var expected = ComputeTotp(secret, candidateStep);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(code)))
            {
                acceptedTimeStep = candidateStep;
                return true;
            }
        }

        return false;
    }

    private static bool AcceptTimeStep(UserMfaCredential credential, long acceptedTimeStep)
    {
        if (acceptedTimeStep <= credential.LastAcceptedTimeStep)
        {
            return false;
        }

        credential.LastAcceptedTimeStep = acceptedTimeStep;
        return true;
    }

    private async Task<bool> ConsumeRecoveryCodeAsync(
        UserMfaCredential credential,
        string suppliedCode,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeRecoveryCode(suppliedCode);
        if (normalized.Length != 12)
        {
            return false;
        }

        var candidates = await context.UserMfaRecoveryCodes
            .Where(item => item.UserMfaCredentialId == credential.Id && item.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(item => BCrypt.Net.BCrypt.Verify(normalized, item.CodeHash));
        if (match is null)
        {
            return false;
        }

        match.UsedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return true;
    }

    private async Task RegisterFailureAsync(UserMfaCredential credential, CancellationToken cancellationToken)
    {
        credential.FailedAttemptCount++;
        if (credential.FailedAttemptCount >= MaximumFailedAttempts)
        {
            credential.FailedAttemptCount = 0;
            credential.LockedUntilUtc = timeProvider.GetUtcNow().UtcDateTime.Add(LockoutDuration);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private string ProtectChallenge(ChallengePayload payload)
    {
        var serialized = string.Join('|',
            payload.UserId.ToString("N"),
            ((int)payload.Purpose).ToString(CultureInfo.InvariantCulture),
            payload.CredentialId?.ToString("N") ?? string.Empty);
        return protector.Protect(ChallengeProtectionPurpose, serialized);
    }

    private bool TryReadChallenge(string token, TimeSpan maximumAge, out ChallengePayload payload)
    {
        payload = default!;
        if (!protector.TryUnprotect(ChallengeProtectionPurpose, token, maximumAge, out var serialized))
        {
            return false;
        }

        var parts = serialized.Split('|');
        if (parts.Length != 3
            || !Guid.TryParseExact(parts[0], "N", out var userId)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var purposeValue)
            || !Enum.IsDefined(typeof(MfaChallengePurpose), purposeValue))
        {
            return false;
        }

        Guid? credentialId = null;
        if (parts[2].Length > 0)
        {
            if (!Guid.TryParseExact(parts[2], "N", out var parsedCredentialId)) return false;
            credentialId = parsedCredentialId;
        }

        payload = new ChallengePayload(userId, (MfaChallengePurpose)purposeValue, credentialId);
        return true;
    }

    private async Task AuditAsync(string eventType, User user, Guid credentialId, CancellationToken cancellationToken)
    {
        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = eventType,
            UserId = user.Id,
            EntityType = nameof(UserMfaCredential),
            EntityId = credentialId,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = user.ClinicId?.ToString() ?? "none",
                ["userId"] = user.Id
            }
        }, cancellationToken);
    }

    private static string ComputeTotp(byte[] secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(timeStep & 0xff);
            timeStep >>= 8;
        }

        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0) result.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return result.ToString();
    }

    private static IReadOnlyList<string> GenerateRecoveryCodes() => Enumerable.Range(0, 10)
        .Select(_ =>
        {
            var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            return $"{value[..4]}-{value[4..8]}-{value[8..]}";
        })
        .ToArray();

    private static string NormalizeRecoveryCode(string value) =>
        new(value.Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed record ChallengePayload(Guid UserId, MfaChallengePurpose Purpose, Guid? CredentialId);
}
