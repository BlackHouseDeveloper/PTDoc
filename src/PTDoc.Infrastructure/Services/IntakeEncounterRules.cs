using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Services;
using PTDoc.Application.Notes.Workspace;
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
        var expectedType = IntakeQuestionnaireDefinitions.NoteTypeFor(intake.QuestionnaireDefinitionId);
        if (expectedType is null)
            return "This questionnaire version is not supported for editing.";
        var validNote = await db.ClinicalNotes.AsNoTracking().AnyAsync(note =>
            note.Id == intake.EncounterNoteId.Value && note.PatientId == intake.PatientId &&
            note.ClinicId == intake.ClinicId && note.NoteType == expectedType.Value &&
            note.IsReEvaluation == intake.EncounterIsReEvaluation && note.NoteStatus == NoteStatus.Draft &&
            !note.IsAddendum && note.SignedUtc == null && string.IsNullOrEmpty(note.SignatureHash), cancellationToken);
        return validNote ? null : "The associated visit is no longer available for intake changes.";
    }

    public static bool IsShort(IntakeForm intake) => intake.EncounterNoteId.HasValue &&
        IntakeQuestionnaireDefinitions.IsShort(intake.QuestionnaireDefinitionId);

    public static string PrepareShortResponse(IntakeForm intake, string? incomingJson)
    {
        var incoming = JsonSerializer.Deserialize<IntakeResponseDraft>(incomingJson ?? "", JsonOptions)
            ?? throw new JsonException("An intake response must be an object.");
        var saved = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, JsonOptions)!;
        var answer = incoming.VisitQuestionnaire;
        var issued = saved.VisitQuestionnaire;
        if (answer is null || issued is null) throw new InvalidOperationException("The issued visit questionnaire is required.");
        var errors = answer.Validate(IntakeQuestionnaireDefinitions.NoteTypeFor(intake.QuestionnaireDefinitionId)!.Value);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if (!answer.ActivityOptions.SequenceEqual(issued.ActivityOptions)
            || (issued.Progress?.Activities is { } baseline && (answer.Progress?.Activities is not { } updated
                || !baseline.Baseline.SequenceEqual(updated.Baseline))))
            throw new InvalidOperationException("The issued activity choices cannot be changed. Reload this intake.");
        if (answer.Progress?.Activities?.NewlyDifficult.Any(a => !issued.ActivityOptions.Contains(a)) == true)
            throw new InvalidOperationException("Choose newly difficult activities from the issued catalog.");
        // Preserve chart facts, the definition and baseline. A short visit has no chart-edit surface.
        if (answer.Progress is { } progress)
        {
            progress.CurrentPainLevel = answer.CurrentPain ?? 0;
            progress.BestPainLevel = answer.BestPain ?? 0;
            progress.WorstPainLevel = answer.WorstPain ?? 0;
            progress.PainFrequency = answer.PainFrequency;
        }
        saved.VisitQuestionnaire = answer;
        saved.CurrentStep = incoming.CurrentStep == (int)IntakeStep.Review ? (int)IntakeStep.Review : (int)IntakeStep.Demographics;
        saved.ConsentPacket = IntakeDraftPersistence.BuildCanonicalConsentPacket(incoming);
        saved.TermsOfServiceAccepted = incoming.TermsOfServiceAccepted;
        saved.AccuracyConfirmed = incoming.AccuracyConfirmed;
        return IntakeDraftPersistence.SerializePersistenceJson(saved);
    }

    public static string? ValidateSubmission(IntakeForm intake)
    {
        if (!IsShort(intake)) return null;
        try
        {
            var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, JsonOptions);
            if (draft?.VisitQuestionnaire is not { } answers) return "Complete the issued visit questionnaire.";
            var errors = answers.Validate(IntakeQuestionnaireDefinitions.NoteTypeFor(intake.QuestionnaireDefinitionId)!.Value, submitting: true);
            return errors.Count == 0 ? null : string.Join(" ", errors);
        }
        catch (JsonException) { return "The saved visit questionnaire is invalid."; }
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
