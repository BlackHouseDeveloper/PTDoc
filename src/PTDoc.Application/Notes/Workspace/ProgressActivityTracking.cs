using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PTDoc.Core.Models;

namespace PTDoc.Application.Notes.Workspace;

public sealed record ProgressActivityReference(string Id, BodyPart BodyPart, string Description,
    string Category, string SourceVersion, Guid? SourceNoteId = null, Guid? SourceIntakeId = null);

public sealed class ProgressActivityTrackingV1
{
    public int Version { get; set; } = 1;
    public List<ProgressActivityReference> Baseline { get; set; } = [];
    public HashSet<string> ImprovedIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> SameIds { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> WorseIds { get; set; } = new(StringComparer.Ordinal);
    public List<ProgressActivityReference> NewlyDifficult { get; set; } = [];
}

public static class ProgressActivityTracking
{
    public static ProgressActivityTrackingV1 Clone(ProgressActivityTrackingV1 value) =>
        JsonSerializer.Deserialize<ProgressActivityTrackingV1>(JsonSerializer.Serialize(value))!;

    public static IReadOnlyList<ProgressActivityReference> CatalogOptions(BodyRegionCatalog catalog)
    {
        var version = catalog.FunctionalLimitations.Provenance?.Version ?? "workspace-catalog";
        return catalog.FunctionalLimitationCategories.SelectMany(c => c.Items.Select(item =>
            new ProgressActivityReference(Key(catalog.BodyPart, c.Name, item), catalog.BodyPart, item, c.Name, version)))
            .DistinctBy(a => a.Id).ToArray();
    }

    public static ProgressActivityReference RecordedActivity(BodyPart region, string description, string category,
        Guid? sourceNoteId = null, Guid? sourceIntakeId = null) => new("recorded-" + Key(region, category, description),
            region, description, category, "recorded-limitation", sourceNoteId, sourceIntakeId);

    private static string Key(BodyPart region, string category, string text) => "activity-v1-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{region}\n{category.Trim()}\n{text.Trim()}"))).ToLowerInvariant();

    public static ProgressActivityTrackingV1 BaselineFrom(NoteWorkspaceV2Payload source, Guid? sourceNoteId)
    {
        var history = source.ProgressQuestionnaire?.Activities;
        var baseline = (history?.Baseline ?? []).Concat(history?.NewlyDifficult ?? []).ToList();
        baseline.AddRange(source.Subjective.FunctionalLimitations.Where(f => !string.IsNullOrWhiteSpace(f.Description))
            .Select(f => RecordedActivity(f.BodyPart, f.Description, f.Category, sourceNoteId, source.SeedContext.SourceIntakeId)));
        return new() { Baseline = baseline.DistinctBy(a => a.Id).ToList() };
    }

    public static IReadOnlyList<string> Validate(WorkspaceProgressNoteQuestionnaireV2 questionnaire)
    {
        var errors = new List<string>();
        if (questionnaire.PercentImproved is < 0 or > 100) errors.Add("Percent improved must be between 0 and 100.");
        var a = questionnaire.Activities;
        if (a is null) return errors;
        if (a.Version != 1) errors.Add("This progress activity version requires a compatible client.");
        if (a.Baseline is null || a.NewlyDifficult is null || a.ImprovedIds is null || a.SameIds is null || a.WorseIds is null)
        { errors.Add("Progress activity data is incomplete."); return errors; }
        if (a.Baseline.Concat(a.NewlyDifficult).Any(r => r is null || string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Description)))
        { errors.Add("Progress activities require stable identities and descriptions."); return errors; }
        var ids = a.Baseline.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != a.Baseline.Count || a.NewlyDifficult.Select(r => r.Id).Distinct().Count() != a.NewlyDifficult.Count)
            errors.Add("Progress activity identities must be unique.");
        if (a.ImprovedIds.Concat(a.SameIds).Concat(a.WorseIds).Any(id => !ids.Contains(id)))
            errors.Add("Choose change states from the recorded activity baseline.");
        if (a.ImprovedIds.Overlaps(a.SameIds) || a.ImprovedIds.Overlaps(a.WorseIds) || a.SameIds.Overlaps(a.WorseIds))
            errors.Add("An activity may have only one change state per progress visit.");
        return errors;
    }

    // All supported write paths use this guard before replacing a saved workspace document.
    public static void ValidateJsonWrite(string? previous, string? incoming)
    {
        var before = Read(previous); var after = Read(incoming);
        if (after is not null && Validate(after) is { Count: > 0 } errors)
            throw new InvalidOperationException(string.Join(" ", errors));
        if (before?.Activities is not null && after?.Activities is null)
            throw new InvalidOperationException("Reload this progress note with a compatible client before saving its activity history.");
        if (before?.Activities is { } old && after?.Activities is { } updated
            && (old.Baseline.Count != updated.Baseline?.Count || old.Baseline.Any(a => !updated.Baseline!.Contains(a))))
            throw new InvalidOperationException("Recorded progress activity history cannot be replaced. Reload the saved baseline.");

    }

    private static WorkspaceProgressNoteQuestionnaireV2? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var p in document.RootElement.EnumerateObject())
                if (p.Name.Equals("progressQuestionnaire", StringComparison.OrdinalIgnoreCase))
                {
                    try { return p.Value.Deserialize<WorkspaceProgressNoteQuestionnaireV2>(new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
                    catch (JsonException) { throw new InvalidOperationException("Progress questionnaire content is invalid."); }
                }
        }
        catch (JsonException) { }
        return null;
    }
}
