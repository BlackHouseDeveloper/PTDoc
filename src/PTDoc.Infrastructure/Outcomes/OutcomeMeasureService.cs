using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// EF Core-backed implementation of <see cref="IOutcomeMeasureService"/>.
/// Persists and retrieves <see cref="OutcomeMeasureResult"/> entities.
/// </summary>
public sealed class OutcomeMeasureService : IOutcomeMeasureService
{
    private readonly ApplicationDbContext _db;

    public OutcomeMeasureService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<OutcomeMeasureResult> RecordResultAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        double score,
        Guid clinicianId,
        Guid? noteId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OutcomeMeasureResult
        {
            PatientId = patientId,
            MeasureType = measureType,
            Score = score,
            ClinicianId = clinicianId,
            NoteId = noteId,
            DateRecorded = DateTime.UtcNow
        };

        _db.OutcomeMeasureResults.Add(result);
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutcomeMeasureResult>> GetPatientHistoryAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var results = await _db.OutcomeMeasureResults
            .Where(r => r.PatientId == patientId)
            .OrderBy(r => r.DateRecorded)
            .ToListAsync(cancellationToken);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutcomeMeasureResult>> GetPatientHistoryByMeasureAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        CancellationToken cancellationToken = default)
    {
        var results = await _db.OutcomeMeasureResults
            .Where(r => r.PatientId == patientId && r.MeasureType == measureType)
            .OrderBy(r => r.DateRecorded)
            .ToListAsync(cancellationToken);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OutcomeMeasureResult?> GetLatestResultAsync(
        Guid patientId,
        OutcomeMeasureType measureType,
        CancellationToken cancellationToken = default)
    {
        return await _db.OutcomeMeasureResults
            .Where(r => r.PatientId == patientId && r.MeasureType == measureType)
            .OrderByDescending(r => r.DateRecorded)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
