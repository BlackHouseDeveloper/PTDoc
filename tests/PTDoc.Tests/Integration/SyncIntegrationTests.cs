using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests for sync queue persistence with encrypted databases.
/// Validates that encryption does not break sync functionality.
/// </summary>
public class SyncIntegrationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    
    public SyncIntegrationTests()
    {
        // Use encrypted in-memory database
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        
        // Set encryption key
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA key = 'test-encryption-key-for-sync-tests-32-chars';";
            command.ExecuteNonQuery();
        }
        
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        
        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();
    }
    
    [Fact]
    public async Task Encrypted_DB_Does_Not_Break_Queue_Persistence()
    {
        // Arrange: Create sync queue item in encrypted DB
        var entityId = Guid.NewGuid();
        var queueItem = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = entityId,
            Operation = SyncOperation.Update,
            EnqueuedAt = DateTime.UtcNow,
            Status = SyncQueueStatus.Pending
        };
        
        // Act: Persist to encrypted database
        _context.SyncQueueItems.Add(queueItem);
        await _context.SaveChangesAsync();
        
        // Assert: Queue item persisted correctly
        var retrieved = await _context.SyncQueueItems.FirstAsync(q => q.EntityId == entityId);
        
        Assert.Equal("Patient", retrieved.EntityType);
        Assert.Equal(entityId, retrieved.EntityId);
        Assert.Equal(SyncOperation.Update, retrieved.Operation);
    }
    
    [Fact]
    public async Task Encrypted_DB_Supports_Multiple_Queue_Items()
    {
        // Arrange: Create multiple queue items
        var items = new[]
        {
            new SyncQueueItem
            {
                EntityType = "Patient",
                EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Create,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            },
            new SyncQueueItem
            {
                EntityType = "ClinicalNote",
                EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Update,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            },
            new SyncQueueItem
            {
                EntityType = "Appointment",
                EntityId = Guid.NewGuid(),
                Operation = SyncOperation.Delete,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            }
        };
        
        // Act: Add all items to encrypted DB
        _context.SyncQueueItems.AddRange(items);
        await _context.SaveChangesAsync();
        
        // Assert: All items persisted
        Assert.Equal(3, await _context.SyncQueueItems.CountAsync());
        
        var patientItem = await _context.SyncQueueItems.FirstAsync(q => q.EntityType == "Patient");
        Assert.Equal(SyncOperation.Create, patientItem.Operation);
        
        var noteItem = await _context.SyncQueueItems.FirstAsync(q => q.EntityType == "ClinicalNote");
        Assert.Equal(SyncOperation.Update, noteItem.Operation);
        
        var appointmentItem = await _context.SyncQueueItems.FirstAsync(q => q.EntityType == "Appointment");
        Assert.Equal(SyncOperation.Delete, appointmentItem.Operation);
    }
    
    [Fact]
    public async Task Sync_Conflict_Archive_Works_With_Encryption()
    {
        // Arrange: Create conflict archive entry
        var entityId = Guid.NewGuid();
        var conflict = new SyncConflictArchive
        {
            EntityType = "Patient",
            EntityId = entityId,
            ResolutionType = "LastWriteWins",
            Reason = "Client and server versions differed",
            ArchivedDataJson = "{\"firstName\":\"Client\"}",
            ChosenDataJson = "{\"firstName\":\"Server\"}",
            DetectedAt = DateTime.UtcNow,
            IsResolved = true,
            ResolvedAt = DateTime.UtcNow
        };
        
        // Act: Persist to encrypted DB
        _context.SyncConflictArchives.Add(conflict);
        await _context.SaveChangesAsync();
        
        // Assert: Conflict archived correctly
        var retrieved = await _context.SyncConflictArchives.FirstAsync(c => c.EntityId == entityId);
        
        Assert.Equal("Patient", retrieved.EntityType);
        Assert.Equal("LastWriteWins", retrieved.ResolutionType);
        Assert.Contains("Client", retrieved.ArchivedDataJson);
        Assert.Contains("Server", retrieved.ChosenDataJson);
    }
    
    [Fact]
    public async Task Queue_Items_Can_Be_Deleted_From_Encrypted_DB()
    {
        // Arrange: Create and persist queue item
        var queueItem = new SyncQueueItem
        {
            EntityType = "Appointment",
            EntityId = Guid.NewGuid(),
            Operation = SyncOperation.Create,
            EnqueuedAt = DateTime.UtcNow,
            Status = SyncQueueStatus.Pending
        };
        
        _context.SyncQueueItems.Add(queueItem);
        await _context.SaveChangesAsync();
        
        Assert.Equal(1, await _context.SyncQueueItems.CountAsync());
        
        // Act: Delete queue item (simulating successful sync)
        _context.SyncQueueItems.Remove(queueItem);
        await _context.SaveChangesAsync();
        
        // Assert: Item removed
        Assert.Equal(0, await _context.SyncQueueItems.CountAsync());
    }
    
    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
