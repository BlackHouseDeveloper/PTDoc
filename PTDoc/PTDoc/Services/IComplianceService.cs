using PTDoc.Models;

namespace PTDoc.Services;

/// <summary>
/// Enforces Medicare documentation compliance rules within the note lifecycle.
/// </summary>
public interface IComplianceService
{
    /// <summary>
    /// Checks whether a Progress Note is required before a new Daily note can be saved.
    /// Returns a hard stop if ≥10 visits or ≥30 days have elapsed since the last Progress Note or Evaluation.
    /// </summary>
    /// <param name="patientId">Patient to check.</param>
    /// <param name="clinicId">Clinic (tenant) scope.</param>
    Task<ComplianceResult> CheckProgressNoteRequiredAsync(Guid patientId, Guid clinicId);

    /// <summary>
    /// Validates the 8-minute billing rule for a note.
    /// Returns a warning or hard stop when billed units do not correspond to documented minutes.
    /// </summary>
    /// <param name="durationMinutes">Total timed minutes documented in the note.</param>
    /// <param name="billedUnits">Number of timed CPT units billed.</param>
    ComplianceResult ValidateEightMinuteRule(int durationMinutes, int billedUnits);

    /// <summary>
    /// Enforces signature immutability: a signed note cannot be edited.
    /// </summary>
    /// <param name="note">The note being evaluated.</param>
    ComplianceResult EnforceSignatureLock(SOAPNote note);

    /// <summary>
    /// Signs a note, capturing timestamp and user, and enforcing immutability.
    /// Returns a hard stop if the note is already signed.
    /// </summary>
    /// <param name="note">The note to sign.</param>
    /// <param name="userId">The user signing the note.</param>
    ComplianceResult SignNote(SOAPNote note, string userId);
}
