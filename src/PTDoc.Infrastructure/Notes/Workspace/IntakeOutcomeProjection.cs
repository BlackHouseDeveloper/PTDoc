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
        if (!intake.IsLocked || intake.SubmittedAt is not { } submittedAt) return null;
        var answers = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, Options)?.PatientOutcomes;
        if (answers is null || !answers.Responses.Any(r => r.Selected)) return null;
        var selected = answers.Responses.Where(r => r.Selected).ToImmutableArray();
        return new(intake.Id, submittedAt, answers.Choices.Where(q => selected.Any(r =>
            r.InstrumentId == q.Definition.InstrumentId && r.DefinitionVersion == q.Definition.Version)).ToImmutableArray(), selected);
    }

    public static async Task ValidateAsync(ApplicationDbContext db, ClinicalNote? previous,
        Guid patientId, Guid noteId, Guid? expectedClinicId, NoteWorkspaceV2Payload incoming, CancellationToken ct,
        Guid? standaloneSeedIntakeId = null)
    {
        var old = ReadSnapshot(previous?.ContentJson);
        if (incoming.IntakeOutcomes is not { } requested)
        {
            if (old is not null) throw new InvalidOperationException("Reload this note to preserve its submitted outcome evidence.");
            return;
        }
        // Background contexts may have no tenant filter. Null is a legacy clinic value, not a wildcard.
        var intake = await db.IntakeForms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == requested.IntakeId
            && f.PatientId == patientId && f.ClinicId == expectedClinicId && f.IsLocked && f.SubmittedAt != null, ct);
        // Legacy patient-wide forms predate the encounter definition column and are Evaluation intakes.
        var definitionId = intake?.QuestionnaireDefinitionId
            ?? (intake?.EncounterNoteId is null ? IntakeQuestionnaireDefinitions.EvaluationFullV1 : null);
        if (intake is null || (previous is not null && intake.ClinicId != previous.ClinicId)
            || IntakeQuestionnaireDefinitions.NoteTypeFor(definitionId) != incoming.NoteType
            || requested.AnswerVersionUtc != intake.SubmittedAt
            || (old is not null && old.IntakeId != requested.IntakeId))
            throw new InvalidOperationException("The outcome source must be the submitted intake for this visit.");
        if (intake.EncounterNoteId is { } encounterNoteId)
        {
            if (encounterNoteId != noteId)
                throw new InvalidOperationException("The outcome source must be the submitted intake for this visit.");
        }
        else
        {
            var seed = incoming.SeedContext;
            // Only a server-selected new Evaluation seed can establish the standalone association.
            // Later generic/workspace/sync writes must retain the source already saved in this note.
            var canBind = previous is null
                ? standaloneSeedIntakeId == intake.Id
                : old?.IntakeId == intake.Id && previous.NoteType == NoteType.Evaluation && !previous.IsAddendum;
            if (!canBind || incoming.NoteType != NoteType.Evaluation || incoming.DryNeedling is not null
                || seed is null || seed.Kind != WorkspaceSeedKind.IntakePrefill || !seed.FromLockedSubmittedIntake
                || seed.SourceIntakeId != intake.Id || seed.SourceIntakeDefinitionId != definitionId
                || seed.SourceIntakeVersionUtc != intake.SubmittedAt || seed.SourceReferenceDateUtc != intake.SubmittedAt)
                throw new InvalidOperationException("The outcome source must be the submitted intake selected for this Evaluation.");
        }
        // Replace every submitted answer/total/definition with the immutable server source.
        incoming.IntakeOutcomes = From(intake)
            ?? throw new InvalidOperationException("The submitted intake has no selected outcome questionnaires.");
    }

    public static async Task<string> PrepareJsonAsync(ApplicationDbContext db, ClinicalNote? previous,
        Guid patientId, Guid noteId, Guid? expectedClinicId, string json, CancellationToken ct)
    {
        if (ReadSnapshot(json) is null && ReadSnapshot(previous?.ContentJson) is null) return json;
        NoteWorkspaceV2Payload? payload;
        try { payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(json, Options); }
        catch (JsonException) { throw new InvalidOperationException("Submitted outcome evidence must be a valid workspace document."); }
        if (payload is null) throw new InvalidOperationException("Reload this note to preserve its submitted outcome evidence.");
        await ValidateAsync(db, previous, patientId, noteId, expectedClinicId, payload, ct);
        return payload.IntakeOutcomes is null ? json : JsonSerializer.Serialize(payload, Options);
    }

    private static WorkspaceIntakeOutcomeSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            JsonElement? snapshot = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("intakeOutcomes", StringComparison.OrdinalIgnoreCase))
                {
                    if (snapshot.HasValue)
                        throw new InvalidOperationException("Submitted outcome evidence must not contain duplicate intakeOutcomes properties.");
                    snapshot = property.Value;
                }
            }
            return snapshot is not { } value || value.ValueKind == JsonValueKind.Null
                ? null
                : value.Deserialize<WorkspaceIntakeOutcomeSnapshot>(Options);
        }
        catch (JsonException) { throw new InvalidOperationException("Submitted outcome evidence is not a valid snapshot."); }
    }
}
