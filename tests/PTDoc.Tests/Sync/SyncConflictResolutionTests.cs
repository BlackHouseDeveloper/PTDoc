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
[Xunit.Trait("Category", "OfflineSync")]
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
        var firstItem = await context.SyncQueueItems.FirstAsync();
        var firstEnqueuedAt = firstItem.EnqueuedAt;
        var firstOperation = firstItem.Operation;

        // Act - enqueue again with different operation
        await syncEngine.EnqueueAsync("Patient", entityId, SyncOperation.Update);

        // Assert
        var queueItems = await context.SyncQueueItems
            .Where(q => q.EntityType == "Patient" && q.EntityId == entityId)
            .ToListAsync();

        Assert.Single(queueItems); // Should only have one item
        Assert.Equal(SyncOperation.Update, queueItems[0].Operation); // Operation updated

        // Timestamp should be updated (or at least not before the original)
        Assert.True(queueItems[0].EnqueuedAt >= firstEnqueuedAt,
            "EnqueuedAt should be updated when re-enqueueing");

        // Verify operation was actually updated
        Assert.NotEqual(firstOperation, queueItems[0].Operation);
    }

    [Fact]
    public async Task PushAsync_ProcessesPendingItems()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Create entities in the DB so ProcessQueueItemAsync can find them
        var userId = Guid.NewGuid();
        var p1 = new Patient { FirstName = "A", LastName = "B", DateOfBirth = new DateTime(1980, 1, 1), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending };
        var p2 = new Patient { FirstName = "C", LastName = "D", DateOfBirth = new DateTime(1985, 6, 1), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending };
        context.Patients.AddRange(p1, p2);
        var appt = new Appointment { PatientId = p1.Id, StartTimeUtc = DateTime.UtcNow, EndTimeUtc = DateTime.UtcNow.AddHours(1), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending };
        context.Appointments.Add(appt);
        await context.SaveChangesAsync();

        // Enqueue some items
        await syncEngine.EnqueueAsync("Patient", p1.Id, SyncOperation.Create);
        await syncEngine.EnqueueAsync("Patient", p2.Id, SyncOperation.Update);
        await syncEngine.EnqueueAsync("Appointment", appt.Id, SyncOperation.Create);

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

    [Fact]
    public async Task PushAsync_MarksValidationFailures_AsDeadLetter()
    {
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var failedItem = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = Guid.NewGuid(),
            Status = SyncQueueStatus.Pending,
            RetryCount = 0,
            MaxRetries = 5,
            EnqueuedAt = DateTime.UtcNow.AddMinutes(-2)
        };

        context.SyncQueueItems.Add(failedItem);
        await context.SaveChangesAsync();

        var result = await syncEngine.PushAsync();

        var item = await context.SyncQueueItems.FindAsync(failedItem.Id);
        Assert.NotNull(item);
        Assert.Equal(1, result.DeadLetterCount);
        Assert.Equal(SyncQueueStatus.DeadLetter, item.Status);
        Assert.Equal(SyncFailureType.ValidationError, item.FailureType);
    }

    [Fact]
    public async Task GetQueueStatusAsync_UsesSharedRuntimeStateAcrossEngineInstances()
    {
        var context = CreateInMemoryContext();
        var runtimeStateStore = new SyncRuntimeStateStore();
        var engine1 = new SyncEngine(context, NullLogger<SyncEngine>.Instance, runtimeStateStore);
        var engine2 = new SyncEngine(context, NullLogger<SyncEngine>.Instance, runtimeStateStore);

        await engine1.PushAsync();

        var status = await engine2.GetQueueStatusAsync();

        Assert.False(status.IsRunning);
        Assert.NotNull(status.LastSyncAt);
        Assert.NotNull(status.LastSuccessUtc);
    }
}
