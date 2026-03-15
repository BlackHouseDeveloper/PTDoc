namespace PTDoc.Services;

/// <summary>
/// Logs compliance-relevant events to the audit trail.
/// Audit entries must not contain PHI.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs a note-edited event.
    /// </summary>
    /// <param name="clinicId">Clinic (tenant) identifier.</param>
    /// <param name="noteId">Identifier of the note that was edited.</param>
    /// <param name="userId">Identifier of the user who performed the edit.</param>
    Task LogNoteEditedAsync(Guid clinicId, Guid noteId, string? userId);

    /// <summary>
    /// Logs a note-signed event.
    /// </summary>
    /// <param name="clinicId">Clinic (tenant) identifier.</param>
    /// <param name="noteId">Identifier of the note that was signed.</param>
    /// <param name="userId">Identifier of the user who signed the note.</param>
    Task LogNoteSignedAsync(Guid clinicId, Guid noteId, string? userId);

    /// <summary>
    /// Logs a note-exported event.
    /// </summary>
    /// <param name="clinicId">Clinic (tenant) identifier.</param>
    /// <param name="noteId">Identifier of the note that was exported.</param>
    /// <param name="userId">Identifier of the user who initiated the export.</param>
    Task LogNoteExportedAsync(Guid clinicId, Guid noteId, string? userId);
}
