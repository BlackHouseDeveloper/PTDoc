using Microsoft.EntityFrameworkCore;
using PTDoc.Application.LocalData;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.LocalData;

namespace PTDoc.Tests.LocalData;

[Trait("Category", "OfflineSync")]
public sealed class LocalClinicScopeTests
{
    [Fact]
    public async Task ClinicChange_IsolatesDraftsAndWatermarks_AndPreservesBothPartitions()
    {
        var scope = new LocalDataScope();
        await scope.ChangeAsync("user:clinic-a");
        await using var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var repository = new LocalRepository<LocalClinicalNoteDraft>(db);
        var draft = await repository.UpsertAsync(new LocalClinicalNoteDraft { NoteType = "Daily", ContentJson = "{\"clinic\":\"a\"}", SyncState = SyncState.Pending });
        db.SyncMetadata.Add(new LocalSyncMetadata { EntityType = "ClinicalNote", SyncToken = "clinic-a-cursor" });
        await db.SaveChangesAsync();
        await scope.ChangeAsync("user:clinic-b");
        Assert.Empty(await repository.GetAllAsync());
        Assert.Empty(await db.SyncMetadata.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpsertAsync(draft));
        await repository.UpsertAsync(new LocalClinicalNoteDraft { NoteType = "Daily", ContentJson = "{\"clinic\":\"b\"}" });
        await scope.ChangeAsync("user:clinic-a");
        Assert.Equal(draft.LocalId, Assert.Single(await repository.GetAllAsync()).LocalId);
        Assert.Equal("clinic-a-cursor", (await db.SyncMetadata.SingleAsync()).SyncToken);
        Assert.Equal(2, await db.ClinicalNoteDrafts.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ScopeReplacement_WaitsForInFlightSyncLease()
    {
        var scope = new LocalDataScope();
        await scope.ChangeAsync("a");
        using var lease = await scope.EnterAsync();
        var tokensReplaced = false;
        var change = scope.ChangeAsync("b", () => { tokensReplaced = true; return Task.CompletedTask; });
        Assert.False(change.IsCompleted);
        Assert.False(tokensReplaced);
        Assert.Equal("a", scope.Key);
        lease.Dispose();
        await change;
        Assert.Equal("b", scope.Key);
        Assert.True(tokensReplaced);
    }

    [Fact]
    public async Task LegacyRows_AreRetainedButNeverAssignedToNewlySelectedClinic()
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using (var legacy = new LocalDbContext(options))
        {
            legacy.ClinicalNoteDrafts.Add(new LocalClinicalNoteDraft { ContentJson = "legacy unsynced work", SyncState = SyncState.Pending });
            await legacy.SaveChangesAsync();
        }
        var scope = new LocalDataScope();
        await scope.ChangeAsync("user:clinic");
        await using var db = new LocalDbContext(options, scope);
        Assert.Empty(await db.ClinicalNoteDrafts.ToListAsync());
        Assert.Equal("legacy unsynced work", (await db.ClinicalNoteDrafts.IgnoreQueryFilters().SingleAsync()).ContentJson);
        await scope.ChangeAsync(null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var lease = await scope.EnterAsync(); });
    }
}
