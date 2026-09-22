using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using System.Data.Common;

namespace PTDoc.Infrastructure.Communication;

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private const int MaxTokenLength = 4096;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PasswordResetTokenService>? _logger;
    private readonly TimeProvider _timeProvider;

    public PasswordResetTokenService(
        ApplicationDbContext db,
        ILogger<PasswordResetTokenService>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PasswordResetCompletionResult> ResetPinAsync(
        PasswordResetCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSafeTokenInput(request.Token))
        {
            return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
        }

        try
        {
            var tokenHash = CommunicationText.HashToken(request.Token);
            var now = _timeProvider.GetUtcNow();

            if (_db.Database.IsRelational())
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                var attemptNumber = 0;
                return await strategy.ExecuteAsync(async () =>
                {
                    var isRetry = attemptNumber++ > 0;
                    await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                    var tokenMetadata = await _db.PasswordResetTokens
                        .AsNoTracking()
                        .Where(resetToken => resetToken.TokenHash == tokenHash)
                        .Select(resetToken => new
                        {
                            resetToken.Id,
                            resetToken.UserId,
                            ClinicId = resetToken.User == null ? null : resetToken.User.ClinicId,
                            resetToken.UsedAtUtc,
                            resetToken.RevokedAtUtc,
                            resetToken.ExpiresAtUtc
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (tokenMetadata is null)
                    {
                        return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
                    }

                    if (tokenMetadata.UsedAtUtc.HasValue)
                    {
                        if (isRetry
                            && !tokenMetadata.RevokedAtUtc.HasValue
                            && await DoesStoredPinMatchAsync(tokenMetadata.UserId, request.NewPin, cancellationToken))
                        {
                            return Succeeded();
                        }

                        return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
                    }

                    if (tokenMetadata.RevokedAtUtc.HasValue)
                    {
                        return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
                    }

                    if (tokenMetadata.ExpiresAtUtc <= now)
                    {
                        return Failure(PasswordResetCompletionStatus.Expired, "The reset link is invalid or expired.");
                    }

                    var minimumPinLength = await GetMinimumPinLengthAsync(
                        tokenMetadata.ClinicId, cancellationToken);
                    if (!IsValidPin(request.NewPin, minimumPinLength))
                    {
                        return Failure(
                            PasswordResetCompletionStatus.InvalidPin,
                            $"PIN must be {minimumPinLength} to 12 digits.",
                            minimumPinLength);
                    }

                    var claimTime = _timeProvider.GetUtcNow();
                    if (tokenMetadata.ExpiresAtUtc <= claimTime)
                    {
                        return Failure(PasswordResetCompletionStatus.Expired, "The reset link is invalid or expired.");
                    }

                    var claimed = await ClaimTokenAsync(
                        tokenMetadata.Id, claimTime, cancellationToken);

                    if (claimed != 1)
                    {
                        return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
                    }

                    var userUpdated = await _db.Users
                        .Where(user => user.Id == tokenMetadata.UserId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(user => user.PinHash, AuthService.HashPin(request.NewPin))
                                .SetProperty(user => user.MustChangePin, false)
                                .SetProperty(user => user.PinChangedAtUtc, DateTime.UtcNow)
                                .SetProperty(user => user.LegacyPinGraceEndsAtUtc, (DateTime?)null),
                            cancellationToken);
                    if (userUpdated != 1)
                    {
                        return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
                    }

                    var revokedAtUtc = claimTime.UtcDateTime;
                    await _db.Sessions
                        .Where(session => session.UserId == tokenMetadata.UserId && !session.IsRevoked)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(session => session.IsRevoked, true)
                                .SetProperty(session => session.RevokedAt, revokedAtUtc),
                            cancellationToken);
                    var subject = tokenMetadata.UserId.ToString();
                    await _db.StoredRefreshTokens
                        .Where(refreshToken => refreshToken.Subject == subject && !refreshToken.IsRevoked)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(refreshToken => refreshToken.IsRevoked, true)
                                .SetProperty(refreshToken => refreshToken.RevokedAtUtc, claimTime),
                            cancellationToken);

                    StagePinResetAudit(tokenMetadata.UserId, tokenMetadata.ClinicId);
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return Succeeded();
                });
            }

            var token = await _db.PasswordResetTokens
                .Include(resetToken => resetToken.User)
                .FirstOrDefaultAsync(resetToken => resetToken.TokenHash == tokenHash, cancellationToken);

            if (token is null || token.User is null)
            {
                return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
            }

            if (token.UsedAtUtc.HasValue)
            {
                return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
            }

            if (token.RevokedAtUtc.HasValue)
            {
                return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
            }

            if (token.ExpiresAtUtc <= now)
            {
                return Failure(PasswordResetCompletionStatus.Expired, "The reset link is invalid or expired.");
            }

            var minimumPinLength = await GetMinimumPinLengthAsync(
                token.User.ClinicId, cancellationToken);
            if (!IsValidPin(request.NewPin, minimumPinLength))
            {
                return Failure(
                    PasswordResetCompletionStatus.InvalidPin,
                    $"PIN must be {minimumPinLength} to 12 digits.",
                    minimumPinLength);
            }

            token.User.PinHash = AuthService.HashPin(request.NewPin);
            token.User.MustChangePin = false;
            token.User.PinChangedAtUtc = DateTime.UtcNow;
            token.User.LegacyPinGraceEndsAtUtc = null;
            token.UsedAtUtc = now;
            var activeSessions = await _db.Sessions
                .Where(session => session.UserId == token.UserId && !session.IsRevoked)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.IsRevoked = true;
                session.RevokedAt = now.UtcDateTime;
            }

            var subject = token.UserId.ToString();
            var activeRefreshTokens = await _db.StoredRefreshTokens
                .Where(refreshToken => refreshToken.Subject == subject && !refreshToken.IsRevoked)
                .ToListAsync(cancellationToken);
            foreach (var refreshToken in activeRefreshTokens)
            {
                refreshToken.IsRevoked = true;
                refreshToken.RevokedAtUtc = now;
            }

            StagePinResetAudit(token.UserId, token.User.ClinicId);
            await _db.SaveChangesAsync(cancellationToken);

            return Succeeded();
        }
        catch (Exception ex) when (IsExpectedResetTokenStorageException(ex))
        {
            _logger?.LogWarning(ex, "Password reset completion failed safe because reset-token storage was unavailable or malformed.");
            return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
        }
    }

    public async Task<PasswordResetTokenValidationResult> ValidateTokenAsync(
        PasswordResetTokenValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsSafeTokenInput(request.Token))
        {
            return new PasswordResetTokenValidationResult { IsValid = false };
        }

        try
        {
            var tokenHash = CommunicationText.HashToken(request.Token);
            var now = _timeProvider.GetUtcNow();
            var tokenMetadata = await _db.PasswordResetTokens
                .AsNoTracking()
                .Where(resetToken =>
                    resetToken.TokenHash == tokenHash
                    && resetToken.UsedAtUtc == null
                    && resetToken.RevokedAtUtc == null
                    && resetToken.User != null)
                .Select(resetToken => new
                {
                    resetToken.User!.ClinicId,
                    resetToken.ExpiresAtUtc
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (tokenMetadata is null || tokenMetadata.ExpiresAtUtc <= now)
            {
                return new PasswordResetTokenValidationResult { IsValid = false };
            }

            return new PasswordResetTokenValidationResult
            {
                IsValid = true,
                MinimumPinLength = await GetMinimumPinLengthAsync(tokenMetadata.ClinicId, cancellationToken)
            };
        }
        catch (Exception ex) when (IsExpectedResetTokenStorageException(ex))
        {
            _logger?.LogWarning(ex, "Password reset token validation failed safe because reset-token storage was unavailable or malformed.");
            return new PasswordResetTokenValidationResult { IsValid = false };
        }
    }

    private static bool IsSafeTokenInput(string? token)
        => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxTokenLength;

    private static bool IsValidPin(string pin, int minimumPinLength)
        => pin.Length >= minimumPinLength && pin.Length <= 12 && pin.All(char.IsDigit);

    private void StagePinResetAudit(Guid userId, Guid? clinicId)
    {
        _db.AuditLogs.Add(AuditService.CreateAuditLog(new AuditEvent
        {
            EventType = "PinChanged",
            UserId = userId,
            EntityType = nameof(User),
            EntityId = userId,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId?.ToString() ?? "none",
                ["reasonCode"] = "password_reset"
            }
        }));
    }

    private async Task<int> GetMinimumPinLengthAsync(
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        if (!clinicId.HasValue)
        {
            return PinPolicyRules.MinimumLength;
        }

        var configuredMinimum = await _db.ClinicSecurityPolicies
            .Where(policy => policy.ClinicId == clinicId.Value)
            .Select(policy => (int?)policy.MinimumPinLength)
            .SingleOrDefaultAsync(cancellationToken)
            ?? PinPolicyRules.MinimumLength;
        return PinPolicyRules.NormalizeMinimumLength(configuredMinimum);
    }

    private async Task<int> ClaimTokenAsync(
        Guid tokenId,
        DateTimeOffset claimedAtUtc,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                _db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.Sqlite",
                StringComparison.Ordinal))
        {
            return await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE "PasswordResetTokens"
                SET "UsedAtUtc" = {claimedAtUtc}
                WHERE "Id" = {tokenId}
                  AND "UsedAtUtc" IS NULL
                  AND "RevokedAtUtc" IS NULL
                  AND julianday("ExpiresAtUtc") > julianday({claimedAtUtc})
                """,
                cancellationToken);
        }

        return await _db.PasswordResetTokens
            .Where(resetToken =>
                resetToken.Id == tokenId &&
                resetToken.UsedAtUtc == null &&
                resetToken.RevokedAtUtc == null &&
                resetToken.ExpiresAtUtc > claimedAtUtc)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(resetToken => resetToken.UsedAtUtc, claimedAtUtc),
                cancellationToken);
    }

    private static bool IsExpectedResetTokenStorageException(Exception ex)
        => ex is DbException or InvalidOperationException or FormatException or OverflowException;

    private async Task<bool> DoesStoredPinMatchAsync(Guid userId, string requestedPin, CancellationToken cancellationToken)
    {
        var storedHash = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.PinHash)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(requestedPin, storedHash);
        }
        catch
        {
            return false;
        }
    }

    private static PasswordResetCompletionResult Succeeded()
        => new()
        {
            Succeeded = true,
            Status = PasswordResetCompletionStatus.Succeeded
        };

    private static PasswordResetCompletionResult Failure(
        PasswordResetCompletionStatus status,
        string message,
        int? minimumPinLength = null)
        => new()
        {
            Succeeded = false,
            Status = status,
            SafeErrorMessage = message,
            MinimumPinLength = minimumPinLength
        };
}
