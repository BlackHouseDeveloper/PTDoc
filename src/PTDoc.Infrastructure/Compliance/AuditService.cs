using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Text.Json;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Audit service for logging compliance and security events.
/// CRITICAL: No PHI in audit metadata.
/// </summary>
public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _context;

    public AuditService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogRuleEvaluationAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await LogEventAsync(auditEvent, ct);
    }

    public async Task LogRuleOverrideAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await LogEventAsync(auditEvent, ct);
    }

    public async Task LogNoteSignedAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await LogEventAsync(auditEvent, ct);
    }

    public async Task LogAddendumCreatedAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        await LogEventAsync(auditEvent, ct);
    }

    private async Task LogEventAsync(AuditEvent auditEvent, CancellationToken ct)
    {
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            EventType = auditEvent.EventType,
            UserId = auditEvent.UserId,
            CorrelationId = auditEvent.CorrelationId,
            MetadataJson = JsonSerializer.Serialize(auditEvent.Metadata),
            Severity = auditEvent.Severity,
            Success = auditEvent.Success,
            ErrorMessage = auditEvent.ErrorMessage,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(ct);
    }
}
