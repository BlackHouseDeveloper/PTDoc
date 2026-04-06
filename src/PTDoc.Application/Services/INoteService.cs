using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

/// <summary>
/// Contract for retrieving clinical notes. Write operations remain on the API layer.
/// UI services implement this to call the API; server implementations may query the DB directly.
/// </summary>
public interface INoteService
{
    /// <summary>
    /// Returns a lightweight list of notes, optionally filtered by patient, note type, sign status,
    /// or taxonomy category/item (first-class SQL filter via NoteTaxonomySelections join table).
    /// </summary>
    Task<IReadOnlyList<NoteListItemApiResponse>> GetNotesAsync(
        Guid? patientId = null,
        string? noteType = null,
        string? status = null,
        int take = 100,
        string? categoryId = null,
        string? itemId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a full note record when the UI needs authoritative backend status details.
    /// </summary>
    Task<NoteDetailResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a bounded set of note details for batch-oriented UI flows.
    /// </summary>
    Task<IReadOnlyList<NoteDetailResponse>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the current export preview target using authoritative backend filters.
    /// </summary>
    Task<ExportPreviewTargetResponse> ResolveExportPreviewTargetAsync(
        ExportPreviewTargetRequest request,
        CancellationToken cancellationToken = default);
}
