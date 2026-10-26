using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Infrastructure.Notes.Workspace;

/// <summary>Shared server validation/canonicalization for workspace, generic note and sync writes.</summary>
public static class ProgressActivityWriteValidator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IWorkspaceReferenceCatalogService Catalogs = new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry());

    public static async Task<string> PrepareJsonAsync(ApplicationDbContext db, ClinicalNote? previous,
        Guid patientId, Guid noteId, string normalizedJson, CancellationToken ct)
    {
        ProgressActivityTracking.ValidateJsonWrite(previous?.ContentJson, normalizedJson);
        NoteWorkspaceV2Payload? payload;
        try { payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(normalizedJson, SerializerOptions); }
        catch (JsonException) { return normalizedJson; }
        if (payload?.ProgressQuestionnaire?.Activities is null) return normalizedJson;
        var errors = await PrepareAsync(db, Catalogs, payload, previous, patientId, noteId, ct);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static async Task<List<string>> PrepareAsync(ApplicationDbContext db, IWorkspaceReferenceCatalogService workspaceCatalogs,
        NoteWorkspaceV2Payload payload, ClinicalNote? note,
        Guid patientId, Guid noteId, CancellationToken ct)
    {
        try { ProgressActivityTracking.ValidateJsonWrite(note?.ContentJson, JsonSerializer.Serialize(payload, SerializerOptions)); }
        catch (InvalidOperationException ex) { return [ex.Message]; }
        var errors = ProgressActivityTracking.Validate(payload.ProgressQuestionnaire).ToList();
        if (errors.Count > 0 || payload.ProgressQuestionnaire.Activities is not { } current) return errors;
        NoteWorkspaceV2Payload? previous = null;
        try { if (note is not null) previous = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, SerializerOptions); }
        catch (JsonException) { }
        if (payload.NoteType != NoteType.ProgressNote)
        {
            // Other note types may carry historical progress evidence but cannot author change states.
            var historical = previous?.ProgressQuestionnaire;
            if (historical?.Activities is null && payload.SeedContext.SourceNoteId is { } historySourceId)
            {
                var sourceNote = await db.ClinicalNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == historySourceId
                    && n.PatientId == patientId && n.NoteStatus == NoteStatus.Signed, ct);
                if (sourceNote is not null)
                    historical = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(NoteWriteService.NormalizeContentJson(
                        sourceNote.NoteType, sourceNote.IsReEvaluation, sourceNote.DateOfService, sourceNote.ContentJson), SerializerOptions)?.ProgressQuestionnaire;
            }
            if (historical?.Activities is null
                || JsonSerializer.Serialize(historical.Activities, SerializerOptions) != JsonSerializer.Serialize(current, SerializerOptions)
                || historical.PercentImproved != payload.ProgressQuestionnaire.PercentImproved)
                return ["Only Progress notes may author progress activity changes; other note types preserve the signed source history."];
            return errors;
        }
        var baseline = previous?.ProgressQuestionnaire?.Activities?.Baseline;
        if (baseline is null)
        {
            var source = previous ?? payload;
            Guid? sourceId = note?.Id;
            if (note is null && payload.SeedContext.SourceNoteId is { } requestedSource)
            {
                var savedSource = await db.ClinicalNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == requestedSource
                    && n.PatientId == patientId && n.NoteStatus == NoteStatus.Signed, ct);
                if (savedSource is null) return ["The progress baseline source is not an accessible signed note for this patient."];
                source = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(NoteWriteService.NormalizeContentJson(savedSource.NoteType, savedSource.IsReEvaluation, savedSource.DateOfService, savedSource.ContentJson), SerializerOptions) ?? new();
                sourceId = savedSource.Id;
            }
            baseline = ProgressActivityTracking.BaselineFrom(source, sourceId).Baseline;
        }
        current.Baseline = baseline.ToList();
        var regions = baseline.Select(a => a.BodyPart).Append(payload.Objective.PrimaryBodyPart)
            .Concat(payload.Subjective.FunctionalLimitations.Select(f => f.BodyPart)).Where(b => b != BodyPart.Other).Distinct();
        var options = regions.SelectMany(b => ProgressActivityTracking.CatalogOptions(workspaceCatalogs.GetBodyRegionCatalog(b)))
            .Concat(previous?.ProgressQuestionnaire?.Activities?.NewlyDifficult ?? []).DistinctBy(a => a.Id).ToDictionary(a => a.Id);
        var selected = new List<ProgressActivityReference>();
        foreach (var activity in current.NewlyDifficult)
        {
            if (!options.TryGetValue(activity.Id, out var canonical))
                errors.Add("Select newly difficult activities from the documented body-region catalog.");
            else if (baseline.Any(a => a.BodyPart == canonical.BodyPart && a.Description.Equals(canonical.Description, StringComparison.OrdinalIgnoreCase)))
                errors.Add("An already recorded activity belongs in improved, same or worse, not newly difficult.");
            else selected.Add(canonical with { SourceNoteId = noteId });
        }
        current.NewlyDifficult = selected;
        errors.AddRange(ProgressActivityTracking.Validate(payload.ProgressQuestionnaire));
        return errors;
    }

}
