using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Models;
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
            if (IsSqlite(db))
            {
                // EF Core SQLite cannot translate DateTimeOffset retention predicates for these columns.
                tokenCount = await RemoveExpiredResetTokensForSqliteAsync(db, resetCutoff, cancellationToken);
                challengeCount = await RemoveExpiredOtpChallengesForSqliteAsync(db, now, cancellationToken);
                if (tokenCount > 0 || challengeCount > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                tokenCount = await db.PasswordResetTokens
                    .Where(token =>
                        token.ExpiresAtUtc < resetCutoff &&
                        (token.UsedAtUtc == null || token.UsedAtUtc < resetCutoff) &&
                        (token.RevokedAtUtc == null || token.RevokedAtUtc < resetCutoff))
                    .ExecuteDeleteAsync(cancellationToken);

                challengeCount = await db.IntakeOtpChallenges
                    .Where(challenge => challenge.ExpiresAtUtc < now)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            logCount = await db.CommunicationDeliveryLogs
                .Where(log => log.CreatedAtUnixSeconds < logCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            LogCleanupCounts(tokenCount, logCount, challengeCount);
            return;
        }

        tokenCount = await RemoveExpiredResetTokensClientSideAsync(db, resetCutoff, cancellationToken);

        var expiredLogs = await db.CommunicationDeliveryLogs
            .Where(log => log.CreatedAtUnixSeconds < logCutoff)
            .ToListAsync(cancellationToken);
        db.CommunicationDeliveryLogs.RemoveRange(expiredLogs);

        challengeCount = await RemoveExpiredOtpChallengesClientSideAsync(db, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        LogCleanupCounts(tokenCount, expiredLogs.Count, challengeCount);
    }

    private static bool IsSqlite(ApplicationDbContext db) =>
        string.Equals(
            db.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.Sqlite",
            StringComparison.OrdinalIgnoreCase);

    private static Task<int> RemoveExpiredResetTokensForSqliteAsync(
        ApplicationDbContext db,
        DateTimeOffset resetCutoff,
        CancellationToken cancellationToken) =>
        RemoveExpiredResetTokensClientSideAsync(db, resetCutoff, cancellationToken);

    private static async Task<int> RemoveExpiredResetTokensClientSideAsync(
        ApplicationDbContext db,
        DateTimeOffset resetCutoff,
        CancellationToken cancellationToken)
    {
        var expiredTokens = new List<PasswordResetToken>();
        await foreach (var token in db.PasswordResetTokens.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (token.ExpiresAtUtc < resetCutoff &&
                (token.UsedAtUtc is null || token.UsedAtUtc < resetCutoff) &&
                (token.RevokedAtUtc is null || token.RevokedAtUtc < resetCutoff))
            {
                expiredTokens.Add(token);
            }
        }

        db.PasswordResetTokens.RemoveRange(expiredTokens);
        return expiredTokens.Count;
    }

    private static Task<int> RemoveExpiredOtpChallengesForSqliteAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        RemoveExpiredOtpChallengesClientSideAsync(db, now, cancellationToken);

    private static async Task<int> RemoveExpiredOtpChallengesClientSideAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var expiredChallenges = new List<IntakeOtpChallenge>();
        await foreach (var challenge in db.IntakeOtpChallenges.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (challenge.ExpiresAtUtc < now)
            {
                expiredChallenges.Add(challenge);
            }
        }

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
