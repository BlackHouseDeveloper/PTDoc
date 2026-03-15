using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Service interface for managing SOAP note operations.
/// </summary>
public interface ISOAPNoteService
{
    /// <summary>
    /// Gets all SOAP notes.
    /// </summary>
    /// <returns>A list of all SOAP notes.</returns>
    Task<List<SOAPNote>> GetAllSOAPNotesAsync();

    /// <summary>
    /// Gets all SOAP notes for a specific patient.
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <returns>A list of SOAP notes for the patient.</returns>
    Task<List<SOAPNote>> GetSOAPNotesByPatientIdAsync(Guid patientId);

    /// <summary>
    /// Gets a SOAP note by its unique identifier.
    /// </summary>
    /// <param name="id">The SOAP note's unique identifier.</param>
    /// <returns>The SOAP note if found; otherwise, null.</returns>
    Task<SOAPNote?> GetSOAPNoteByIdAsync(Guid id);

    /// <summary>
    /// Creates a new SOAP note.
    /// Enforces Progress Note hard stop for Daily notes if Medicare thresholds are exceeded.
    /// </summary>
    /// <param name="soapNote">The SOAP note to create.</param>
    /// <returns>The created SOAP note.</returns>
    /// <exception cref="ComplianceException">Thrown when a compliance rule blocks the creation.</exception>
    Task<SOAPNote> CreateSOAPNoteAsync(SOAPNote soapNote);

    /// <summary>
    /// Updates an existing SOAP note.
    /// Blocks edits to signed (locked) notes and writes an audit entry on success.
    /// </summary>
    /// <param name="soapNote">The SOAP note to update.</param>
    /// <returns>The updated SOAP note.</returns>
    /// <exception cref="ComplianceException">Thrown when the note is signed and immutable.</exception>
    Task<SOAPNote> UpdateSOAPNoteAsync(SOAPNote soapNote);

    /// <summary>
    /// Soft-deletes a SOAP note.
    /// </summary>
    /// <param name="id">The SOAP note's unique identifier.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task DeleteSOAPNoteAsync(Guid id);

    /// <summary>
    /// Signs a SOAP note, locking it from further edits and writing an audit entry.
    /// </summary>
    /// <param name="id">The SOAP note's unique identifier.</param>
    /// <param name="userId">Identifier of the user signing the note.</param>
    /// <returns>The signed SOAP note.</returns>
    /// <exception cref="ComplianceException">Thrown when the note is already signed.</exception>
    Task<SOAPNote> SignSOAPNoteAsync(Guid id, string userId);

    /// <summary>
    /// Writes an audit log entry recording that a note was exported.
    /// </summary>
    /// <param name="id">The SOAP note's unique identifier.</param>
    /// <param name="userId">Identifier of the user who initiated the export.</param>
    Task LogNoteExportedAsync(Guid id, string? userId);
}

