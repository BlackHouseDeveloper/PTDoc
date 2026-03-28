using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

/// <summary>
/// Contract for retrieving clinical notes. Write operations remain on the API layer.
/// UI services implement this to call the API; server implementations may query the DB directly.
/// </summary>
public interface INoteService
{
    /// <summary>
    /// Returns a lightweight list of notes, optionally filtered by patient, note type, or sign status.
    /// </summary>
    Task<IReadOnlyList<NoteListItemApiResponse>> GetNotesAsync(
        Guid? patientId = null,
        string? noteType = null,
        string? status = null,
        int take = 100,
        CancellationToken cancellationToken = default);
}
