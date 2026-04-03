using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Text.Json;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Medicare rules engine implementation.
/// Enforces deterministic compliance rules with no reliance on clinician memory.
/// </summary>
public class RulesEngine : IRulesEngine
{
    private readonly ApplicationDbContext _context;
    private readonly IAuditService _auditService;

    // Rule constants based on Medicare specification
    private const int ProgressNoteWarningVisitThreshold = 8;
    private const int ProgressNoteWarningDayThreshold = 25;
    private const int ProgressNoteVisitThreshold = 10;
    private const int ProgressNoteDayThreshold = 30;

    public RulesEngine(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }

    /// <summary>
    /// Determines whether a Progress Note is due for a Medicare patient on the supplied service date.
    /// </summary>
    public Task<ValidationResult> CheckProgressNoteDueAsync(Guid patientId, DateTime referenceDate, CancellationToken ct = default)
        => CheckProgressNoteDueCoreAsync(patientId, referenceDate, payerTypeOverride: null, ct);

    /// <summary>
    /// Validates timed CPT units against Medicare 8-minute rule requirements.
    /// </summary>
    public async Task<ValidationResult> ValidateTimedUnitsAsync(List<CptCodeEntry> entries, CancellationToken ct = default)
    {
        entries ??= [];

        var normalizedEntries = entries
            .Select(entry => new CptCodeEntry
            {
                Code = entry.Code?.Trim() ?? string.Empty,
                Units = entry.Units,
                Minutes = entry.Minutes,
                IsTimed = entry.IsTimed
            })
            .ToList();

        TimedUnitCalculator.EnforceKnownTimedCptStatus(normalizedEntries);

        var result = ValidationResult.Valid();

        foreach (var entry in normalizedEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Code))
            {
                result.Errors.Add("Missing CPT data");
            }

            if (entry.Units < 0)
            {
                result.Errors.Add($"CPT code {entry.Code} has an invalid unit count.");
            }

