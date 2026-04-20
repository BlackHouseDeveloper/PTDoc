using PTDoc.Application.Intake;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

public sealed class IntakeDraftCanonicalizer(
    IOutcomeMeasureRegistry outcomeMeasureRegistry,
    IIntakeBodyPartMapper intakeBodyPartMapper) : IIntakeDraftCanonicalizer
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
