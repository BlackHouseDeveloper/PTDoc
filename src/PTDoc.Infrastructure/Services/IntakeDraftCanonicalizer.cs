using PTDoc.Application.Intake;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

public sealed class IntakeDraftCanonicalizer(
    IOutcomeMeasureRegistry outcomeMeasureRegistry,
    IIntakeBodyPartMapper intakeBodyPartMapper,
    IIntakeReferenceDataCatalogService intakeReferenceData) : IIntakeDraftCanonicalizer
{
    public IntakeResponseDraft CreateCanonicalCopy(
        IntakeResponseDraft draft,
        IntakeStructuredDataDto? structuredDataOverride = null)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var copy = IntakeDraftPersistence.CreatePersistenceCopy(draft);
        var effectiveStructuredData = CloneStructuredData(structuredDataOverride ?? copy.StructuredData);
        copy.StructuredData = effectiveStructuredData;
        copy.RecommendedOutcomeMeasures = BuildCanonicalRecommendedOutcomeMeasures(
            copy.RecommendedOutcomeMeasures,
            effectiveStructuredData);
        copy.AssignedOutcomeMeasures = BuildAssignedOutcomeMeasures(
            copy.AssignedOutcomeMeasures ?? [],
            effectiveStructuredData);
        copy.InitialOutcomeMeasureReports = NormalizeInitialOutcomeMeasureReports(copy.InitialOutcomeMeasureReports ?? []);

        return copy;
    }

    private HashSet<string> BuildCanonicalRecommendedOutcomeMeasures(
        IEnumerable<string> existingRecommendations,
        IntakeStructuredDataDto? structuredData)
    {
        if ((structuredData?.BodyPartSelections.Count ?? 0) > 0)
        {
            return structuredData!.BodyPartSelections
                .Select(selection => intakeBodyPartMapper.MapBodyPartId(selection.BodyPartId))
                .Distinct()
                .SelectMany(outcomeMeasureRegistry.GetRecommendedMeasureAbbreviationsForBodyPart)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return existingRecommendations
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => outcomeMeasureRegistry.TryNormalizeRecommendedMeasure(value, out var canonical)
                ? canonical
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private List<AssignedOutcomeMeasureDraft> BuildAssignedOutcomeMeasures(
        IEnumerable<AssignedOutcomeMeasureDraft> existingAssignments,
        IntakeStructuredDataDto? structuredData)
    {
        if ((structuredData?.BodyPartSelections.Count ?? 0) == 0)
        {
            return existingAssignments
                .Where(assignment => !string.IsNullOrWhiteSpace(assignment.BodyPartId)
                    || !string.IsNullOrWhiteSpace(assignment.MeasureAbbreviation))
                .Select(CloneAssignment)
                .ToList();
        }

        return structuredData!.BodyPartSelections
            .Where(selection => !string.IsNullOrWhiteSpace(selection.BodyPartId))
            .GroupBy(selection => selection.BodyPartId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildAssignedOutcomeMeasure(group.First(), structuredData.SchemaVersion))
            .ToList();
    }

    private AssignedOutcomeMeasureDraft BuildAssignedOutcomeMeasure(
        IntakeBodyPartSelectionDto selection,
        string? referenceVersion)
    {
        var canonicalBodyPart = intakeBodyPartMapper.MapBodyPartId(selection.BodyPartId);
        var bodyPart = intakeReferenceData.GetBodyPart(selection.BodyPartId);
        var assignment = new AssignedOutcomeMeasureDraft
        {
            BodyPartId = selection.BodyPartId,
            BodyPartLabel = bodyPart?.Label ?? selection.BodyPartId,
            CanonicalBodyPart = canonicalBodyPart.ToString(),
            Laterality = FormatLaterality(selection.Lateralities),
            ReferenceVersion = string.IsNullOrWhiteSpace(referenceVersion) ? null : referenceVersion,
            IsPrimary = true
        };

        if (outcomeMeasureRegistry.TryGetPrimaryRecommendedMeasureForBodyPart(canonicalBodyPart, out var definition))
        {
            assignment.MeasureAbbreviation = definition.Abbreviation;
            assignment.MeasureFullName = definition.FullName;
            assignment.RequiresClinicalConfirmation = false;
        }
        else
        {
            assignment.RequiresClinicalConfirmation = true;
        }

        return assignment;
    }

    private static List<InitialOutcomeMeasureReportDraft> NormalizeInitialOutcomeMeasureReports(
        IEnumerable<InitialOutcomeMeasureReportDraft> reports)
    {
        return reports
            .Where(report => report.Skipped
                || !string.IsNullOrWhiteSpace(report.AssignedMeasureAbbreviation)
                || !string.IsNullOrWhiteSpace(report.PatientEnteredMeasureName)
                || !string.IsNullOrWhiteSpace(report.ScoreText)
                || report.CompletedDate.HasValue
                || !string.IsNullOrWhiteSpace(report.Notes))
            .Select(report => new InitialOutcomeMeasureReportDraft
            {
                AssignedMeasureAbbreviation = TrimOrNull(report.AssignedMeasureAbbreviation),
                PatientEnteredMeasureName = TrimOrNull(report.PatientEnteredMeasureName),
                ScoreText = TrimOrNull(report.ScoreText),
                CompletedDate = report.CompletedDate,
                Notes = TrimOrNull(report.Notes),
                Skipped = report.Skipped
            })
            .ToList();
    }

    private static AssignedOutcomeMeasureDraft CloneAssignment(AssignedOutcomeMeasureDraft assignment)
    {
        return new AssignedOutcomeMeasureDraft
        {
            BodyPartId = TrimOrNull(assignment.BodyPartId),
            BodyPartLabel = TrimOrNull(assignment.BodyPartLabel),
            CanonicalBodyPart = TrimOrNull(assignment.CanonicalBodyPart),
            Laterality = TrimOrNull(assignment.Laterality),
            MeasureAbbreviation = TrimOrNull(assignment.MeasureAbbreviation),
            MeasureFullName = TrimOrNull(assignment.MeasureFullName),
            ReferenceVersion = TrimOrNull(assignment.ReferenceVersion),
            IsPrimary = assignment.IsPrimary,
            RequiresClinicalConfirmation = assignment.RequiresClinicalConfirmation
        };
    }

    private static string? FormatLaterality(IEnumerable<string>? lateralities)
    {
        var values = lateralities?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values is { Count: > 0 } ? string.Join(", ", values) : null;
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IntakeStructuredDataDto? CloneStructuredData(IntakeStructuredDataDto? structuredData)
    {
        if (structuredData is null)
        {
            return null;
        }

        var json = IntakeStructuredDataJson.Serialize(structuredData);
        return IntakeStructuredDataJson.TryParse(json, out var clone, out _)
            ? clone
            : new IntakeStructuredDataDto();
    }
}