            if (entry.Minutes < 0)
            {
                result.Errors.Add($"CPT code {entry.Code} must record zero or more minutes.");
            }
        }

        var timedEntries = normalizedEntries
            .Where(TimedUnitCalculator.IsRelevantTimedEntry)
            .ToList();

        if (timedEntries.Count == 0)
        {
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("8MIN_RULE", result.Errors.Count == 0), ct);
            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        foreach (var entry in timedEntries)
        {
            if (entry.Units > 0 && entry.Minutes is null)
            {
                result.Errors.Add($"Timed CPT code {entry.Code} is missing minutes.");
                continue;
            }

            if (entry.Units > 0 && entry.Minutes <= 0)
            {
                result.Errors.Add($"Timed CPT code {entry.Code} must record minutes greater than zero.");
            }
        }

        if (result.Errors.Count > 0)
        {
            result.IsValid = false;
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("8MIN_RULE", false), ct);
            return result;
        }

        var totalMinutes = timedEntries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        if (totalMinutes < 5)
        {
            result.Errors.Add("Minimum 5 minutes required");
            result.IsValid = false;
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("8MIN_RULE", false), ct);
            return result;
        }

        if (totalMinutes <= 7)
        {
            result.Warnings.Add("Minutes fall below standard 8-minute threshold");
            result.RequiresOverride = true;
        }

        var allowedUnits = TimedUnitCalculator.CalculateAllowedUnits(totalMinutes);
        var requestedUnits = timedEntries.Sum(TimedUnitCalculator.ResolveRequestedUnits);

        if (requestedUnits > allowedUnits)
        {
            result.Warnings.Add("Timed CPT units exceed allowed range for documented minutes");
            result.RequiresOverride = true;
        }

        result.IsValid = result.Errors.Count == 0;
        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("8MIN_RULE", result.Errors.Count == 0 && result.Warnings.Count == 0), ct);
        return result;
    }

    /// <summary>
    /// Validates Progress Note frequency: Medicare = ≥10 visits OR ≥30 days; Commercial = ≥30 days only.
    /// </summary>
    public async Task<RuleResult> ValidateProgressNoteFrequencyAsync(Guid patientId, string? payerType = null, CancellationToken ct = default)
    {
        var validation = await CheckProgressNoteDueCoreAsync(patientId, DateTime.UtcNow.Date, payerType, ct);
        if (validation.Errors.Count > 0)
        {
            return RuleResult.HardStop("PN_FREQUENCY", validation.Errors[0]);
        }

        if (validation.Warnings.Count > 0)
        {
            return RuleResult.Warning("PN_FREQUENCY", validation.Warnings[0]);
        }

        return RuleResult.Success("PN_FREQUENCY", "Progress Note not required");
    }

    /// <summary>
    /// Validates CPT units against 8-minute rule.
    /// 8-22 min = 1 unit, 23-37 = 2, 38-52 = 3, 53-67 = 4, etc.
    /// </summary>
    public async Task<RuleResult> ValidateEightMinuteRuleAsync(int totalMinutes, List<CptCodeEntry> cptCodes, CancellationToken ct = default)
    {
        if (totalMinutes < 0)
        {
            return RuleResult.Error("8MIN_RULE", "Total minutes cannot be negative");
        }

        TimedUnitCalculator.EnforceKnownTimedCptStatus(cptCodes);

        int allowedUnits = TimedUnitCalculator.CalculateAllowedUnits(totalMinutes);

        int requestedTimedUnits = cptCodes
            .Where(TimedUnitCalculator.IsTimed)
            .Sum(entry => entry.Units > 0
                ? entry.Units
                : TimedUnitCalculator.CalculateAllowedUnits(entry.Minutes.GetValueOrDefault()));

        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("8MIN_RULE", requestedTimedUnits <= allowedUnits), ct);

        if (requestedTimedUnits > allowedUnits)
        {
            return RuleResult.Warning(
                "8MIN_RULE",
                $"Units exceed 8-minute rule allowance. PT override required.",
                new Dictionary<string, object>
                {
                    ["TotalMinutes"] = totalMinutes,
                    ["AllowedUnits"] = allowedUnits,
                    ["RequestedUnits"] = requestedTimedUnits,
                    ["ExcessUnits"] = requestedTimedUnits - allowedUnits
                });
        }

        return RuleResult.Success("8MIN_RULE",
            $"Units valid: {requestedTimedUnits} of {allowedUnits} allowed for {totalMinutes} minutes");
    }

    /// <summary>
    /// Calculates allowed units based on total minutes using 8-minute rule.
    /// </summary>
    private static int CalculateAllowedUnits(int totalMinutes)
    {
        return TimedUnitCalculator.CalculateAllowedUnits(totalMinutes);
    }

    /// <summary>
    /// Validates that a note is eligible for signing.
    /// </summary>
    public async Task<RuleResult> ValidateSignatureEligibilityAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null)
        {
            return RuleResult.Error("SIGN_ELIGIBLE", "Note not found");
        }

        if (!string.IsNullOrEmpty(note.SignatureHash))
        {
            return RuleResult.Error("SIGN_ELIGIBLE", "Note is already signed");
        }

        // Could add content validation here (e.g., required fields populated)

        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("SIGN_ELIGIBLE", true), ct);

        return RuleResult.Success("SIGN_ELIGIBLE", "Note is eligible for signing");
    }

    /// <summary>
    /// Validates that a note is immutable (cannot be edited if signed).
    /// </summary>
    public async Task<RuleResult> ValidateImmutabilityAsync(Guid noteId, CancellationToken ct = default)
    {
        var note = await _context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note == null)
        {
            return RuleResult.Error("IMMUTABLE", "Note not found");
        }

        if (string.IsNullOrEmpty(note.SignatureHash))
        {
            // Not signed, can be edited
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("IMMUTABLE", true), ct);
            return RuleResult.Success("IMMUTABLE", "Note is not signed, edits allowed");
        }

        // Signed note - immutable
        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("IMMUTABLE", false), ct);

        return RuleResult.HardStop(
            "IMMUTABLE",
            "Note is signed and cannot be edited. Create an addendum instead.",
            new Dictionary<string, object>
            {
                ["SignedUtc"] = note.SignedUtc ?? DateTime.MinValue,
                ["SignedByUserId"] = note.SignedByUserId ?? Guid.Empty
            });
    }

    private async Task<ValidationResult> CheckProgressNoteDueCoreAsync(
        Guid patientId,
        DateTime referenceDate,
        string? payerTypeOverride,
        CancellationToken ct)
    {
        var payerType = payerTypeOverride;
        if (string.IsNullOrWhiteSpace(payerType))
        {
            payerType = await ResolvePayerTypeAsync(patientId, ct);
        }

        if (!string.Equals(payerType, "Medicare", StringComparison.OrdinalIgnoreCase))
        {
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("PN_FREQUENCY", true), ct);
            return ValidationResult.Valid();
        }

        var serviceDate = referenceDate.Date;
        var nextDay = serviceDate.AddDays(1);
        var patientNotes = _context.ClinicalNotes
            .AsNoTracking()
            .Where(note => note.PatientId == patientId && note.DateOfService < nextDay);

        var lastSignedPnOrEval = await patientNotes
            .Where(note =>
                note.SignatureHash != null &&
                (note.NoteType == NoteType.Evaluation || note.NoteType == NoteType.ProgressNote))
            .OrderByDescending(note => note.DateOfService)
            .FirstOrDefaultAsync(ct);

        int visitsSincePn;
        int daysSincePn;

        if (lastSignedPnOrEval is null)
        {
            var firstNoteDate = await patientNotes
                .OrderBy(note => note.DateOfService)
                .Select(note => (DateTime?)note.DateOfService)
                .FirstOrDefaultAsync(ct);

            if (!firstNoteDate.HasValue)
            {
                await _auditService.LogRuleEvaluationAsync(
                    AuditEvent.RuleEvaluation("PN_FREQUENCY", true), ct);
                return ValidationResult.Valid();
            }

            visitsSincePn = await patientNotes
                .Where(note => note.NoteType == NoteType.Daily)
                .CountAsync(ct);

            daysSincePn = (serviceDate - firstNoteDate.Value.Date).Days;
        }
        else
        {
            visitsSincePn = await patientNotes
                .Where(note => note.NoteType == NoteType.Daily && note.DateOfService > lastSignedPnOrEval.DateOfService)
                .CountAsync(ct);

            daysSincePn = (serviceDate - lastSignedPnOrEval.DateOfService.Date).Days;
        }

        ValidationResult result;
        if (visitsSincePn >= ProgressNoteVisitThreshold || daysSincePn >= ProgressNoteDayThreshold)
        {
            result = ValidationResult.Error("Progress Note required");
        }
        else if (visitsSincePn >= ProgressNoteWarningVisitThreshold || daysSincePn >= ProgressNoteWarningDayThreshold)
        {
            result = ValidationResult.Warning("Progress Note due soon");
        }
        else
        {
            result = ValidationResult.Valid();
        }

        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("PN_FREQUENCY", result.Errors.Count == 0 && result.Warnings.Count == 0), ct);
        return result;
    }

    private async Task<string?> ResolvePayerTypeAsync(Guid patientId, CancellationToken ct)
    {
        var payerInfoJson = await _context.Patients
            .AsNoTracking()
            .Where(patient => patient.Id == patientId)
            .Select(patient => patient.PayerInfoJson)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(payerInfoJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payerInfoJson);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "PayerType", StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}

internal static class TimedUnitCalculator
{
    public static void EnforceKnownTimedCptStatus(List<CptCodeEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (KnownTimedCptCodes.Codes.Contains(entry.Code))
            {
                entry.IsTimed = true;
            }
        }
    }

    public static bool IsTimed(CptCodeEntry entry)
        => entry.IsTimed || KnownTimedCptCodes.Codes.Contains(entry.Code);

    public static bool IsRelevantTimedEntry(CptCodeEntry entry)
        => IsTimed(entry) && (entry.Units > 0 || entry.Minutes.HasValue);

    public static int ResolveRequestedUnits(CptCodeEntry entry)
    {
        if (entry.Units > 0)
        {
            return entry.Units;
        }

        var minutes = entry.Minutes.GetValueOrDefault();
        return minutes > 0
            ? CalculateAllowedUnits(minutes)
            : 0;
    }

    public static int CalculateAllowedUnits(int totalMinutes)
    {
        if (totalMinutes < 8)
        {
            return 0;
        }

        return ((totalMinutes - 8) / 15) + 1;
    }
}
