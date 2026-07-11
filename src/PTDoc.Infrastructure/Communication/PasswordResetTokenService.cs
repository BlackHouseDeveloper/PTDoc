using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using System.Data.Common;

namespace PTDoc.Infrastructure.Communication;

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private const int MaxTokenLength = 4096;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<PasswordResetTokenService>? _logger;

    public PasswordResetTokenService(
        ApplicationDbContext db,
        ILogger<PasswordResetTokenService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PasswordResetCompletionResult> ResetPinAsync(
        PasswordResetCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsValidPin(request.NewPin))
        {
            return Failure(PasswordResetCompletionStatus.InvalidPin, "PIN must be exactly 4 digits.");
        }

        if (!IsSafeTokenInput(request.Token))
        {
            return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
        }

        try
        {
            var tokenHash = CommunicationText.HashToken(request.Token);
            var now = DateTimeOffset.UtcNow;

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
                            resetToken.UserId,
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

                    var claimed = await _db.PasswordResetTokens
                        .Where(resetToken =>
                            resetToken.TokenHash == tokenHash &&
                            resetToken.UsedAtUtc == null &&
                            resetToken.RevokedAtUtc == null &&
                            resetToken.ExpiresAtUtc > now)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(resetToken => resetToken.UsedAtUtc, now),
                            cancellationToken);

                    if (claimed != 1)
                    {
                        return Failure(PasswordResetCompletionStatus.AlreadyUsed, "The reset link is invalid or expired.");
                    }

                    var userUpdated = await _db.Users
                        .Where(user => user.Id == tokenMetadata.UserId)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(user => user.PinHash, AuthService.HashPin(request.NewPin)),
                            cancellationToken);
                    if (userUpdated != 1)
                    {
                        return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
                    }

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

            token.User.PinHash = AuthService.HashPin(request.NewPin);
            token.UsedAtUtc = now;
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
            var now = DateTimeOffset.UtcNow;
            var exists = await _db.PasswordResetTokens
                .AsNoTracking()
                .AnyAsync(resetToken =>
                    resetToken.TokenHash == tokenHash &&
                    resetToken.UsedAtUtc == null &&
                    resetToken.RevokedAtUtc == null &&
                    resetToken.ExpiresAtUtc > now,
                    cancellationToken);

            return new PasswordResetTokenValidationResult { IsValid = exists };
        }
        catch (Exception ex) when (IsExpectedResetTokenStorageException(ex))
        {
            _logger?.LogWarning(ex, "Password reset token validation failed safe because reset-token storage was unavailable or malformed.");
            return new PasswordResetTokenValidationResult { IsValid = false };
        }
    }

    private static bool IsSafeTokenInput(string? token)
        => !string.IsNullOrWhiteSpace(token) && token.Length <= MaxTokenLength;

    private static bool IsValidPin(string pin)
        => pin.Length == 4 && pin.All(char.IsDigit);

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
        string message)
        => new()
        {
            Succeeded = false,
            Status = status,
            SafeErrorMessage = message
        };
}
