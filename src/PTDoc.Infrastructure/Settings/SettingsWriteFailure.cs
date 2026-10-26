using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

internal static class SettingsWriteFailure
{
    public static void Detach(
        ApplicationDbContext context,
        string eventType,
        Guid entityId,
        IEnumerable<object> entities)
    {
        foreach (var entity in entities.ToArray())
        {
            context.Entry(entity).State = EntityState.Detached;
        }

        foreach (var entry in context.ChangeTracker.Entries<AuditLog>()
                     .Where(entry => entry.State == EntityState.Added
                         && entry.Entity.EventType == eventType
                         && entry.Entity.EntityId == entityId).ToArray())
        {
            entry.State = EntityState.Detached;
        }
    }
}
