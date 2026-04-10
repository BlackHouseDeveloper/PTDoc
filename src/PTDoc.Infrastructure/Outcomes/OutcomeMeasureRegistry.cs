using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Outcomes;

/// <summary>
/// Registry of supported outcome measure instruments.
/// Implements scoring interpretation, body-region mapping, and improvement calculation.
/// Per TDD §9 — Outcome Measures: auto-assigned by body region.
/// </summary>
public sealed class OutcomeMeasureRegistry : IOutcomeMeasureRegistry
{
    private static readonly Lazy<OutcomeMeasureCatalogAsset> CatalogAsset = new(
        () => EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<OutcomeMeasureCatalogAsset>(
            "PTDoc.Application.Data.OutcomeMeasureReferenceData.json"));

    private static readonly IReadOnlyList<OutcomeMeasureDefinition> _allMeasures = BuildRegistry();

    /// <inheritdoc />
    public IReadOnlyList<OutcomeMeasureDefinition> GetAllMeasures() => _allMeasures;

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
    {
        return _allMeasures
            .Where(m => m.RecommendedForBodyParts.Contains(bodyPart))
            .ToList()
            .AsReadOnly();
    }

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

    // ──────────────────────────────────────────────────────────────
    // Registry construction
    // ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<OutcomeMeasureDefinition> BuildRegistry()
    {
        return CatalogAsset.Value.Measures
            .Select(MapDefinition)
            .ToList()
            .AsReadOnly();
    }

    private static OutcomeMeasureDefinition MapDefinition(OutcomeMeasureDefinitionAsset asset)
    {
        var provenance = CloneProvenance(CatalogAsset.Value.Provenance);

        return new OutcomeMeasureDefinition
        {
            MeasureType = ParseEnum<OutcomeMeasureType>(asset.MeasureType, nameof(asset.MeasureType)),
            Abbreviation = asset.Abbreviation,
            FullName = asset.FullName,
            Description = asset.Description,
            MinScore = asset.MinScore,
            MaxScore = asset.MaxScore,
            HigherIsBetter = asset.HigherIsBetter,
            ScoreUnit = asset.ScoreUnit,
            MinimumClinicallyImportantDifference = asset.MinimumClinicallyImportantDifference,
            RecommendedForBodyParts = asset.RecommendedForBodyParts
                .Select(value => ParseEnum<BodyPart>(value, nameof(asset.RecommendedForBodyParts)))
                .ToList()
                .AsReadOnly(),
            ScoringBands = asset.ScoringBands
                .Select(band => new ScoringBand
                {
                    Label = band.Label,
                    MinScore = band.MinScore,
                    MaxScore = band.MaxScore
                })
                .ToList()
                .AsReadOnly(),
            Provenance = provenance
        };
    }

    private static TEnum ParseEnum<TEnum>(string rawValue, string fieldName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(rawValue, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Outcome measure catalog value '{rawValue}' is not a valid {typeof(TEnum).Name} for {fieldName}.");
    }

    private static ReferenceDataProvenance? CloneProvenance(ReferenceDataProvenance? provenance)
        => provenance is null
            ? null
            : new ReferenceDataProvenance
            {
                DocumentPath = provenance.DocumentPath,
                Version = provenance.Version,
                Notes = provenance.Notes
            };
}
