using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Communication;

[Trait("Category", "CoreCi")]
public sealed class CommunicationRetentionCleanupServiceTests
{
    [Fact]
    public async Task CleanupAsync_WithSqlite_RemovesExpiredEligibleRecords()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));

        await using var provider = services.BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            await SeedRetentionRecordsAsync(db);
        }

        var cleanup = new CommunicationRetentionCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new CommunicationOptions
            {
                Retention = new CommunicationRetentionOptions
                {
                    ResetTokensDays = 30,
                    DeliveryLogsDays = 30
                }
            }),
            NullLogger<CommunicationRetentionCleanupService>.Instance);

        await InvokeCleanupAsync(cleanup);

        await using var assertionScope = provider.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var remainingTokenHashes = await assertionDb.PasswordResetTokens
            .OrderBy(token => token.TokenHash)
            .Select(token => token.TokenHash)
            .ToListAsync();
        Assert.Equal(["expired-recent-revoked", "expired-recent-used", "unexpired-active"], remainingTokenHashes);

        var remainingLogHashes = await assertionDb.CommunicationDeliveryLogs
            .OrderBy(log => log.RecipientHash)
            .Select(log => log.RecipientHash)
            .ToListAsync();
        Assert.Equal(["recent-log"], remainingLogHashes);

        var remainingChallengeHashes = await assertionDb.IntakeOtpChallenges
            .OrderBy(challenge => challenge.ContactHash)
            .Select(challenge => challenge.ContactHash)
            .ToListAsync();
        Assert.Equal(["unexpired-challenge"], remainingChallengeHashes);
    }

    private static async Task SeedRetentionRecordsAsync(ApplicationDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "cleanup-user",
            PinHash = "hash",
            FirstName = "Cleanup",
            LastName = "User",
            Role = "PT",
            IsActive = true,
            CreatedAt = now.UtcDateTime
        };

        db.Users.Add(user);
        db.PasswordResetTokens.AddRange(
            CreateResetToken(user.Id, "expired-active", now.AddDays(-40)),
            CreateResetToken(user.Id, "unexpired-active", now.AddDays(1)),
            CreateResetToken(user.Id, "expired-recent-used", now.AddDays(-40), usedAtUtc: now.AddDays(-5)),
            CreateResetToken(user.Id, "expired-recent-revoked", now.AddDays(-40), revokedAtUtc: now.AddDays(-5)));

        db.CommunicationDeliveryLogs.AddRange(
            CreateDeliveryLog("old-log", now.AddDays(-40)),
            CreateDeliveryLog("recent-log", now.AddDays(-5)));

        db.IntakeOtpChallenges.AddRange(
            CreateOtpChallenge("expired-challenge", now.AddMinutes(-5)),
            CreateOtpChallenge("unexpired-challenge", now.AddMinutes(30)));

        await db.SaveChangesAsync();
    }

    private static PasswordResetToken CreateResetToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? usedAtUtc = null,
        DateTimeOffset? revokedAtUtc = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            Channel = DeliveryChannel.Email,
            RecipientHash = $"{tokenHash}-recipient",
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = expiresAtUtc.AddDays(-1),
            UsedAtUtc = usedAtUtc,
            RevokedAtUtc = revokedAtUtc
        };

    private static CommunicationDeliveryLog CreateDeliveryLog(string recipientHash, DateTimeOffset createdAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            Purpose = DeliveryPurpose.PasswordReset,
            Channel = DeliveryChannel.Email,
            RecipientHash = recipientHash,
            Provider = "Test",
            Status = DeliveryStatus.Sent,
            SentAtUtc = createdAtUtc,
            CreatedAtUtc = createdAtUtc,
            CreatedAtUnixSeconds = createdAtUtc.ToUnixTimeSeconds()
        };

    private static IntakeOtpChallenge CreateOtpChallenge(string contactHash, DateTimeOffset expiresAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            IntakeId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            Channel = DeliveryChannel.Sms,
            ContactHash = contactHash,
            OtpHash = $"{contactHash}-otp",
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = expiresAtUtc.AddMinutes(-10),
            UpdatedAtUtc = expiresAtUtc.AddMinutes(-5),
            WindowStartUtc = expiresAtUtc.AddMinutes(-10),
            SendCount = 1
        };

    private static async Task InvokeCleanupAsync(CommunicationRetentionCleanupService cleanup)
    {
        var method = typeof(CommunicationRetentionCleanupService).GetMethod(
            "CleanupAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(cleanup, [CancellationToken.None]));
        await task;
    }
}
