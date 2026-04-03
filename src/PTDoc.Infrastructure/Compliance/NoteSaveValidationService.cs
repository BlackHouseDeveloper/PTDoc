using PTDoc.Application.Compliance;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Compliance;

public sealed class NoteSaveValidationService(IRulesEngine rulesEngine) : INoteSaveValidationService
{
    public async Task<ValidationResult> ValidateAsync(NoteSaveComplianceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var merged = ValidationResult.Valid();

        if (request.NoteType == NoteType.Daily && !request.ExistingNoteId.HasValue)
        {
            var progressNoteValidation = await rulesEngine.CheckProgressNoteDueAsync(
                request.PatientId,
                request.DateOfService,
                ct);
            merged.MergeFrom(progressNoteValidation);
        }

        var normalizedEntries = (request.CptEntries ?? [])
            .Select(entry => new CptCodeEntry
            {
                Code = entry.Code?.Trim() ?? string.Empty,
                Units = entry.Units,
                Minutes = entry.Minutes,
                IsTimed = entry.IsTimed
            })
            .ToList();

        TimedUnitCalculator.EnforceKnownTimedCptStatus(normalizedEntries);

        foreach (var entry in normalizedEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Code))
            {
                merged.Errors.Add("Missing CPT data");
            }

            if (entry.Units < 0)
            {
                merged.Errors.Add($"CPT code {entry.Code} has an invalid unit count.");
            }

            if (entry.Minutes < 0)
            {
                merged.Errors.Add($"CPT code {entry.Code} must record zero or more minutes.");
            }
        }

        var timedEntries = normalizedEntries
            .Where(TimedUnitCalculator.IsRelevantTimedEntry)
            .ToList();

        foreach (var entry in timedEntries.Where(entry => entry.Units > 0 && entry.Minutes is <= 0))
        {
            merged.Errors.Add($"Timed CPT code {entry.Code} must record minutes greater than zero.");
        }

        if (merged.Errors.Count > 0)
        {
            merged.IsValid = false;
            merged.Errors = merged.Errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return merged;
        }

        var missingTimedMinutes = timedEntries
            .Where(entry => entry.Units > 0 && entry.Minutes is null)
            .Select(entry => entry.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingTimedMinutes.Count > 0)
        {
            if (request.TotalTimedMinutes.HasValue)
            {
                var aggregateEntry = new CptCodeEntry
                {
                    Code = timedEntries.First().Code,
                    Units = timedEntries.Sum(TimedUnitCalculator.ResolveRequestedUnits),
                    Minutes = request.TotalTimedMinutes.Value,
                    IsTimed = true
                };

                var aggregateValidation = await rulesEngine.ValidateTimedUnitsAsync([aggregateEntry], ct);
                merged.MergeFrom(aggregateValidation);
                return merged;
            }

            if (request.AllowIncompleteTimedEntries)
            {
                merged.Warnings.Add(
                    $"Timed CPT minutes are missing for {string.Join(", ", missingTimedMinutes)}. 8-minute validation skipped until minutes are provided.");
                merged.Warnings = merged.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return merged;
            }

            merged.Errors.Add(
                $"Timed CPT minutes are required for {string.Join(", ", missingTimedMinutes)}.");
            merged.Errors = merged.Errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            merged.IsValid = false;
            return merged;
        }

        if (timedEntries.Count == 0)
        {
            merged.IsValid = merged.Errors.Count == 0;
            return merged;
        }

        var timedValidation = await rulesEngine.ValidateTimedUnitsAsync(normalizedEntries, ct);
        merged.MergeFrom(timedValidation);
        return merged;
    }
}
