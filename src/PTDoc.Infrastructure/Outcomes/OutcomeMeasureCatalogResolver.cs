using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Outcomes;

internal static class OutcomeMeasureCatalogResolver
{
    private static readonly Lazy<OutcomeMeasureCatalogAsset> CatalogAsset = new(
        () => EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<OutcomeMeasureCatalogAsset>(
            "PTDoc.Application.Data.OutcomeMeasureReferenceData.json"));

    private static readonly Lazy<IReadOnlyList<OutcomeMeasureDefinition>> Definitions = new(BuildDefinitions);

    private static readonly Lazy<IReadOnlyDictionary<OutcomeMeasureType, OutcomeMeasureDefinition>> DefinitionsByType = new(
        () => Definitions.Value.ToDictionary(definition => definition.MeasureType));

    private static readonly Lazy<IReadOnlyList<OutcomeMeasureDefinition>> SelectableDefinitions = new(
        () => Definitions.Value
            .Where(definition => definition.IsSelectableForNewEntry)
            .ToList()
            .AsReadOnly());

    internal static IReadOnlyList<OutcomeMeasureDefinition> GetAllDefinitions() => Definitions.Value;

    internal static IReadOnlyList<OutcomeMeasureDefinition> GetSelectableDefinitions() => SelectableDefinitions.Value;

    internal static OutcomeMeasureDefinition GetDefinition(OutcomeMeasureType measureType)
    {
        if (DefinitionsByType.Value.TryGetValue(measureType, out var definition))
        {
            return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(measureType), $"No definition registered for {measureType}.");
    }

    internal static bool IsSelectableForNewEntry(OutcomeMeasureType measureType)
        => DefinitionsByType.Value.TryGetValue(measureType, out var definition) && definition.IsSelectableForNewEntry;

    internal static IReadOnlyList<OutcomeMeasureDefinition> GetSelectableDefinitionsForBodyPart(BodyPart bodyPart)
    {
        return SelectableDefinitions.Value
            .Where(definition => definition.RecommendedForBodyParts.Contains(bodyPart))
            .ToList()
            .AsReadOnly();
    }

    internal static IReadOnlyList<string> GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart bodyPart)
    {
        return GetSelectableDefinitionsForBodyPart(bodyPart)
            .Select(definition => definition.Abbreviation)
            .ToList()
            .AsReadOnly();
    }

    internal static bool TryResolveSupportedMeasureType(string? rawValue, out OutcomeMeasureType measureType)
    {
        measureType = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();
        var normalizedKey = NormalizeLookupKey(normalized);

        if (Enum.TryParse<OutcomeMeasureType>(normalized, ignoreCase: true, out var parsed)
            && DefinitionsByType.Value.ContainsKey(parsed))
        {
            measureType = parsed;
            return true;
        }

        foreach (var definition in Definitions.Value)
        {
            if (MatchesDefinition(normalized, normalizedKey, definition))
            {
                measureType = definition.MeasureType;
                return true;
            }
        }

        return false;
    }

    internal static bool TryNormalizeRecommendedMeasure(string? rawValue, out string canonicalAbbreviation)
    {
        canonicalAbbreviation = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalizedKey = NormalizeLookupKey(rawValue);
        if (normalizedKey.StartsWith("VASNPRS", StringComparison.Ordinal)
            || normalizedKey.StartsWith("NPRSVAS", StringComparison.Ordinal))
        {
            canonicalAbbreviation = GetDefinition(OutcomeMeasureType.NPRS).Abbreviation;
            return true;
        }

        if (!TryResolveSupportedMeasureType(rawValue, out var measureType))
        {
            return false;
        }

        if (measureType == OutcomeMeasureType.VAS)
        {
            canonicalAbbreviation = GetDefinition(OutcomeMeasureType.NPRS).Abbreviation;
            return true;
        }

        if (!IsSelectableForNewEntry(measureType))
        {
            return false;
        }

        canonicalAbbreviation = GetDefinition(measureType).Abbreviation;

        return true;
    }

    internal static bool TryFormatDisplayOption(string? rawValue, out string displayOption)
    {
        displayOption = string.Empty;
        if (!TryResolveSupportedMeasureType(rawValue, out var measureType))
        {
            return false;
        }

        var definition = GetDefinition(measureType);
        displayOption = $"{definition.Abbreviation} - {definition.FullName}";
        return true;
    }

    private static IReadOnlyList<OutcomeMeasureDefinition> BuildDefinitions()
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
            IsSelectableForNewEntry = asset.IsSelectableForNewEntry,
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
            Provenance = CloneProvenance(asset.Provenance) ?? provenance
        };
    }

    private static bool MatchesDefinition(
        string rawValue,
        string normalizedKey,
        OutcomeMeasureDefinition definition)
    {
        return normalizedKey == NormalizeLookupKey(definition.Abbreviation)
               || normalizedKey == NormalizeLookupKey(definition.FullName)
               || normalizedKey == NormalizeLookupKey(definition.MeasureType.ToString())
               || normalizedKey == NormalizeLookupKey(definition.Abbreviation + definition.FullName)
               || normalizedKey == NormalizeLookupKey(definition.FullName + definition.Abbreviation)
               || rawValue.StartsWith($"{definition.Abbreviation} - ", StringComparison.OrdinalIgnoreCase)
               || rawValue.StartsWith($"{definition.Abbreviation} — ", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
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
