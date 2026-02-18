using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Data.Interceptors;

/// <summary>
/// EF Core interceptor that automatically stamps LastModifiedUtc and ModifiedByUserId on ISyncTrackedEntity entities.
/// This ensures consistent modification tracking for offline-first synchronization.
/// </summary>
public class SyncMetadataInterceptor : SaveChangesInterceptor
{
    private readonly IIdentityContextAccessor _identityContext;

    public SyncMetadataInterceptor(IIdentityContextAccessor identityContext)
    {
        _identityContext = identityContext;
    }

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

    private void UpdateSyncMetadata(DbContext? context)
    {
        if (context == null) return;

        var entries = context.ChangeTracker.Entries<ISyncTrackedEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        var now = DateTime.UtcNow;
        var currentUserId = _identityContext.GetCurrentUserId();

        foreach (var entry in entries)
        {
            // Always update LastModifiedUtc and ModifiedByUserId
            entry.Entity.LastModifiedUtc = now;
            entry.Entity.ModifiedByUserId = currentUserId;

            // For new entities, ensure sync state is Pending
            if (entry.State == EntityState.Added)
            {
                // Set to Pending unless already Pending or Conflict
                // (SyncState defaults to Pending=0, but enforce it in case it was manually set to Synced)
                if (entry.Entity.SyncState == SyncState.Synced)
                {
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
        }
    }
}
