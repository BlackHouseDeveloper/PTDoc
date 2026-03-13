using Microsoft.EntityFrameworkCore;
using PTDoc.Application.LocalData;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.LocalData;

/// <summary>
/// Generic EF Core repository backed by <see cref="LocalDbContext"/>.
/// Provides sync-aware CRUD operations for any <see cref="ILocalEntity"/>.
/// </summary>
public class LocalRepository<TEntity> : ILocalRepository<TEntity>
    where TEntity : class, ILocalEntity
{
    private readonly LocalDbContext _context;

    public LocalRepository(LocalDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<TEntity?> GetByLocalIdAsync(int localId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.LocalId == localId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TEntity?> GetByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.ServerId == serverId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<TEntity>()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TEntity>> GetPendingSyncAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<TEntity>()
            .AsNoTracking()
            .Where(e => e.SyncState == SyncState.Pending || e.SyncState == SyncState.Conflict)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TEntity> UpsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.LocalId == 0)
        {
            // Insert
            entity.LastModifiedUtc = DateTime.UtcNow;
            _context.Set<TEntity>().Add(entity);
        }
        else
        {
            // Update
            entity.LastModifiedUtc = DateTime.UtcNow;
            _context.Set<TEntity>().Update(entity);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(int localId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.LocalId == localId, cancellationToken);

        if (entity is not null)
        {
            _context.Set<TEntity>().Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task MarkSyncedAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<TEntity>()
            .FirstOrDefaultAsync(e => e.ServerId == serverId, cancellationToken);

        if (entity is not null)
        {
            entity.SyncState = SyncState.Synced;
            entity.LastSyncedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
