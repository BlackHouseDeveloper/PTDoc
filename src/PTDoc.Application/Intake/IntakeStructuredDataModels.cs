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
