using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData;

/// <summary>
/// Generic repository contract for local MAUI SQLite persistence.
/// Provides CRUD operations and sync-aware queries for any <see cref="ILocalEntity"/>.
/// </summary>
/// <typeparam name="TEntity">A local entity that implements <see cref="ILocalEntity"/>.</typeparam>
public interface ILocalRepository<TEntity> where TEntity : class, ILocalEntity
{
    /// <summary>Retrieves a record by its local SQLite primary key.</summary>
    Task<TEntity?> GetByLocalIdAsync(int localId, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a record by its server-side UUID.</summary>
    Task<TEntity?> GetByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Returns all records for this entity type.</summary>
    Task<IReadOnlyList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns records that have not yet been synced with the server
    /// (i.e. <see cref="SyncState.Pending"/> or <see cref="SyncState.Conflict"/>).
    /// </summary>
    Task<IReadOnlyList<TEntity>> GetPendingSyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the entity.
    /// If <see cref="ILocalEntity.LocalId"/> is zero the record is inserted; otherwise it is updated.
    /// </summary>
    Task<TEntity> UpsertAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Deletes a record by its local primary key.</summary>
    Task DeleteAsync(int localId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a record as synced with the server.
    /// Sets <see cref="SyncState.Synced"/> and updates <see cref="ILocalEntity.LastSyncedUtc"/>.
    /// </summary>
    Task MarkSyncedAsync(Guid serverId, CancellationToken cancellationToken = default);
}
