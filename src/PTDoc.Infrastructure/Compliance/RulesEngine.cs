using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

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
    private const int ProgressNoteVisitThreshold = 10;
    private const int ProgressNoteDayThreshold = 30;
    
    public RulesEngine(ApplicationDbContext context, IAuditService auditService)
    {
        _context = context;
        _auditService = auditService;
    }
    
    /// <summary>
    /// Validates Progress Note frequency: ≥10 visits OR ≥30 days since last PN/Eval.
    /// </summary>
    public async Task<RuleResult> ValidateProgressNoteFrequencyAsync(Guid patientId, CancellationToken ct = default)
    {
        // Get all notes for patient ordered by date
        var notes = await _context.ClinicalNotes
            .Where(n => n.PatientId == patientId)
            .OrderByDescending(n => n.DateOfService)
            .ToListAsync(ct);
        
        if (!notes.Any())
        {
            // No notes yet, no PN required
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("PN_FREQUENCY", true), ct);
            return RuleResult.Success("PN_FREQUENCY", "No notes yet, Progress Note not required");
        }
        
        // Find most recent Evaluation or Progress Note
        var lastPnOrEval = notes
            .Where(n => n.NoteType == NoteType.Evaluation || n.NoteType == NoteType.ProgressNote)
            .FirstOrDefault();
        
        if (lastPnOrEval == null)
        {
            // Only daily notes exist, check if PN is needed
            var daysSinceFirstNote = (DateTime.UtcNow.Date - notes.Last().DateOfService.Date).Days;
            var dailyNoteCount = notes.Count(n => n.NoteType == NoteType.Daily);
            
            if (dailyNoteCount >= ProgressNoteVisitThreshold || daysSinceFirstNote >= ProgressNoteDayThreshold)
            {
                await _auditService.LogRuleEvaluationAsync(
                    AuditEvent.RuleEvaluation("PN_FREQUENCY", false), ct);
                
                return RuleResult.HardStop(
                    "PN_FREQUENCY",
                    "Progress Note required per Medicare guidelines.",
                    new Dictionary<string, object>
                    {
                        ["VisitCount"] = dailyNoteCount,
                        ["DaysSinceStart"] = daysSinceFirstNote,
                        ["Threshold"] = $"{ProgressNoteVisitThreshold} visits OR {ProgressNoteDayThreshold} days"
                    });
            }
            
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("PN_FREQUENCY", true), ct);
            return RuleResult.Success("PN_FREQUENCY", "Progress Note not yet required");
        }
        
        // Count visits since last PN/Eval
        var visitsSinceLastPn = notes
            .Where(n => n.DateOfService > lastPnOrEval.DateOfService)
            .Count();
        
        var daysSinceLastPn = (DateTime.UtcNow.Date - lastPnOrEval.DateOfService.Date).Days;
        
        if (visitsSinceLastPn >= ProgressNoteVisitThreshold || daysSinceLastPn >= ProgressNoteDayThreshold)
        {
            await _auditService.LogRuleEvaluationAsync(
                AuditEvent.RuleEvaluation("PN_FREQUENCY", false), ct);
            
            return RuleResult.HardStop(
                "PN_FREQUENCY",
                "Progress Note required per Medicare guidelines.",
                new Dictionary<string, object>
                {
                    ["VisitsSinceLastPN"] = visitsSinceLastPn,
                    ["DaysSinceLastPN"] = daysSinceLastPn,
                    ["LastPNDate"] = lastPnOrEval.DateOfService,
                    ["Threshold"] = $"{ProgressNoteVisitThreshold} visits OR {ProgressNoteDayThreshold} days"
                });
        }
        
        await _auditService.LogRuleEvaluationAsync(
            AuditEvent.RuleEvaluation("PN_FREQUENCY", true), ct);
        
        return RuleResult.Success("PN_FREQUENCY", 
            $"Progress Note not required ({visitsSinceLastPn} visits, {daysSinceLastPn} days since last PN)");
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
        
        // Calculate allowed units based on 8-minute rule
        int allowedUnits = CalculateAllowedUnits(totalMinutes);
        
        // Sum up requested timed units
        int requestedTimedUnits = cptCodes
            .Where(c => c.IsTimed)
            .Sum(c => c.Units);
        
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
        // 8-minute rule table:
        // 8-22 min = 1 unit
        // 23-37 min = 2 units
        // 38-52 min = 3 units
        // 53-67 min = 4 units
        // Pattern: first unit at 8min, additional units every 15min
        
        if (totalMinutes < 8) return 0;
        if (totalMinutes <= 22) return 1;
        
        // After first unit (8-22 min), each additional 15 minutes = 1 unit
        int minutesAbove22 = totalMinutes - 22;
        int additionalUnits = (minutesAbove22 + 14) / 15; // Round up using integer division
        
        return 1 + additionalUnits;
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
}
