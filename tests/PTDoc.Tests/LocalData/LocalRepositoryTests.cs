using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.LocalData;
using Xunit;

namespace PTDoc.Tests.LocalData;

/// <summary>
/// Tests for LocalRepository CRUD operations and sync-state tracking.
/// </summary>
[Trait("Category", "CoreCi")]
public class LocalRepositoryTests
{
    private static LocalDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new LocalDbContext(options);
    }

    // ---------------------------------------------------------------
    // Upsert / Insert
    // ---------------------------------------------------------------

    [Fact]
    public async Task UpsertAsync_InsertsNewEntity_WhenLocalIdIsZero()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var patient = new LocalPatientSummary
        {
            FirstName = "Bob",
            LastName = "Builder",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };

        // Act
        var result = await repo.UpsertAsync(patient);

        // Assert
        Assert.True(result.LocalId > 0);
        Assert.Equal(1, await context.PatientSummaries.CountAsync());
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingEntity_WhenLocalIdIsSet()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var insertTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var patient = new LocalPatientSummary
        {
            FirstName = "Original",
            LastName = "Name",
            SyncState = SyncState.Pending,
            LastModifiedUtc = insertTime
        };
        await repo.UpsertAsync(patient);

        // Act — caller supplies an explicit server timestamp on update
        var serverTimestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        patient.FirstName = "Updated";
        patient.LastModifiedUtc = serverTimestamp;
        await repo.UpsertAsync(patient);

        // Assert
        Assert.Equal(1, await context.PatientSummaries.CountAsync());
        var saved = await context.PatientSummaries.FirstAsync();
        Assert.Equal("Updated", saved.FirstName);
        // Caller-supplied timestamp must be preserved (not overwritten by the repo)
        Assert.Equal(serverTimestamp, saved.LastModifiedUtc);
    }

    [Fact]
    public async Task UpsertAsync_UsesDefaultTimestamp_WhenInsertingWithDefaultLastModified()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var before = DateTime.UtcNow;

        // Act — insert with default (zero) LastModifiedUtc; repo should stamp it
        var patient = new LocalPatientSummary
        {
            FirstName = "New",
            LastName = "Record",
            SyncState = SyncState.Pending
            // LastModifiedUtc intentionally left as default(DateTime)
        };
        await repo.UpsertAsync(patient);

        // Assert — timestamp should have been set by the repository
        var saved = await context.PatientSummaries.FirstAsync();
        Assert.True(saved.LastModifiedUtc >= before, "Repo should stamp LastModifiedUtc on insert when it is default");
    }

    [Fact]
    public async Task UpsertAsync_PreservesCallerTimestamp_WhenInsertingWithExplicitTimestamp()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var serverTime = new DateTime(2023, 3, 10, 8, 30, 0, DateTimeKind.Utc);

        // Act — insert with an explicit server-sourced timestamp (e.g., during initial cache fill)
        var patient = new LocalPatientSummary
        {
            FirstName = "Server",
            LastName = "Sourced",
            SyncState = SyncState.Synced,
            LastModifiedUtc = serverTime
        };
        await repo.UpsertAsync(patient);

        // Assert — caller-supplied timestamp is preserved
        var saved = await context.PatientSummaries.FirstAsync();
        Assert.Equal(serverTime, saved.LastModifiedUtc);
    }

    // ---------------------------------------------------------------
    // GetByLocalId
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetByLocalIdAsync_ReturnsEntity_WhenExists()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var inserted = await repo.UpsertAsync(new LocalPatientSummary
        {
            FirstName = "Alice",
            LastName = "Smith",
            SyncState = SyncState.Synced,
            LastModifiedUtc = DateTime.UtcNow
        });

        // Act
        var found = await repo.GetByLocalIdAsync(inserted.LocalId);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("Alice", found.FirstName);
    }

    [Fact]
    public async Task GetByLocalIdAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);

        // Act
        var result = await repo.GetByLocalIdAsync(9999);

        // Assert
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // GetByServerId
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetByServerIdAsync_ReturnsEntity_WhenExists()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var serverId = Guid.NewGuid();
        await repo.UpsertAsync(new LocalPatientSummary
        {
            ServerId = serverId,
            FirstName = "Charlie",
            LastName = "Brown",
            SyncState = SyncState.Synced,
            LastModifiedUtc = DateTime.UtcNow
        });

        // Act
        var found = await repo.GetByServerIdAsync(serverId);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("Charlie", found.FirstName);
    }

    // ---------------------------------------------------------------
    // GetAll
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsAllRecords()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);

        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "P1", LastName = "L1", SyncState = SyncState.Synced, LastModifiedUtc = DateTime.UtcNow });
        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "P2", LastName = "L2", SyncState = SyncState.Pending, LastModifiedUtc = DateTime.UtcNow });
        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "P3", LastName = "L3", SyncState = SyncState.Conflict, LastModifiedUtc = DateTime.UtcNow });

        // Act
        var all = await repo.GetAllAsync();

        // Assert
        Assert.Equal(3, all.Count);
    }

    // ---------------------------------------------------------------
    // GetPendingSync
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetPendingSyncAsync_ReturnsOnlyPendingAndConflictRecords()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);

        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "Synced", LastName = "X", SyncState = SyncState.Synced, LastModifiedUtc = DateTime.UtcNow });
        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "Pending", LastName = "X", SyncState = SyncState.Pending, LastModifiedUtc = DateTime.UtcNow });
        await repo.UpsertAsync(new LocalPatientSummary { FirstName = "Conflict", LastName = "X", SyncState = SyncState.Conflict, LastModifiedUtc = DateTime.UtcNow });

        // Act
        var pending = await repo.GetPendingSyncAsync();

        // Assert
        Assert.Equal(2, pending.Count);
        Assert.All(pending, p => Assert.NotEqual(SyncState.Synced, p.SyncState));
    }

    // ---------------------------------------------------------------
    // Delete
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_RemovesRecord_WhenExists()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var patient = await repo.UpsertAsync(new LocalPatientSummary
        {
            FirstName = "ToDelete",
            LastName = "Me",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        });

        // Act
        await repo.DeleteAsync(patient.LocalId);

        // Assert
        Assert.Equal(0, await context.PatientSummaries.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_DoesNotThrow_WhenRecordNotFound()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);

        // Act / Assert — should not throw
        await repo.DeleteAsync(9999);
    }

    // ---------------------------------------------------------------
    // MarkSynced
    // ---------------------------------------------------------------

    [Fact]
    public async Task MarkSyncedAsync_SetsSyncStateToSynced_AndUpdatesLastSyncedUtc()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var repo = new LocalRepository<LocalPatientSummary>(context);
        var serverId = Guid.NewGuid();
        await repo.UpsertAsync(new LocalPatientSummary
        {
            ServerId = serverId,
            FirstName = "Sync",
            LastName = "Test",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        });

        // Act
        await repo.MarkSyncedAsync(serverId);

        // Assert
        var entity = await context.PatientSummaries.FirstAsync(p => p.ServerId == serverId);
        Assert.Equal(SyncState.Synced, entity.SyncState);
        Assert.NotNull(entity.LastSyncedUtc);
    }

    // ---------------------------------------------------------------
    // LocalDbInitializer
    // ---------------------------------------------------------------

    [Fact]
    public async Task LocalDbInitializer_InitializeAsync_CompletesWithoutError()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var initializer = new LocalDbInitializer(context, NullLogger<LocalDbInitializer>.Instance);

        // Act / Assert — should not throw
        await initializer.InitializeAsync();
    }
}
