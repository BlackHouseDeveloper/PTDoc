using PTDoc.Application.Outcomes;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// Registry of supported outcome measure instruments.
/// Implements scoring interpretation, body-region mapping, and improvement calculation.
/// Per TDD §9 — Outcome Measures: auto-assigned by body region.
/// </summary>
public sealed class OutcomeMeasureRegistry : IOutcomeMeasureRegistry
{
    private static readonly IReadOnlyList<OutcomeMeasureDefinition> _allMeasures = OutcomeMeasureCatalogResolver.GetAllDefinitions();
    private static readonly IReadOnlyList<OutcomeMeasureDefinition> _selectableMeasures = OutcomeMeasureCatalogResolver.GetSelectableDefinitions();

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetAllMeasures() => _allMeasures;

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetSelectableMeasures() => _selectableMeasures;

    /// <inheritdoc />
    public OutcomeMeasureDefinition GetDefinition(OutcomeMeasureType measureType)
    {
        var definition = _allMeasures.FirstOrDefault(m => m.MeasureType == measureType);
        if (definition is null)
            throw new ArgumentOutOfRangeException(nameof(measureType), $"No definition registered for {measureType}.");
        return definition;
    }

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetMeasuresForBodyPart(BodyPart bodyPart)
        => OutcomeMeasureCatalogResolver.GetSelectableDefinitionsForBodyPart(bodyPart);

    /// <inheritdoc />
    public bool IsSelectableForNewEntry(OutcomeMeasureType measureType)
        => OutcomeMeasureCatalogResolver.IsSelectableForNewEntry(measureType);

    /// <inheritdoc />
    public IReadOnlyList<string> GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart bodyPart)
        => OutcomeMeasureCatalogResolver.GetRecommendedMeasureAbbreviationsForBodyPart(bodyPart);

    /// <inheritdoc />
    public bool TryResolveSupportedMeasureType(string rawValue, out OutcomeMeasureType measureType)
        => OutcomeMeasureCatalogResolver.TryResolveSupportedMeasureType(rawValue, out measureType);

    /// <inheritdoc />
    public bool TryNormalizeRecommendedMeasure(string rawValue, out string canonicalAbbreviation)
        => OutcomeMeasureCatalogResolver.TryNormalizeRecommendedMeasure(rawValue, out canonicalAbbreviation);

    /// <inheritdoc />
    public string InterpretScore(OutcomeMeasureType measureType, double score)
    {
        var definition = GetDefinition(measureType);

        // Find the matching band
        foreach (var band in definition.ScoringBands)
        {
            if (score >= band.MinScore && score <= band.MaxScore)
                return band.Label;
        }

        // Clamp to edges if score is outside defined range
        return score <= definition.MinScore
            ? definition.ScoringBands.First().Label
            : definition.ScoringBands.Last().Label;
    }

    /// <inheritdoc />
    public double CalculateImprovementPercent(OutcomeMeasureType measureType, double baselineScore, double currentScore)
    {
        var definition = GetDefinition(measureType);
        var rawChange = currentScore - baselineScore;
        var range = definition.MaxScore - definition.MinScore;

        if (range == 0)
            return 0;

        // For disability scales (higher = worse), improvement means score went down.
        // For function scales (higher = better), improvement means score went up.
        var changePercent = (rawChange / range) * 100.0;
        return definition.HigherIsBetter ? changePercent : -changePercent;
    }
}
