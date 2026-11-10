using System.Text.Json;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Infrastructure.Notes.Workspace;

/// <summary>Generic and sync writes may retain Daily outcome evidence, but cannot author it.</summary>
public static class DailyOutcomeWriteValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static void Validate(NoteType requestedType, ClinicalNote? previous, string normalizedJson)
    {
        if (requestedType != NoteType.Daily && previous?.NoteType != NoteType.Daily) return;
        var incoming = Read(normalizedJson);
        List<OutcomeMeasureEntryV2> historical = previous is null ? [] : Read(NoteWriteService.NormalizeContentJson(
            previous.NoteType, previous.IsReEvaluation, previous.DateOfService, previous.ContentJson));
        if (incoming.Count != historical.Count) throw ReadOnlyError();
        foreach (var entry in incoming)
        {
            var index = historical.FindIndex(old => old.MeasureType == entry.MeasureType
                && old.Score == entry.Score && old.RecordedAtUtc.Ticks == entry.RecordedAtUtc.Ticks
                && old.MinimumDetectableChange == entry.MinimumDetectableChange);
            if (index < 0) throw ReadOnlyError();
            historical.RemoveAt(index);
        }
    }

    private static List<OutcomeMeasureEntryV2> Read(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(json, Options);
            var entries = payload?.Objective?.OutcomeMeasures;
            if (entries is null || entries.Any(entry => entry is null)) throw ReadOnlyError();
            return entries;
        }
        catch (JsonException) { throw ReadOnlyError(); }
    }

    private static InvalidOperationException ReadOnlyError() =>
        new("Daily outcome measures are historical and read-only. Reload the saved results before saving.");
}
