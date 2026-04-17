using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

public sealed class NoteSaveValidationService(
    ApplicationDbContext db,
    IRulesEngine rulesEngine,
    IWorkspaceReferenceCatalogService workspaceCatalogs) : INoteSaveValidationService
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
            .OfType<CptCodeEntry>()
            .Select(entry => new CptCodeEntry
            {
                Code = entry.Code?.Trim() ?? string.Empty,
                Units = entry.Units,
                Minutes = entry.Minutes,
                IsTimed = entry.IsTimed,
                Modifiers = (entry.Modifiers ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ModifierOptions = (entry.ModifierOptions ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SuggestedModifiers = (entry.SuggestedModifiers ?? [])
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ModifierSource = string.IsNullOrWhiteSpace(entry.ModifierSource)
                    ? null
                    : entry.ModifierSource.Trim()
            })
            .ToList();
        var diagnosisCodes = (request.DiagnosisCodes ?? [])
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        TimedUnitCalculator.EnforceKnownTimedCptStatus(normalizedEntries);

        if ((request.DiagnosisCodes ?? []).Any(code => string.IsNullOrWhiteSpace(code)))
        {
            merged.Errors.Add("ICD-10 diagnosis codes cannot be blank.");
        }

        if (diagnosisCodes.Count > 4)
        {
            merged.Errors.Add("Maximum of 4 ICD-10 diagnosis codes allowed.");
        }

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

            if (!string.IsNullOrWhiteSpace(entry.Code))
            {
                var canonicalEntry = FindCanonicalCptEntry(entry.Code);
                if (canonicalEntry is not null)
                {
                    var allowedModifiers = canonicalEntry.ModifierOptions
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var invalidModifiers = entry.Modifiers
                        .Where(value => !allowedModifiers.Contains(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (invalidModifiers.Count > 0)
                    {
                        merged.Errors.Add(
                            $"CPT code {entry.Code} includes unsupported modifier(s): {string.Join(", ", invalidModifiers)}.");
                    }
                }
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
            return await PopulateOverrideMetadataAsync(merged, ct);
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
                return await PopulateOverrideMetadataAsync(merged, ct);
            }

            if (request.AllowIncompleteTimedEntries)
            {
                merged.Warnings.Add(
                    $"Timed CPT minutes are missing for {string.Join(", ", missingTimedMinutes)}. 8-minute validation skipped until minutes are provided.");
                merged.Warnings = merged.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return await PopulateOverrideMetadataAsync(merged, ct);
            }

            merged.Errors.Add(
                $"Timed CPT minutes are required for {string.Join(", ", missingTimedMinutes)}.");
            merged.Errors = merged.Errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            merged.IsValid = false;
            return await PopulateOverrideMetadataAsync(merged, ct);
        }

        if (timedEntries.Count == 0)
        {
            merged.IsValid = merged.Errors.Count == 0 && !merged.RequiresOverride;
            return await PopulateOverrideMetadataAsync(merged, ct);
        }

        var timedValidation = await rulesEngine.ValidateTimedUnitsAsync(normalizedEntries, ct);
        merged.MergeFrom(timedValidation);
        return await PopulateOverrideMetadataAsync(merged, ct);

        CodeLookupEntry? FindCanonicalCptEntry(string code)
        {
            return workspaceCatalogs
                .SearchCpt(code, take: 100)
                .FirstOrDefault(entry => string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase));
        }
    }

    private async Task<ValidationResult> PopulateOverrideMetadataAsync(ValidationResult result, CancellationToken ct)
    {
        if (result.OverrideRequirements.Count == 0)
        {
            return result;
        }

        var attestationText = await db.ComplianceSettings
            .AsNoTracking()
            .Select(settings => settings.OverrideAttestationText)
            .FirstOrDefaultAsync(ct)
            ?? ComplianceSettings.DefaultOverrideAttestationText;

        foreach (var requirement in result.OverrideRequirements.Where(requirement => string.IsNullOrWhiteSpace(requirement.AttestationText)))
        {
            requirement.AttestationText = attestationText;
        }

        result.RequiresOverride = true;
        result.IsValid = result.Errors.Count == 0 && !result.RequiresOverride;
        return result;
    }
}
