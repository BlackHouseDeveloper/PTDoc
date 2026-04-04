using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

public sealed class OverrideService(ApplicationDbContext db) : IOverrideService
{
    public async Task ApplyOverrideAsync(OverrideRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var note = db.ClinicalNotes.Local.FirstOrDefault(entity => entity.Id == request.NoteId)
            ?? await db.ClinicalNotes.FirstOrDefaultAsync(entity => entity.Id == request.NoteId, ct)
            ?? throw new KeyNotFoundException($"Note {request.NoteId} not found.");

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == request.AttestedBy, ct);

        if (user is null)
        {
            throw new KeyNotFoundException($"Attesting user {request.AttestedBy} not found.");
        }

        if (!string.Equals(user.Role, Roles.PT, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only PT can override compliance warnings.");
        }

        if (request.RuleType == ComplianceRuleType.ProgressNoteRequired)
        {
            throw new InvalidOperationException("This rule cannot be overridden.");
        }

        if (request.RuleType != ComplianceRuleType.EightMinuteRule)
        {
            throw new InvalidOperationException("This rule cannot currently be overridden.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Override reason required.", nameof(request.Reason));
        }

        var attestationText = await db.ComplianceSettings
            .AsNoTracking()
            .Select(settings => settings.OverrideAttestationText)
            .FirstOrDefaultAsync(ct)
            ?? ComplianceSettings.DefaultOverrideAttestationText;

        db.RuleOverrides.Add(new RuleOverride
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            RuleName = request.RuleType.ToString(),
            UserId = request.AttestedBy,
            Justification = reason,
            AttestationText = attestationText,
            TimestampUtc = request.Timestamp == default ? DateTime.UtcNow : request.Timestamp
        });

        db.AuditLogs.Add(AuditService.CreateAuditLog(
            AuditEvent.OverrideApplied(note.Id, request.RuleType, reason, request.AttestedBy)));

        await db.SaveChangesAsync(ct);
    }
}
