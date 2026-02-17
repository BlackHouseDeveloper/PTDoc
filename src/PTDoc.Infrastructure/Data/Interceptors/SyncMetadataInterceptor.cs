using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Data.Interceptors;

/// <summary>
/// EF Core interceptor that automatically stamps LastModifiedUtc on ISyncTrackedEntity entities.
/// This ensures consistent modification tracking for offline-first synchronization.
/// </summary>
public class SyncMetadataInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateSyncMetadata(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateSyncMetadata(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateSyncMetadata(DbContext? context)
    {
        if (context == null) return;

        var entries = context.ChangeTracker.Entries<ISyncTrackedEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        var now = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            // Always update LastModifiedUtc
            entry.Entity.LastModifiedUtc = now;

            // For new entities, set default sync state if not already set
            if (entry.State == EntityState.Added)
            {
                // Only set to Pending if it's still default (0)
                if (entry.Entity.SyncState == SyncState.Synced && entry.Entity.Id == Guid.Empty)
                {
                    // New entity, not yet synced
                    entry.Entity.SyncState = SyncState.Pending;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                // Modified entity - mark as pending if it was previously synced
                if (entry.Entity.SyncState == SyncState.Synced)
                {
                    entry.Entity.SyncState = SyncState.Pending;
                }
            }

            // Note: ModifiedByUserId should be set by the application layer via IIdentityContextAccessor
            // This interceptor doesn't have access to the current user context
        }
    }
}
