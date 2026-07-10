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
                return await strategy.ExecuteAsync(async () =>
                {
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

                    var user = await _db.Users.FirstOrDefaultAsync(user => user.Id == tokenMetadata.UserId, cancellationToken);
                    if (user is null)
                    {
                        return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
                    }

                    user.PinHash = AuthService.HashPin(request.NewPin);
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return new PasswordResetCompletionResult
                    {
                        Succeeded = true,
                        Status = PasswordResetCompletionStatus.Succeeded
                    };
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

            return new PasswordResetCompletionResult
            {
                Succeeded = true,
                Status = PasswordResetCompletionStatus.Succeeded
            };
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
