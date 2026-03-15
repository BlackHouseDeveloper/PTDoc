using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Data;
using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Medicare compliance rules implementation.
/// Enforces Progress Note frequency, 8-minute billing rule, and signature immutability.
/// </summary>
public class ComplianceService : IComplianceService
{
    // Medicare thresholds
    private const int ProgressNoteVisitThreshold = 10;
    private const int ProgressNoteDayThreshold = 30;

    private readonly PTDocDbContext _context;
    private readonly ILogger<ComplianceService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComplianceService"/> class.
    /// </summary>
    public ComplianceService(PTDocDbContext context, ILogger<ComplianceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ComplianceResult> CheckProgressNoteRequiredAsync(Guid patientId, Guid clinicId)
    {
        // Retrieve all non-deleted notes for the patient within the clinic, ordered most-recent first.
        var notes = await _context.SOAPNotes
            .Where(n => n.PatientId == patientId && n.ClinicId == clinicId && !n.IsDeleted)
            .OrderByDescending(n => n.VisitDate)
            .ToListAsync();

        if (notes.Count == 0)
        {
            return ComplianceResult.Pass("PN_FREQUENCY", "No prior notes – initial note permitted.");
        }

        // Find the most-recent Progress Note or Evaluation.
        var lastPnOrEval = notes.FirstOrDefault(n =>
            n.NoteType == NoteType.ProgressNote || n.NoteType == NoteType.Evaluation);

        if (lastPnOrEval == null)
        {
            // Only daily (or discharge) notes on record. Check whether a PN is now overdue.
            var daysSinceFirst = (DateTime.UtcNow.Date - notes.Last().VisitDate.Date).Days;
            var dailyCount = notes.Count(n => n.NoteType == NoteType.Daily);

            if (dailyCount >= ProgressNoteVisitThreshold || daysSinceFirst >= ProgressNoteDayThreshold)
            {
                _logger.LogInformation(
                    "PN hard stop for patient {PatientId}: {Visits} visits, {Days} days.",
                    patientId, dailyCount, daysSinceFirst);

                return ComplianceResult.HardStop(
                    "PN_REQUIRED",
                    $"A Progress Note is required per Medicare guidelines " +
                    $"({dailyCount} visits, {daysSinceFirst} days since episode start). " +
                    $"Create a Progress Note before adding a Daily note.");
            }

            return ComplianceResult.Pass("PN_FREQUENCY", "Progress Note not yet required.");
        }

        // Count daily visits since the last PN/Eval.
        var visitsSincePn = notes
            .Where(n => n.VisitDate > lastPnOrEval.VisitDate && n.NoteType == NoteType.Daily)
            .Count();
        var daysSincePn = (DateTime.UtcNow.Date - lastPnOrEval.VisitDate.Date).Days;

        if (visitsSincePn >= ProgressNoteVisitThreshold || daysSincePn >= ProgressNoteDayThreshold)
        {
            _logger.LogInformation(
                "PN hard stop for patient {PatientId}: {Visits} visits since last PN, {Days} days.",
                patientId, visitsSincePn, daysSincePn);

            return ComplianceResult.HardStop(
                "PN_REQUIRED",
                $"A Progress Note is required per Medicare guidelines " +
                $"({visitsSincePn} visits, {daysSincePn} days since last Progress Note). " +
                $"Create a Progress Note before adding a Daily note.");
        }

        return ComplianceResult.Pass(
            "PN_FREQUENCY",
            $"Progress Note not required ({visitsSincePn} visits, {daysSincePn} days since last PN).");
    }

    /// <inheritdoc/>
    public ComplianceResult ValidateEightMinuteRule(int durationMinutes, int billedUnits)
    {
        if (durationMinutes < 0)
        {
            return ComplianceResult.HardStop("8MIN_RULE", "Duration cannot be negative.");
        }

        int allowedUnits = CalculateAllowedUnits(durationMinutes);

        if (billedUnits > allowedUnits)
        {
            return ComplianceResult.HardStop(
                "8MIN_RULE",
                $"Billed units ({billedUnits}) exceed the allowed units ({allowedUnits}) " +
                $"for {durationMinutes} minutes per the Medicare 8-minute rule.");
        }

        return ComplianceResult.Pass(
            "8MIN_RULE",
            $"{durationMinutes} minutes → {allowedUnits} allowed unit(s); {billedUnits} billed.");
    }

    /// <inheritdoc/>
    public ComplianceResult EnforceSignatureLock(SOAPNote note)
    {
        if (note.IsCompleted)
        {
            return ComplianceResult.HardStop(
                "SIGN_LOCK",
                "This note is signed and locked. Signed notes cannot be edited. Create an addendum instead.");
        }

        return ComplianceResult.Pass("SIGN_LOCK", "Note is not signed – editing permitted.");
    }

    /// <inheritdoc/>
    public ComplianceResult SignNote(SOAPNote note, string userId)
    {
        if (note.IsCompleted)
        {
            return ComplianceResult.HardStop(
                "SIGN_LOCK",
                "Note is already signed. Signature immutability prevents re-signing.");
        }

        note.IsCompleted = true;
        note.SignedAt = DateTime.UtcNow;
        note.SignedBy = userId;

        return ComplianceResult.Pass("SIGNED", "Note signed successfully.");
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Calculates the maximum billable units for a given treatment duration per the
    /// Medicare 8-minute rule (CMS §5.2): 8-22 min → 1 unit, +1 unit per 15 min thereafter.
    /// </summary>
    private static int CalculateAllowedUnits(int totalMinutes)
    {
        if (totalMinutes < 8) return 0;
        if (totalMinutes <= 22) return 1;

        int minutesAbove22 = totalMinutes - 22;
        return 1 + (int)Math.Ceiling(minutesAbove22 / 15.0);
    }
}
