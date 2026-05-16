using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Communication;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Infrastructure.Communication;

public sealed class PasswordResetTokenService : IPasswordResetTokenService
{
    private readonly ApplicationDbContext _db;

    public PasswordResetTokenService(ApplicationDbContext db)
    {
        _db = db;
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

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Failure(PasswordResetCompletionStatus.InvalidToken, "The reset link is invalid or expired.");
        }

        var tokenHash = CommunicationText.HashToken(request.Token);
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

        if (token.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Failure(PasswordResetCompletionStatus.Expired, "The reset link is invalid or expired.");
        }

        token.User.PinHash = AuthService.HashPin(request.NewPin);
        token.UsedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return new PasswordResetCompletionResult
        {
            Succeeded = true,
            Status = PasswordResetCompletionStatus.Succeeded
        };
    }

    private static bool IsValidPin(string pin)
        => pin.Length == 4 && pin.All(char.IsDigit);

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
