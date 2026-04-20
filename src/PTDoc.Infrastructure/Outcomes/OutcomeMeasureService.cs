using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// EF Core-backed implementation of <see cref="IOutcomeMeasureService"/>.
/// Persists and retrieves <see cref="OutcomeMeasureResult"/> entities.
/// <c>ClinicId</c> is automatically populated from the active tenant context so that
/// results remain visible under the global query filter.
/// </summary>
public sealed class OutcomeMeasureService : IOutcomeMeasureService
{
    private readonly ApplicationDbContext _db;
    private readonly IOutcomeMeasureRegistry _outcomeMeasureRegistry;
    private readonly ITenantContextAccessor? _tenantContext;

    public OutcomeMeasureService(
        ApplicationDbContext db,
        IOutcomeMeasureRegistry outcomeMeasureRegistry,
        ITenantContextAccessor? tenantContext = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _outcomeMeasureRegistry = outcomeMeasureRegistry ?? throw new ArgumentNullException(nameof(outcomeMeasureRegistry));
        _tenantContext = tenantContext;
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
        if (!TryGetDefinition(measureType, out var definition))
        {
            throw new InvalidOperationException(
                $"Outcome measure type '{FormatMeasureTypeForError(measureType)}' is not recognized.");
        }

        if (!definition.IsSelectableForNewEntry)
        {
            throw new InvalidOperationException(
                $"Outcome measure '{definition.Abbreviation}' is historical-only and cannot be newly recorded.");
        }

        var result = new OutcomeMeasureResult
        {
            PatientId = patientId,
            MeasureType = measureType,
            Score = score,
            ClinicianId = clinicianId,
            NoteId = noteId,
            DateRecorded = DateTime.UtcNow,
            // Capture the current tenant's clinic ID so the result is visible
            // under the global query filter (tenant-scoped contexts filter on ClinicId).
            ClinicId = _tenantContext?.GetCurrentClinicId()
        };

        _db.OutcomeMeasureResults.Add(result);
        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    private bool TryGetDefinition(OutcomeMeasureType measureType, out OutcomeMeasureDefinition definition)
    {
        try
        {
            definition = _outcomeMeasureRegistry.GetDefinition(measureType);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            definition = null!;
            return false;
        }
    }

    private static string FormatMeasureTypeForError(OutcomeMeasureType measureType)
        => Enum.IsDefined(typeof(OutcomeMeasureType), measureType)
            ? measureType.ToString()
            : $"{measureType} ({(int)measureType})";

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
