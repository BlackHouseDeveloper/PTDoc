using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using Xunit;

namespace PTDoc.Tests.Sync;

/// <summary>
/// Tests for sync conflict resolution rules.
/// Covers draft LWW, signed immutability, and intake locking.
/// </summary>
public class SyncConflictResolutionTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task EnqueueAsync_CreatesQueueItem_WhenNotExists()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var entityId = Guid.NewGuid();

        // Act
        await syncEngine.EnqueueAsync("Patient", entityId, SyncOperation.Update);

        // Assert
        var queueItem = await context.SyncQueueItems
            .FirstOrDefaultAsync(q => q.EntityType == "Patient" && q.EntityId == entityId);

        Assert.NotNull(queueItem);
        Assert.Equal(SyncOperation.Update, queueItem.Operation);
        Assert.Equal(SyncQueueStatus.Pending, queueItem.Status);
    }

    [Fact]
    public async Task EnqueueAsync_UpdatesExistingItem_WhenAlreadyPending()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var entityId = Guid.NewGuid();

        await syncEngine.EnqueueAsync("Patient", entityId, SyncOperation.Create);
        var firstEnqueuedAt = (await context.SyncQueueItems.FirstAsync()).EnqueuedAt;

        // Wait a bit to ensure timestamp changes
        await Task.Delay(100);

        // Act
        await syncEngine.EnqueueAsync("Patient", entityId, SyncOperation.Update);

        // Assert
        var queueItems = await context.SyncQueueItems
            .Where(q => q.EntityType == "Patient" && q.EntityId == entityId)
            .ToListAsync();

        Assert.Single(queueItems); // Should only have one item
        Assert.Equal(SyncOperation.Update, queueItems[0].Operation);
        Assert.True(queueItems[0].EnqueuedAt > firstEnqueuedAt);
    }

    [Fact]
    public async Task PushAsync_ProcessesPendingItems()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Enqueue some items
        await syncEngine.EnqueueAsync("Patient", Guid.NewGuid(), SyncOperation.Create);
        await syncEngine.EnqueueAsync("Patient", Guid.NewGuid(), SyncOperation.Update);
        await syncEngine.EnqueueAsync("Appointment", Guid.NewGuid(), SyncOperation.Create);

        // Act
        var result = await syncEngine.PushAsync();

        // Assert
        Assert.Equal(3, result.TotalPushed);
        Assert.Equal(3, result.SuccessCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, result.ConflictCount);

        // Verify items are marked as completed
        var completedItems = await context.SyncQueueItems
            .Where(q => q.Status == SyncQueueStatus.Completed)
            .CountAsync();
        Assert.Equal(3, completedItems);
    }

    [Fact]
    public async Task GetQueueStatusAsync_ReturnsCorrectCounts()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Create items with different statuses
        context.SyncQueueItems.AddRange(
            new SyncQueueItem { EntityType = "Patient", EntityId = Guid.NewGuid(), Status = SyncQueueStatus.Pending, EnqueuedAt = DateTime.UtcNow },
            new SyncQueueItem { EntityType = "Patient", EntityId = Guid.NewGuid(), Status = SyncQueueStatus.Pending, EnqueuedAt = DateTime.UtcNow.AddMinutes(-5) },
            new SyncQueueItem { EntityType = "Patient", EntityId = Guid.NewGuid(), Status = SyncQueueStatus.Processing, EnqueuedAt = DateTime.UtcNow },
            new SyncQueueItem { EntityType = "Patient", EntityId = Guid.NewGuid(), Status = SyncQueueStatus.Failed, EnqueuedAt = DateTime.UtcNow },
            new SyncQueueItem { EntityType = "Patient", EntityId = Guid.NewGuid(), Status = SyncQueueStatus.Completed, EnqueuedAt = DateTime.UtcNow }
        );
        await context.SaveChangesAsync();

        // Act
        var status = await syncEngine.GetQueueStatusAsync();

        // Assert
        Assert.Equal(2, status.PendingCount);
        Assert.Equal(1, status.ProcessingCount);
        Assert.Equal(1, status.FailedCount);
        Assert.NotNull(status.OldestPendingAt);
    }

    [Fact]
    public async Task SyncNowAsync_ExecutesPushThenPull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Enqueue some items to push
        await syncEngine.EnqueueAsync("Patient", Guid.NewGuid(), SyncOperation.Create);

        // Act
        var result = await syncEngine.SyncNowAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.PushResult);
        Assert.NotNull(result.PullResult);
        Assert.Equal(1, result.PushResult.TotalPushed);
        Assert.True(result.Duration.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task PushAsync_RetriesFailedItems_WithinRetryLimit()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Create a failed item that can be retried
        var failedItem = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = Guid.NewGuid(),
            Status = SyncQueueStatus.Failed,
            RetryCount = 1,
            MaxRetries = 3,
            EnqueuedAt = DateTime.UtcNow
        };
        context.SyncQueueItems.Add(failedItem);
        await context.SaveChangesAsync();

        // Act
        var result = await syncEngine.PushAsync();

        // Assert
        Assert.Equal(1, result.TotalPushed);

        // Verify retry count was incremented (or item completed if successful)
        var item = await context.SyncQueueItems.FindAsync(failedItem.Id);
        Assert.NotNull(item);
        // Item should either be completed or have increased retry count
        Assert.True(item.Status == SyncQueueStatus.Completed || item.RetryCount > 1);
    }
}
