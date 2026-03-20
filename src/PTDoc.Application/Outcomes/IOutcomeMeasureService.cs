using PTDoc.Core.Models;

namespace PTDoc.Application.Outcomes;

/// <summary>
/// Service for recording and retrieving outcome measure results.
/// Persistence layer for the <see cref="OutcomeMeasureResult"/> entity.
/// </summary>
public interface IOutcomeMeasureService
{
    /// <summary>
    /// Records a new outcome measure result for a patient.
    /// </summary>
    /// <param name="patientId">The patient identifier.</param>
    /// <param name="measureType">The instrument used.</param>
    /// <param name="score">The recorded score.</param>
    /// <param name="clinicianId">The clinician recording the score.</param>
    /// <param name="noteId">Optional clinical note this measurement was taken during.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted result entity.</returns>
    Task<OutcomeMeasureResult> RecordResultAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        double score,
        Guid clinicianId,
        Guid? noteId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all recorded results for a patient, ordered chronologically.
    /// </summary>
    /// <param name="patientId">The patient identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OutcomeMeasureResult>> GetPatientHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns recorded results for a specific measure type for a patient, ordered chronologically.
    /// </summary>
    /// <param name="patientId">The patient identifier.</param>
    /// <param name="measureType">The instrument to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<OutcomeMeasureResult>> GetPatientHistoryByMeasureAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent result (by <c>DateRecorded</c>) for a patient and measure type.
    /// </summary>
    /// <param name="patientId">The patient identifier.</param>
    /// <param name="measureType">The instrument to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<OutcomeMeasureResult?> GetLatestResultAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lightweight payload raised by <c>OutcomeMeasurePanel</c> when a score is recorded.
/// Does not carry <c>ClinicianId</c> — the caller (API or service layer) must supply
/// the authenticated user's ID before persisting via <see cref="IOutcomeMeasureService"/>.
/// </summary>
public sealed record OutcomeScoreEntry(
    OutcomeMeasureType MeasureType,
    double Score,
    DateTime DateRecorded);
