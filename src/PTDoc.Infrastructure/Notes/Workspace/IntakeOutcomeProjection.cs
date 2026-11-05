using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Notes.Workspace;

/// <summary>Snapshots are copied from the locked intake; note writers cannot supply their own computed scores.</summary>
public static class IntakeOutcomeProjection
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static WorkspaceIntakeOutcomeSnapshot? From(IntakeForm intake)
    {
        var answers = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, Options)?.PatientOutcomes;
        if (answers is null || !answers.Responses.Any(r => r.Selected)) return null;
        var selected = answers.Responses.Where(r => r.Selected).ToImmutableArray();
        return new(intake.Id, intake.LastModifiedUtc, answers.Choices.Where(q => selected.Any(r =>
            r.InstrumentId == q.Definition.InstrumentId && r.DefinitionVersion == q.Definition.Version)).ToImmutableArray(), selected);
    }

    public static async Task ValidateAsync(ApplicationDbContext db, ClinicalNote? previous,
        Guid patientId, Guid noteId, NoteWorkspaceV2Payload incoming, CancellationToken ct)
    {
        var old = ReadSnapshot(previous?.ContentJson);
        if (incoming.IntakeOutcomes is not { } requested)
        {
            if (old is not null) throw new InvalidOperationException("Reload this note to preserve its submitted outcome evidence.");
            return;
        }
        var intake = await db.IntakeForms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == requested.IntakeId
            && f.PatientId == patientId && f.EncounterNoteId == noteId && f.IsLocked && f.SubmittedAt != null, ct);
        if (intake is null || (previous is not null && intake.ClinicId != previous.ClinicId)
            || IntakeQuestionnaireDefinitions.NoteTypeFor(intake.QuestionnaireDefinitionId) != incoming.NoteType
            || requested.AnswerVersionUtc != intake.LastModifiedUtc)
            throw new InvalidOperationException("The outcome source must be the submitted intake for this visit.");
        // Replace every submitted answer/total/definition with the immutable server source.
        incoming.IntakeOutcomes = From(intake)
            ?? throw new InvalidOperationException("The submitted intake has no selected outcome questionnaires.");
    }

    public static async Task<string> PrepareJsonAsync(ApplicationDbContext db, ClinicalNote? previous,
        Guid patientId, Guid noteId, string json, CancellationToken ct)
    {
        if (ReadSnapshot(json) is null && ReadSnapshot(previous?.ContentJson) is null) return json;
        NoteWorkspaceV2Payload? payload;
        try { payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(json, Options); }
        catch (JsonException) { throw new InvalidOperationException("Submitted outcome evidence must be a valid workspace document."); }
        if (payload is null) throw new InvalidOperationException("Reload this note to preserve its submitted outcome evidence.");
        await ValidateAsync(db, previous, patientId, noteId, payload, ct);
        return payload.IntakeOutcomes is null ? json : JsonSerializer.Serialize(payload, Options);
    }

    private static WorkspaceIntakeOutcomeSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name.Equals("intakeOutcomes", StringComparison.OrdinalIgnoreCase))
                    return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.Deserialize<WorkspaceIntakeOutcomeSnapshot>(Options);
            return null;
        }
        catch (JsonException) { throw new InvalidOperationException("Submitted outcome evidence is not a valid snapshot."); }
    }
}
