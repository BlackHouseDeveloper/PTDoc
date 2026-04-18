using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.Notes.Workspace;

public sealed class WorkspaceSubjectiveCatalogNormalizer
{
    public const string CatalogTextDelimiter = "; ";
    public const string MedicationTextDelimiter = ", ";

    private readonly IReadOnlyList<IntakeCatalogOptionDto> _houseLayoutOptions;
    private readonly Dictionary<string, string> _comorbidityLabelsByAlias;
    private readonly Dictionary<string, string> _assistiveDeviceLabelsByAlias;
    private readonly Dictionary<string, string> _livingSituationLabelsByAlias;
    private readonly Dictionary<string, string> _houseLayoutLabelsByAlias;
    private readonly Dictionary<string, IntakeMedicationItemDto> _medicationsByAlias;
    private readonly Dictionary<string, string> _medicationLabelsByAlias;

    public WorkspaceSubjectiveCatalogNormalizer(IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        var comorbidityOptions = intakeReferenceData.GetComorbidities();
        var assistiveDeviceOptions = intakeReferenceData.GetAssistiveDevices();
        var livingSituationOptions = intakeReferenceData.GetLivingSituations();
        _houseLayoutOptions = intakeReferenceData.GetHouseLayoutOptions();
        _comorbidityLabelsByAlias = BuildOptionLookup(comorbidityOptions);
        _assistiveDeviceLabelsByAlias = BuildOptionLookup(assistiveDeviceOptions);
        _livingSituationLabelsByAlias = BuildOptionLookup(livingSituationOptions);
        _houseLayoutLabelsByAlias = BuildOptionLookup(_houseLayoutOptions);
        _medicationsByAlias = BuildMedicationLookup(intakeReferenceData.GetMedications());
        _medicationLabelsByAlias = _medicationsByAlias.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.DisplayLabel,
            StringComparer.OrdinalIgnoreCase);
    }

    public SubjectiveCatalogSelectionResult ParseAssistiveDeviceSelections(AssistiveDeviceDetailsV2 details)
    {
        return ParseCatalogSelections(
            details.Devices,
            SplitCatalogTextValues(details.OtherDevice),
            _assistiveDeviceLabelsByAlias,
            CatalogTextDelimiter);
    }

    public HashSet<string> NormalizeAssistiveDeviceLabels(IEnumerable<string> values) =>
        NormalizeMatchedCatalogSet(values, _assistiveDeviceLabelsByAlias);

    public HashSet<string> NormalizeLivingSituationLabels(IEnumerable<string> values) =>
        NormalizeCatalogBackedSet(values, _livingSituationLabelsByAlias);

    public SubjectiveCatalogSelectionResult ParseHouseLayoutSelections(string? encodedValue)
    {
        return ParseCatalogSelections(
            [],
            SplitCatalogTextValues(encodedValue),
            _houseLayoutLabelsByAlias,
            CatalogTextDelimiter);
    }

    public string? ComposeHouseLayoutSelections(IEnumerable<string> selectedLabels, string? otherText)
    {
        var normalizedSelected = NormalizeMatchedCatalogValues(selectedLabels, _houseLayoutLabelsByAlias)
            .OrderBy(label => GetCatalogDisplayOrder(label, _houseLayoutOptions))
            .ToList();
        var unmatchedSelected = selectedLabels
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => !_houseLayoutLabelsByAlias.ContainsKey(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var segments = normalizedSelected
            .Concat(unmatchedSelected)
            .Concat(SplitCatalogTextValues(otherText));
        return JoinValues(segments, CatalogTextDelimiter);
    }

    public HashSet<string> NormalizeComorbidityLabels(IEnumerable<string> values) =>
        NormalizeCatalogBackedSet(values, _comorbidityLabelsByAlias);

    public SubjectiveCatalogSelectionResult ParseMedicationSelections(IReadOnlyCollection<MedicationEntryV2> entries)
    {
        return ParseCatalogSelections(
            entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => entry.Name),
            [],
            _medicationLabelsByAlias,
            MedicationTextDelimiter);
    }

    public List<string> NormalizeMedicationSelectionLabels(IEnumerable<string> values)
    {
        var labels = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Select(value => TryNormalizeMedicationLabel(value, out var canonical) ? canonical : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return labels
            .OrderBy(GetMedicationDisplayOrder)
            .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryNormalizeMedicationLabel(string value, out string canonical)
    {
        if (_medicationsByAlias.TryGetValue(value.Trim(), out var medication))
        {
            canonical = medication.DisplayLabel;
            return true;
        }

        canonical = value.Trim();
        return false;
    }

    private int GetMedicationDisplayOrder(string label)
    {
        return _medicationsByAlias.TryGetValue(label, out var medication)
            ? medication.DisplayOrder
            : int.MaxValue;
    }

    private static SubjectiveCatalogSelectionResult ParseCatalogSelections(
        IEnumerable<string> sourceValues,
        IEnumerable<string> freeTextValues,
        IReadOnlyDictionary<string, string> lookup,
        string joinDelimiter)
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<string>();

        foreach (var value in sourceValues.Concat(freeTextValues))
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (lookup.TryGetValue(trimmed, out var canonical))
            {
                selected.Add(canonical);
            }
            else
            {
                unmatched.Add(trimmed);
            }
        }

        return new SubjectiveCatalogSelectionResult
        {
            SelectedLabels = selected,
            OtherText = JoinValues(unmatched, joinDelimiter)
        };
    }

    private static Dictionary<string, string> BuildOptionLookup(IEnumerable<IntakeCatalogOptionDto> options)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in options)
        {
            TryAddAlias(lookup, option.Id, option.Label);
            TryAddAlias(lookup, option.Label, option.Label);
        }

        return lookup;
    }

    private static Dictionary<string, IntakeMedicationItemDto> BuildMedicationLookup(IEnumerable<IntakeMedicationItemDto> medications)
    {
        var lookup = new Dictionary<string, IntakeMedicationItemDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in medications)
        {
            TryAddMedicationAlias(lookup, item.Id, item);
            TryAddMedicationAlias(lookup, item.DisplayLabel, item);
            TryAddMedicationAlias(lookup, item.BrandName, item);
            TryAddMedicationAlias(lookup, item.GenericName, item);
        }

        return lookup;
    }

    private static void TryAddMedicationAlias(
        IDictionary<string, IntakeMedicationItemDto> lookup,
        string? alias,
        IntakeMedicationItemDto item)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        lookup.TryAdd(alias.Trim(), item);
    }

    private static void TryAddAlias(IDictionary<string, string> lookup, string? alias, string canonical)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return;
        }

        lookup.TryAdd(alias.Trim(), canonical);
    }

    private static HashSet<string> NormalizeCatalogBackedSet(
        IEnumerable<string> values,
        IReadOnlyDictionary<string, string> lookup)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
        {
            normalized.Add(lookup.TryGetValue(value, out var canonical) ? canonical : value);
        }

        return normalized;
    }

    private static HashSet<string> NormalizeMatchedCatalogSet(
        IEnumerable<string> values,
        IReadOnlyDictionary<string, string> lookup) =>
        NormalizeMatchedCatalogValues(values, lookup).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> NormalizeMatchedCatalogValues(
        IEnumerable<string> values,
        IReadOnlyDictionary<string, string> lookup)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Where(value => lookup.ContainsKey(value))
            .Select(value => lookup[value])
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> SplitCatalogTextValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetCatalogDisplayOrder(string label, IReadOnlyList<IntakeCatalogOptionDto> catalog)
    {
        var option = catalog.FirstOrDefault(item => string.Equals(item.Label, label, StringComparison.OrdinalIgnoreCase));
        return option?.DisplayOrder ?? int.MaxValue;
    }

    private static string? JoinValues(IEnumerable<string> values, string delimiter)
    {
        var segments = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return segments.Count == 0
            ? null
            : string.Join(delimiter, segments);
    }
}

public sealed class SubjectiveCatalogSelectionResult
{
    public HashSet<string> SelectedLabels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string? OtherText { get; init; }
    public bool HasSelections => SelectedLabels.Count > 0 || !string.IsNullOrWhiteSpace(OtherText);
}
