using System.Text.Json.Serialization;

namespace PTDoc.Application.Intake;

public static class IntakeLateralityValues
{
    public const string Left = "left";
    public const string Right = "right";

    public static readonly IReadOnlyList<string> Ordered =
    [
        Left,
        Right
    ];
}

public sealed class IntakeStructuredDataDto
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = string.Empty;

    [JsonPropertyName("bodyPartSelections")]
    public List<IntakeBodyPartSelectionDto> BodyPartSelections { get; set; } = new();

    [JsonPropertyName("medicationIds")]
    public List<string> MedicationIds { get; set; } = new();

    [JsonPropertyName("painDescriptorIds")]
    public List<string> PainDescriptorIds { get; set; } = new();

    [JsonPropertyName("comorbidityIds")]
    public List<string> ComorbidityIds { get; set; } = new();

    [JsonPropertyName("assistiveDeviceIds")]
    public List<string> AssistiveDeviceIds { get; set; } = new();

    [JsonPropertyName("livingSituationIds")]
    public List<string> LivingSituationIds { get; set; } = new();

    [JsonPropertyName("houseLayoutOptionIds")]
    public List<string> HouseLayoutOptionIds { get; set; } = new();

    [JsonPropertyName("noMedications")]
    public bool NoMedications { get; set; }

    [JsonPropertyName("noComorbidities")]
    public bool NoComorbidities { get; set; }

    [JsonPropertyName("noAssistiveDevices")]
    public bool NoAssistiveDevices { get; set; }

    [JsonPropertyName("functionalLimitations")]
    public List<IntakeFunctionalLimitationSelectionDto> FunctionalLimitations { get; set; } = new();

    [JsonPropertyName("subjective")]
    public IntakeSubjectiveDataDto Subjective { get; set; } = new();

    [JsonPropertyName("clinicalContext")]
    public IntakeClinicalContextDto ClinicalContext { get; set; } = new();
}

public sealed class IntakeClinicalContextDto
{
    [JsonPropertyName("noteType")]
    public string NoteType { get; set; } = "Evaluation";
}

public sealed class IntakeFunctionalLimitationSelectionDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("bodyPart")]
    public string BodyPart { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("activity")]
    public string Activity { get; set; } = string.Empty;
}

public sealed class IntakeSubjectiveDataDto
{
    [JsonPropertyName("priorFunctionalLevel")]
    public List<string> PriorFunctionalLevel { get; set; } = new();

    [JsonPropertyName("onsetDate")]
    public DateTime? OnsetDate { get; set; }

    [JsonPropertyName("onsetOverAYearAgo")]
    public bool OnsetOverAYearAgo { get; set; }

    [JsonPropertyName("causeUnknown")]
    public bool CauseUnknown { get; set; }

    [JsonPropertyName("knownCause")]
    public string? KnownCause { get; set; }

    [JsonPropertyName("hasImaging")]
    public bool? HasImaging { get; set; }

    [JsonPropertyName("imagingModalities")]
    public List<string> ImagingModalities { get; set; } = new();

    [JsonPropertyName("otherImagingModality")]
    public string? OtherImagingModality { get; set; }

    [JsonPropertyName("imagingFindings")]
    public string? ImagingFindings { get; set; }
}

public sealed class IntakeBodyPartSelectionDto
{
    [JsonPropertyName("bodyPartId")]
    public string BodyPartId { get; set; } = string.Empty;

    [JsonPropertyName("lateralities")]
    public List<string> Lateralities { get; set; } = new();

    [JsonPropertyName("digitIds")]
    public List<string> DigitIds { get; set; } = new();
}

public sealed class IntakeStructuredDataValidationResult
{
    public Dictionary<string, string[]> Errors { get; } = new(StringComparer.Ordinal);

    public bool IsValid => Errors.Count == 0;

    public void AddError(string key, string message)
    {
        if (Errors.TryGetValue(key, out var existing))
        {
            Errors[key] = existing.Concat([message]).Distinct(StringComparer.Ordinal).ToArray();
            return;
        }

        Errors[key] = [message];
    }
}

public sealed class IntakeStructuredDataNormalizationResult
{
    public IntakeStructuredDataDto StructuredData { get; init; } = new();
    public string StructuredDataJson { get; init; } = "{}";
    public string PainMapDataJson { get; init; } = "{}";
}
