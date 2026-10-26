using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Services;
using PTDoc.Application.Intake;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>Server-owned association and version checks for encounter-specific intake.</summary>
public static class IntakeEncounterRules
{
    public const string EvaluationDefinition = IntakeQuestionnaireDefinitions.EvaluationFullV1;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<string?> ValidateWriteAsync(
        ApplicationDbContext db, IntakeForm intake, DateTime? expectedLastModifiedUtc,
        CancellationToken cancellationToken)
    {
        if (!intake.EncounterNoteId.HasValue) return null;
        if (!expectedLastModifiedUtc.HasValue || expectedLastModifiedUtc.Value != intake.LastModifiedUtc)
            return "This intake changed or its version is missing. Reload it before saving.";
        if (intake.QuestionnaireDefinitionId != EvaluationDefinition)
            return "This questionnaire version is not supported for editing.";
        var validNote = await db.ClinicalNotes.AsNoTracking().AnyAsync(note =>
            note.Id == intake.EncounterNoteId.Value && note.PatientId == intake.PatientId &&
            note.ClinicId == intake.ClinicId && note.NoteType == NoteType.Evaluation &&
            note.IsReEvaluation == intake.EncounterIsReEvaluation && note.NoteStatus == NoteStatus.Draft &&
            !note.IsAddendum && note.SignedUtc == null && string.IsNullOrEmpty(note.SignatureHash), cancellationToken);
        return validNote ? null : "The associated evaluation is no longer available for intake changes.";
    }

    public static string NormalizeResponseIdentity(IntakeForm intake, string responseJson)
    {
        if (!intake.EncounterNoteId.HasValue) return responseJson;
        var root = JsonNode.Parse(responseJson) as JsonObject
            ?? throw new JsonException("An intake response must be an object.");
        var authoritativeKeys = new[] { "intakeId", "patientId", "encounterNoteId", "questionnaireDefinitionId", "encounterIsReEvaluation" };
        foreach (var key in root.Select(property => property.Key).ToArray())
            if (authoritativeKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) root.Remove(key);
        root["intakeId"] = intake.Id;
        root["patientId"] = intake.PatientId;
        root["encounterNoteId"] = intake.EncounterNoteId.Value;
        root["questionnaireDefinitionId"] = intake.QuestionnaireDefinitionId;
        root["encounterIsReEvaluation"] = intake.EncounterIsReEvaluation;
        return root.ToJsonString();
    }

    public static bool HasSupportedResponseVersion(IntakeForm intake, string responseJson)
    {
        if (!intake.EncounterNoteId.HasValue) return true;
        try
        {
            return JsonSerializer.Deserialize<IntakeResponseDraft>(responseJson, JsonOptions)?.IntakeFlowVersion == IntakeQuestionnaireDefinitions.EvaluationFlowVersion;
        }
        catch (JsonException) { return false; }
    }
}
