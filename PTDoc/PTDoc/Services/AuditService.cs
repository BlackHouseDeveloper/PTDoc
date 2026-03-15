using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Persists audit log entries for compliance-relevant events.
/// </summary>
public class AuditService : IAuditService
{
    private readonly PTDocDbContext _context;
    private readonly ILogger<AuditService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditService"/> class.
    /// </summary>
    public AuditService(PTDocDbContext context, ILogger<AuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task LogNoteEditedAsync(Guid clinicId, Guid noteId, string? userId)
        => AppendAsync(clinicId, "NoteEdited", nameof(SOAPNote), noteId, userId);

    /// <inheritdoc/>
    public Task LogNoteSignedAsync(Guid clinicId, Guid noteId, string? userId)
        => AppendAsync(clinicId, "NoteSigned", nameof(SOAPNote), noteId, userId);

    /// <inheritdoc/>
    public Task LogNoteExportedAsync(Guid clinicId, Guid noteId, string? userId)
        => AppendAsync(clinicId, "NoteExported", nameof(SOAPNote), noteId, userId);

    private async Task AppendAsync(Guid clinicId, string eventType, string entityType, Guid entityId, string? userId)
    {
        var auditLog = new AuditLog
        {
            ClinicId = clinicId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            TimestampUtc = DateTime.UtcNow
        };

        _context.AuditLogs.Add(auditLog);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Detach the failed entry so it doesn't poison subsequent SaveChanges calls
            // on the same request-scoped DbContext.
            _context.Entry(auditLog).State = EntityState.Detached;

            // Audit failures must not disrupt primary operations; log and continue.
            _logger.LogError(ex, "Failed to write audit log entry for event {EventType} on {EntityType}/{EntityId}",
                eventType, entityType, entityId);
        }
    }
}
