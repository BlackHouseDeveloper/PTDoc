using System.Text.Json;

namespace PTDoc.Application.Services;

public static class IntakeDraftPersistence
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IntakeResponseDraft CreatePersistenceCopy(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var copy = JsonSerializer.Deserialize<IntakeResponseDraft>(
                       JsonSerializer.Serialize(draft, SerializerOptions),
                       SerializerOptions)
                   ?? new IntakeResponseDraft();

        NormalizeCanonicalSupplementalSelections(copy);
        return copy;
    }

    public static void NormalizeCanonicalSupplementalSelections(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if ((draft.StructuredData?.ComorbidityIds?.Count ?? 0) > 0)
        {
            draft.SelectedComorbidities.Clear();
        }

        if ((draft.StructuredData?.AssistiveDeviceIds?.Count ?? 0) > 0)
        {
            draft.SelectedAssistiveDevices.Clear();
        }

        if ((draft.StructuredData?.LivingSituationIds?.Count ?? 0) > 0)
        {
            draft.SelectedLivingSituations.Clear();
        }

        if ((draft.StructuredData?.HouseLayoutOptionIds?.Count ?? 0) > 0)
        {
            draft.SelectedHouseLayoutOptions.Clear();
        }
    }
}
