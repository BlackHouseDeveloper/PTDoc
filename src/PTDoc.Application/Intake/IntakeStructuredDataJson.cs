using System.Text.Json;
using System.Text.Json.Serialization;
using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.Intake;

public static class IntakeStructuredDataJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static bool TryParse(string? json, out IntakeStructuredDataDto payload, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "{}", StringComparison.Ordinal))
        {
            payload = new IntakeStructuredDataDto();
            errorMessage = null;
            return true;
        }

        try
        {
            payload = JsonSerializer.Deserialize<IntakeStructuredDataDto>(json, SerializerOptions) ?? new IntakeStructuredDataDto();
            payload.BodyPartSelections ??= new List<IntakeBodyPartSelectionDto>();
            payload.MedicationIds ??= new List<string>();
            payload.PainDescriptorIds ??= new List<string>();
            errorMessage = null;
            return true;
        }
        catch (JsonException)
        {
            payload = new IntakeStructuredDataDto();
            errorMessage = "Structured intake data is not valid JSON.";
            return false;
        }
    }

    public static string Serialize(IntakeStructuredDataDto payload)
    {
        payload.BodyPartSelections ??= new List<IntakeBodyPartSelectionDto>();
        payload.MedicationIds ??= new List<string>();
        payload.PainDescriptorIds ??= new List<string>();
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static bool TryNormalize(
        IntakeStructuredDataDto payload,
        IIntakeReferenceDataCatalogService catalogService,
        out IntakeStructuredDataNormalizationResult normalizationResult,
        out IntakeStructuredDataValidationResult validationResult)
    {
        validationResult = new IntakeStructuredDataValidationResult();
        normalizationResult = new IntakeStructuredDataNormalizationResult();

        payload ??= new IntakeStructuredDataDto();
        payload.BodyPartSelections ??= new List<IntakeBodyPartSelectionDto>();
        payload.MedicationIds ??= new List<string>();
        payload.PainDescriptorIds ??= new List<string>();

        var catalog = catalogService.GetCatalog();
        var normalized = new IntakeStructuredDataDto
        {
            SchemaVersion = string.IsNullOrWhiteSpace(payload.SchemaVersion)
                ? catalog.Version
                : payload.SchemaVersion.Trim()
        };

        var normalizedBodySelections = new Dictionary<string, NormalizedBodySelection>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < payload.BodyPartSelections.Count; index++)
        {
            var selection = payload.BodyPartSelections[index] ?? new IntakeBodyPartSelectionDto();
            var path = $"structuredData.bodyPartSelections[{index}]";
            var bodyPartId = selection.BodyPartId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(bodyPartId))
            {
                validationResult.AddError($"{path}.bodyPartId", "BodyPartId is required.");
                continue;
            }

            var bodyPart = catalogService.GetBodyPart(bodyPartId);
            if (bodyPart is null)
            {
                validationResult.AddError($"{path}.bodyPartId", $"Unknown body part id '{bodyPartId}'.");
                continue;
            }

            if (!normalizedBodySelections.TryGetValue(bodyPart.Id, out var normalizedSelection))
            {
                normalizedSelection = new NormalizedBodySelection(bodyPart);
                normalizedBodySelections[bodyPart.Id] = normalizedSelection;
            }

            var lateralities = (selection.Lateralities ?? new List<string>())
                .Select(NormalizeLaterality)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var hasUnknownLaterality = (selection.Lateralities ?? new List<string>())
                .Any(value => !string.IsNullOrWhiteSpace(value) && NormalizeLaterality(value) is null);

            if (bodyPart.SupportsLaterality)
            {
                if (lateralities.Count == 0)
                {
                    validationResult.AddError($"{path}.lateralities", $"Laterality is required for '{bodyPart.Label}'.");
                }
                else
                {
                    foreach (var laterality in lateralities)
                    {
                        normalizedSelection.Lateralities.Add(laterality!);
                    }
                }

                if (hasUnknownLaterality)
                {
                    validationResult.AddError($"{path}.lateralities", "Only 'left' and 'right' lateralities are allowed.");
                }
            }
            else if ((selection.Lateralities ?? new List<string>()).Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                validationResult.AddError($"{path}.lateralities", $"Laterality is not allowed for '{bodyPart.Label}'.");
            }

            var digitIds = (selection.DigitIds ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (bodyPart.SupportsDigitSelection)
            {
                var validDigits = bodyPart.DigitOptions
                    .ToDictionary(option => option.Id, option => option, StringComparer.OrdinalIgnoreCase);

                foreach (var digitId in digitIds)
                {
                    if (!validDigits.ContainsKey(digitId))
                    {
                        validationResult.AddError(
                            $"{path}.digitIds",
                            $"Unknown digit id '{digitId}' for body part '{bodyPart.Label}'.");
                        continue;
                    }

                    normalizedSelection.DigitIds.Add(digitId);
                }
            }
            else if (digitIds.Count > 0)
            {
                validationResult.AddError($"{path}.digitIds", $"Digit selection is not allowed for '{bodyPart.Label}'.");
            }
        }

        var normalizedMedicationIds = NormalizeCatalogIds(
            payload.MedicationIds,
            catalogService.GetMedications(),
            item => item.Id,
            item => item.DisplayOrder,
            "structuredData.medicationIds",
            "medication",
            value => catalogService.GetMedication(value),
            validationResult);

        var normalizedPainDescriptorIds = NormalizeCatalogIds(
            payload.PainDescriptorIds,
            catalogService.GetPainDescriptors(),
            item => item.Id,
            item => item.DisplayOrder,
            "structuredData.painDescriptorIds",
            "pain descriptor",
            value => catalogService.GetPainDescriptor(value),
            validationResult);

        if (!validationResult.IsValid)
        {
            return false;
        }

        normalized.BodyPartSelections = normalizedBodySelections.Values
            .OrderBy(selection => selection.BodyPart.GroupDisplayOrder)
            .ThenBy(selection => selection.BodyPart.DisplayOrder)
            .Select(selection => new IntakeBodyPartSelectionDto
            {
                BodyPartId = selection.BodyPart.Id,
                Lateralities = IntakeLateralityValues.Ordered
                    .Where(selection.Lateralities.Contains)
                    .ToList(),
                DigitIds = selection.BodyPart.DigitOptions
                    .OrderBy(option => option.DisplayOrder)
                    .Select(option => option.Id)
                    .Where(selection.DigitIds.Contains)
                    .ToList()
            })
            .ToList();

        normalized.MedicationIds = normalizedMedicationIds;
        normalized.PainDescriptorIds = normalizedPainDescriptorIds;

        normalizationResult = new IntakeStructuredDataNormalizationResult
        {
            StructuredData = normalized,
            StructuredDataJson = Serialize(normalized),
            PainMapDataJson = BuildPainMapProjectionJson(normalized, catalogService)
        };

        return true;
    }

    public static string BuildPainMapProjectionJson(
        IntakeStructuredDataDto payload,
        IIntakeReferenceDataCatalogService catalogService)
    {
        payload ??= new IntakeStructuredDataDto();
        payload.BodyPartSelections ??= new List<IntakeBodyPartSelectionDto>();

        var selections = payload.BodyPartSelections
            .Where(selection => !string.IsNullOrWhiteSpace(selection.BodyPartId))
            .Select(selection => new
            {
                Selection = selection,
                BodyPart = catalogService.GetBodyPart(selection.BodyPartId)
            })
            .Where(entry => entry.BodyPart is not null)
            .Select(entry => new
            {
                entry.Selection,
                BodyPart = entry.BodyPart!
            })
            .ToList();

        var regionKeys = selections
            .SelectMany(entry => ExpandLegacyRegionKeys(entry.BodyPart, entry.Selection))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var bodyPartSelections = selections
            .Select(entry => new
            {
                bodyPartId = entry.BodyPart.Id,
                label = entry.BodyPart.Label,
                groupId = entry.BodyPart.GroupId,
                groupTitle = entry.BodyPart.GroupTitle,
                groupKind = entry.BodyPart.GroupKind.ToString(),
                lateralities = (entry.Selection.Lateralities ?? new List<string>())
                    .Select(NormalizeLaterality)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                digitIds = (entry.Selection.DigitIds ?? new List<string>())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .ToArray();

        var projection = new
        {
            schemaVersion = string.IsNullOrWhiteSpace(payload.SchemaVersion) ? null : payload.SchemaVersion,
            selectedBodyRegion = regionKeys.FirstOrDefault(),
            selectedRegions = regionKeys,
            regions = regionKeys,
            selectedBodyPartIds = selections.Select(entry => entry.BodyPart.Id).ToArray(),
            bodyPartSelections
        };

        return JsonSerializer.Serialize(projection, SerializerOptions);
    }

    private static List<string> NormalizeCatalogIds<TItem>(
        IEnumerable<string>? ids,
        IReadOnlyList<TItem> catalogItems,
        Func<TItem, string> idSelector,
        Func<TItem, int> orderSelector,
        string errorPath,
        string itemLabel,
        Func<string, TItem?> resolver,
        IntakeStructuredDataValidationResult validationResult)
        where TItem : class
    {
        var values = (ids ?? Array.Empty<string>()).ToList();
        var normalizedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                validationResult.AddError($"{errorPath}[{index}]", $"A non-empty {itemLabel} id is required.");
                continue;
            }

            if (resolver(value) is null)
            {
                validationResult.AddError($"{errorPath}[{index}]", $"Unknown {itemLabel} id '{value}'.");
                continue;
            }

            normalizedIds.Add(value);
        }

        return catalogItems
            .Where(item => normalizedIds.Contains(idSelector(item)))
            .OrderBy(orderSelector)
            .Select(idSelector)
            .ToList();
    }

    private static string? NormalizeLaterality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            IntakeLateralityValues.Left => IntakeLateralityValues.Left,
            IntakeLateralityValues.Right => IntakeLateralityValues.Right,
            _ => null
        };
    }

    private static IEnumerable<string> ExpandLegacyRegionKeys(
        IntakeBodyPartItemDto bodyPart,
        IntakeBodyPartSelectionDto selection)
    {
        var lateralities = (selection.Lateralities ?? new List<string>())
            .Select(NormalizeLaterality)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (bodyPart.SupportsLaterality && lateralities.Count > 0)
        {
            foreach (var laterality in IntakeLateralityValues.Ordered.Where(lateralities.Contains))
            {
                yield return $"{bodyPart.Id}-{laterality}";
            }

            yield break;
        }

        yield return bodyPart.Id;
    }

    private sealed class NormalizedBodySelection
    {
        public NormalizedBodySelection(IntakeBodyPartItemDto bodyPart)
        {
            BodyPart = bodyPart;
        }

        public IntakeBodyPartItemDto BodyPart { get; }
        public HashSet<string> Lateralities { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> DigitIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
