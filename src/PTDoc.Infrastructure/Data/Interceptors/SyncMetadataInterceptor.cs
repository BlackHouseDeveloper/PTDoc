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
    private readonly TimeProvider _timeProvider;

    public SyncMetadataInterceptor(IIdentityContextAccessor identityContext, TimeProvider? timeProvider = null)
    {
        _identityContext = identityContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var currentUserId = _identityContext.GetCurrentUserId();

        foreach (var entry in entries)
        {
            // A sync-only acknowledgement preserves the version loaded by an open chart or encounter wizard.
            if (entry.State == EntityState.Modified
                && entry.Entity is (Patient { SyncState: SyncState.Synced } or IntakeForm { EncounterNoteId: not null })
                && entry.Properties.Where(property => property.IsModified)
                    .All(property => property.Metadata.Name == nameof(ISyncTrackedEntity.SyncState)))
            {
                continue;
            }

            // Always update LastModifiedUtc and ModifiedByUserId
            // PostgreSQL timestamps store microseconds. Return the same version that
            // a fresh request will read, without weakening exact concurrency checks.
            entry.Entity.LastModifiedUtc = entry.Entity is (Patient or PatientInsurancePolicy or PatientInsuranceAuthorization or IntakeForm { EncounterNoteId: not null })
                && context.Database.IsNpgsql()
                    ? new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc)
                    : now;
            if (entry.State == EntityState.Modified
                && entry.Entity is (Patient or PatientInsurancePolicy or PatientInsuranceAuthorization or IntakeForm { EncounterNoteId: null, IsLocked: true, SubmittedAt: not null }))
            {
                // The chart and standalone outcome projection both reject stale versions.
                // Keep it strictly increasing even with a fixed/backward clock, at PostgreSQL precision.
                var original = entry.Property(entity => entity.LastModifiedUtc).OriginalValue;
                var minimumTicks = original.Ticks - original.Ticks % TimeSpan.TicksPerMicrosecond + TimeSpan.TicksPerMicrosecond;
                var clockTicks = now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond;
                entry.Entity.LastModifiedUtc = new DateTime(Math.Max(clockTicks, minimumTicks), DateTimeKind.Utc);
            }
            entry.Entity.ModifiedByUserId = currentUserId;

            if (entry.Entity.SyncState == SyncState.Synced)
            {
                entry.Entity.SyncState = SyncState.Pending;
            }
            if (entry.State == EntityState.Modified && entry.Entity is Patient { SyncState: SyncState.Pending })
            {
                // A worker may have acknowledged the previous version since this context loaded it.
                entry.Property(entity => entity.SyncState).IsModified = true;
            }
        }
    }
}
