using PTDoc.Core.Models;

namespace PTDoc.Application.Compliance;

/// <summary>
/// Service for retrieving carry-forward data from the most recent signed clinical note.
/// Used to pre-populate new draft notes with relevant context from the prior visit.
///
/// DETERMINISTIC: Returns read-only data — never modifies existing notes.
/// SAFETY: Only returns data from SIGNED notes; unsigned/draft notes are ignored to
/// prevent unsigned content from propagating into new notes.
/// </summary>
public interface ICarryForwardService
{
    /// <summary>
    /// Retrieves carry-forward data for the specified patient from their most recently
    /// signed note that is eligible as a source for the given target note type.
    /// </summary>
    /// <param name="patientId">The patient whose prior note data should be fetched.</param>
    /// <param name="targetNoteType">
    /// The type of note being authored; determines which source note types are eligible.
    /// Evaluation notes have no note-based carry-forward source (returns null).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Carry-forward data from the most recent eligible signed note,
    /// or null if no eligible signed source note exists.
    /// </returns>
    Task<CarryForwardData?> GetCarryForwardDataAsync(
        Guid patientId,
        NoteType targetNoteType,
        CancellationToken ct = default);
}

/// <summary>
/// Read-only data carry-forwarded from a prior signed clinical note.
/// Provided as a suggestion to pre-populate a new note during authoring.
/// Callers MUST NOT write this data directly to a signed note.
/// </summary>
public sealed class CarryForwardData
{
    /// <summary>Identity of the source note this data was pulled from.</summary>
    public Guid SourceNoteId { get; init; }

    /// <summary>Type of the source note (e.g. Daily, Evaluation).</summary>
    public NoteType SourceNoteType { get; init; }

    /// <summary>Date of service of the source note.</summary>
    public DateTime SourceNoteDateOfService { get; init; }

    /// <summary>
    /// Canonical informational copy of the source note content, returned for carry-forward only.
    /// Recognized legacy note payloads may be normalized into workspace v2, but the signed
    /// source note itself is never mutated.
    /// </summary>
    public string ContentJson { get; init; } = "{}";
}
