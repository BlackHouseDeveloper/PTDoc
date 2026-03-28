using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Implementation of <see cref="ICarryForwardService"/>.
///
/// Fetches the most recently signed note that is eligible as a carry-forward source
/// for the given target note type. The returned data is read-only; this service
/// never modifies any note.
///
/// Eligibility rules:
///   Daily      → last signed Evaluation, ProgressNote, or Daily
///   ProgressNote → last signed Evaluation, ProgressNote, or Daily
///   Discharge  → last signed Evaluation or ProgressNote
///   Evaluation → no note-based source (returns null)
/// </summary>
public class CarryForwardService : ICarryForwardService
{
    private readonly ApplicationDbContext _context;

    public CarryForwardService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async Task<CarryForwardData?> GetCarryForwardDataAsync(
        Guid patientId,
        NoteType targetNoteType,
        CancellationToken ct = default)
    {
        // Determine which source note types are eligible for the target note type.
        // Evaluation notes pull from intake forms, not prior notes, so return null.
        var eligibleSourceTypes = GetEligibleSourceTypes(targetNoteType);
        if (eligibleSourceTypes.Length == 0)
        {
            return null;
        }

        // Fetch the most recent SIGNED note among eligible types.
        // A note is considered signed when SignatureHash is non-null.
        // Ordering: most recent DateOfService, then most recently modified as tie-breaker.
        var sourceNote = await _context.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.PatientId == patientId
                        && n.SignatureHash != null          // signed only
                        && eligibleSourceTypes.Contains(n.NoteType))
            .OrderByDescending(n => n.DateOfService)
            .ThenByDescending(n => n.LastModifiedUtc)
            .FirstOrDefaultAsync(ct);

        if (sourceNote is null)
        {
            return null;
        }

        return new CarryForwardData
        {
            SourceNoteId = sourceNote.Id,
            SourceNoteType = sourceNote.NoteType,
            SourceNoteDateOfService = sourceNote.DateOfService,
            ContentJson = sourceNote.ContentJson
        };
    }

    /// <summary>
    /// Returns the note types eligible as a carry-forward source for the target note type.
    /// </summary>
    private static NoteType[] GetEligibleSourceTypes(NoteType targetNoteType) =>
        targetNoteType switch
        {
            NoteType.Daily =>
                [NoteType.Evaluation, NoteType.ProgressNote, NoteType.Daily],
            NoteType.ProgressNote =>
                [NoteType.Evaluation, NoteType.ProgressNote, NoteType.Daily],
            NoteType.Discharge =>
                [NoteType.Evaluation, NoteType.ProgressNote],
            // Evaluation: no note-based carry-forward (pulls from intake form instead)
            _ => []
        };
}
