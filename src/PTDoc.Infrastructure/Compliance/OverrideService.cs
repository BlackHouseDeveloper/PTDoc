using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

public sealed class OverrideService(
    ApplicationDbContext db,
    ILogger<OverrideService> logger) : IOverrideService
{
    // Default set of overridable rule types when ComplianceSettings contains no configuration.
    private static readonly IReadOnlySet<ComplianceRuleType> DefaultAllowedOverrideTypes =
        new HashSet<ComplianceRuleType> { ComplianceRuleType.EightMinuteRule };

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

        // ProgressNoteRequired is an unconditional hard stop; can never be overridden.
        if (request.RuleType == ComplianceRuleType.ProgressNoteRequired)
        {
            throw new InvalidOperationException("This rule cannot be overridden.");
        }

        // Load compliance settings to determine policy (allowed types, min justification length).
        var settings = await db.ComplianceSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        var allowedTypes = ParseAllowedOverrideTypes(settings?.AllowOverrideTypes, settings?.Id.ToString());
        if (!allowedTypes.Contains(request.RuleType))
        {
            throw new InvalidOperationException("This rule cannot currently be overridden.");
        }

        var minLength = settings?.MinJustificationLength ?? ComplianceSettings.DefaultMinJustificationLength;
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Override reason required.", nameof(request.Reason));
        }

        if (reason.Length < minLength)
        {
            throw new ArgumentException(
                $"Override justification must be at least {minLength} characters.",
                nameof(request.Reason));
        }

        var attestationText = settings?.OverrideAttestationText
            ?? ComplianceSettings.DefaultOverrideAttestationText;

        db.RuleOverrides.Add(new RuleOverride
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            RuleName = request.RuleType.ToString(),
            UserId = request.AttestedBy,
            Justification = reason,
            AttestationText = attestationText,
            TimestampUtc = DateTime.UtcNow
        });

        db.AuditLogs.Add(AuditService.CreateAuditLog(
            AuditEvent.OverrideApplied(note.Id, request.RuleType, request.AttestedBy)));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Parses the <see cref="ComplianceSettings.AllowOverrideTypes"/> JSON array.
    /// Returns the default allowed set when the stored value is absent or empty.
    /// </summary>
    private IReadOnlySet<ComplianceRuleType> ParseAllowedOverrideTypes(string? json, string? settingsId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return DefaultAllowedOverrideTypes;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed is null || parsed.Count == 0)
            {
                return DefaultAllowedOverrideTypes;
            }

            var result = new HashSet<ComplianceRuleType>();
            foreach (var item in parsed)
            {
                if (Enum.TryParse<ComplianceRuleType>(item, ignoreCase: true, out var ruleType))
                {
                    result.Add(ruleType);
                }
            }

            return result.Count > 0 ? result : DefaultAllowedOverrideTypes;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                ex,
                "ComplianceSettings.AllowOverrideTypes contains invalid JSON (settingsId={SettingsId}). Falling back to default allowed override types.",
                settingsId ?? "unknown");
            return DefaultAllowedOverrideTypes;
        }
    }
}
