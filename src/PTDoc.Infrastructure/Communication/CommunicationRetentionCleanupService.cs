using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Communication;

public sealed class CommunicationRetentionCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CommunicationOptions _options;
    private readonly ILogger<CommunicationRetentionCleanupService> _logger;

    public CommunicationRetentionCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<CommunicationOptions> options,
        ILogger<CommunicationRetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Communication retention cleanup failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTimeOffset.UtcNow;
        var resetCutoff = now.AddDays(-Math.Max(1, _options.Retention.ResetTokensDays));
        var logCutoff = now.AddDays(-Math.Max(1, _options.Retention.DeliveryLogsDays)).ToUnixTimeSeconds();
        var tokenCount = 0;
        var logCount = 0;
        var challengeCount = 0;

        if (db.Database.IsRelational())
        {
            tokenCount = await RemoveExpiredResetTokensAsync(db, resetCutoff, cancellationToken);
            challengeCount = await RemoveExpiredOtpChallengesAsync(db, now, cancellationToken);
            if (tokenCount > 0 || challengeCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            logCount = await db.CommunicationDeliveryLogs
                .Where(log => log.CreatedAtUnixSeconds < logCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            LogCleanupCounts(tokenCount, logCount, challengeCount);
            return;
        }

        tokenCount = await RemoveExpiredResetTokensAsync(db, resetCutoff, cancellationToken);

        var expiredLogs = await db.CommunicationDeliveryLogs
            .Where(log => log.CreatedAtUnixSeconds < logCutoff)
            .ToListAsync(cancellationToken);
        db.CommunicationDeliveryLogs.RemoveRange(expiredLogs);

        challengeCount = await RemoveExpiredOtpChallengesAsync(db, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        LogCleanupCounts(tokenCount, expiredLogs.Count, challengeCount);
    }

    private static async Task<int> RemoveExpiredResetTokensAsync(
        ApplicationDbContext db,
        DateTimeOffset resetCutoff,
        CancellationToken cancellationToken)
    {
        var expiredCandidates = await db.PasswordResetTokens.ToListAsync(cancellationToken);

        var expiredTokens = expiredCandidates
            .Where(token =>
                token.ExpiresAtUtc < resetCutoff &&
                (token.UsedAtUtc is null || token.UsedAtUtc < resetCutoff) &&
                (token.RevokedAtUtc is null || token.RevokedAtUtc < resetCutoff))
            .ToList();

        db.PasswordResetTokens.RemoveRange(expiredTokens);
        return expiredTokens.Count;
    }

    private static async Task<int> RemoveExpiredOtpChallengesAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var challengeCandidates = await db.IntakeOtpChallenges.ToListAsync(cancellationToken);
        var expiredChallenges = challengeCandidates
            .Where(challenge => challenge.ExpiresAtUtc < now)
            .ToList();

        db.IntakeOtpChallenges.RemoveRange(expiredChallenges);
        return expiredChallenges.Count;
    }

    private void LogCleanupCounts(int tokenCount, int logCount, int challengeCount)
    {
        if (tokenCount == 0 && logCount == 0 && challengeCount == 0)
        {
            _logger.LogDebug("Communication retention cleanup completed with no expired records.");
            return;
        }

        _logger.LogInformation(
            "Communication retention cleanup deleted {ResetTokenCount} reset token(s), {DeliveryLogCount} delivery log(s), and {OtpChallengeCount} OTP challenge(s).",
            tokenCount,
            logCount,
            challengeCount);
    }
}
