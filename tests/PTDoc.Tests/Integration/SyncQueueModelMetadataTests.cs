using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SyncQueueModelMetadataTests
{
    [Fact]
    public void SyncQueueItem_HasStatusEnqueuedAtCompositeIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:", sqlite => sqlite.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;
        using var context = new ApplicationDbContext(options);

        var entityType = context.Model.FindEntityType(typeof(SyncQueueItem));
        Assert.NotNull(entityType);

        var hasIndex = entityType!.GetIndexes().Any(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(SyncQueueItem.Status), nameof(SyncQueueItem.EnqueuedAt) }));

        Assert.True(hasIndex);
    }
}
