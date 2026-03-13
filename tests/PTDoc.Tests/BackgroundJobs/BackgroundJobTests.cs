using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Identity;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.BackgroundJobs;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using Xunit;

namespace PTDoc.Tests.BackgroundJobs;

/// <summary>
/// Unit tests for Sprint I background job infrastructure.
/// Covers sync retry eligibility, state transitions, and session cleanup delegation.
/// </summary>
public class BackgroundJobTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IServiceScope BuildScope(ApplicationDbContext context, ISyncEngine? syncEngine = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<ISyncEngine>(syncEngine ?? new SyncEngine(context, NullLogger<SyncEngine>.Instance));

        var provider = services.BuildServiceProvider();
        return provider.CreateScope();
    }

    private static IServiceScopeFactory BuildScopeFactory(ApplicationDbContext context, ISyncEngine? syncEngine = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<ISyncEngine>(syncEngine ?? new SyncEngine(context, NullLogger<SyncEngine>.Instance));

        // Required so IServiceScopeFactory is available
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    // ── SyncRetry: no eligible items ─────────────────────────────────────────

    [Fact]
    public async Task SyncRetryJob_DoesNothing_WhenNoFailedItems()
    {
        var context = CreateInMemoryContext();
        var scopeFactory = BuildScopeFactory(context);

        var options = Options.Create(new SyncRetryOptions { Interval = TimeSpan.FromSeconds(1), MinRetryDelay = TimeSpan.Zero });
        var svc = new SyncRetryBackgroundService(scopeFactory, NullLogger<SyncRetryBackgroundService>.Instance, options);

        await svc.ExecuteJobAsync(CancellationToken.None);

        // Nothing in the queue — no items should have changed
        Assert.Empty(await context.SyncQueueItems.ToListAsync());
    }

    // ── SyncRetry: respects MaxRetries ────────────────────────────────────────

    [Fact]
    public async Task SyncRetryJob_SkipsItems_AtMaxRetries()
    {
        var context = CreateInMemoryContext();

        var exhausted = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = Guid.NewGuid(),
            Status = SyncQueueStatus.Failed,
            RetryCount = 3,
            MaxRetries = 3,
            EnqueuedAt = DateTime.UtcNow,
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-10)
        };
        context.SyncQueueItems.Add(exhausted);
        await context.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory(context);
        var options = Options.Create(new SyncRetryOptions { MinRetryDelay = TimeSpan.Zero });
        var svc = new SyncRetryBackgroundService(scopeFactory, NullLogger<SyncRetryBackgroundService>.Instance, options);

        await svc.ExecuteJobAsync(CancellationToken.None);

        // Item should remain Failed and not be reset to Pending
        var item = await context.SyncQueueItems.FindAsync(exhausted.Id);
        Assert.NotNull(item);
        Assert.Equal(SyncQueueStatus.Failed, item.Status);
    }

    // ── SyncRetry: MinRetryDelay enforced ────────────────────────────────────

    [Fact]
    public async Task SyncRetryJob_SkipsItems_TooRecentlyFailed()
    {
        var context = CreateInMemoryContext();

        var recent = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = Guid.NewGuid(),
            Status = SyncQueueStatus.Failed,
            RetryCount = 1,
            MaxRetries = 3,
            EnqueuedAt = DateTime.UtcNow,
            LastAttemptAt = DateTime.UtcNow // failed just now — within MinRetryDelay
        };
        context.SyncQueueItems.Add(recent);
        await context.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory(context);
        // MinRetryDelay = 60 s means the item above should not be retried
        var options = Options.Create(new SyncRetryOptions { MinRetryDelay = TimeSpan.FromSeconds(60) });
        var svc = new SyncRetryBackgroundService(scopeFactory, NullLogger<SyncRetryBackgroundService>.Instance, options);

        await svc.ExecuteJobAsync(CancellationToken.None);

        var item = await context.SyncQueueItems.FindAsync(recent.Id);
        Assert.NotNull(item);
        Assert.Equal(SyncQueueStatus.Failed, item.Status); // unchanged
    }

    // ── SyncRetry: eligible item gets retried ────────────────────────────────

    [Fact]
    public async Task SyncRetryJob_ResetsEligibleFailedItem_ToPending_ThenProcesses()
    {
        var context = CreateInMemoryContext();

        var eligible = new SyncQueueItem
        {
            EntityType = "Appointment",
            EntityId = Guid.NewGuid(),
            Status = SyncQueueStatus.Failed,
            RetryCount = 1,
            MaxRetries = 3,
            EnqueuedAt = DateTime.UtcNow.AddMinutes(-5),
            LastAttemptAt = DateTime.UtcNow.AddMinutes(-5)
        };
        context.SyncQueueItems.Add(eligible);
        await context.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory(context);
        var options = Options.Create(new SyncRetryOptions { MinRetryDelay = TimeSpan.Zero });
        var svc = new SyncRetryBackgroundService(scopeFactory, NullLogger<SyncRetryBackgroundService>.Instance, options);

        await svc.ExecuteJobAsync(CancellationToken.None);

        // After execution, the item should have been processed (Completed or re-Failed with bumped RetryCount)
        var item = await context.SyncQueueItems.FindAsync(eligible.Id);
        Assert.NotNull(item);
        Assert.True(
            item.Status == SyncQueueStatus.Completed || item.RetryCount > eligible.RetryCount,
            $"Expected item to be Completed or have incremented RetryCount, but got Status={item.Status}, RetryCount={item.RetryCount}");
    }

    // ── SessionCleanup: delegates to IAuthService ────────────────────────────

    [Fact]
    public async Task SessionCleanupJob_CallsCleanupExpiredSessionsAsync()
    {
        var mockAuth = new Mock<IAuthService>();
        mockAuth
            .Setup(a => a.CleanupExpiredSessionsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(mockAuth.Object);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = Options.Create(new SessionCleanupOptions { Interval = TimeSpan.FromMinutes(5) });
        var svc = new SessionCleanupBackgroundService(
            scopeFactory,
            NullLogger<SessionCleanupBackgroundService>.Instance,
            options);

        await svc.ExecuteJobAsync(CancellationToken.None);

        mockAuth.Verify(a => a.CleanupExpiredSessionsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── SyncRetryOptions: defaults ───────────────────────────────────────────

    [Fact]
    public void SyncRetryOptions_HasExpectedDefaults()
    {
        var opts = new SyncRetryOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), opts.Interval);
        Assert.Equal(TimeSpan.FromSeconds(60), opts.MinRetryDelay);
    }

    // ── SessionCleanupOptions: defaults ──────────────────────────────────────

    [Fact]
    public void SessionCleanupOptions_HasExpectedDefaults()
    {
        var opts = new SessionCleanupOptions();
        Assert.Equal(TimeSpan.FromMinutes(5), opts.Interval);
    }
}
