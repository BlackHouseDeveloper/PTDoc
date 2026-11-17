using System.Text.Json;
using System.Text.Json.Nodes;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Services;

/// <summary>Shared by staff and scoped patient-link writes. Client choices, regions and totals are never authoritative.</summary>
public static class IntakeOutcomePersistence
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly IntakeBodyPartMapper BodyParts = new(new IntakeReferenceDataCatalogService());

    public static bool HasOutcomeWorkflow(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject()
            .Any(p => p.Name.Equals("patientOutcomes", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null);
    }

    public static void InitializeEvaluation(IntakeResponseDraft draft)
    {
        draft.PatientOutcomes = null;
        Prepare(new IntakeForm { ResponseJson = "{}" }, draft);
    }

    public static string InitializeEvaluationJson(string json, IntakeStructuredDataDto? structured)
    {
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(json, Options) ?? throw new JsonException("Intake must be an object.");
        draft.StructuredData = structured;
        InitializeEvaluation(draft);
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new JsonException("Intake must be an object.");
        foreach (var key in root.Select(p => p.Key).Where(k => k.Equals("patientOutcomes", StringComparison.OrdinalIgnoreCase)).ToArray()) root.Remove(key);
        root["patientOutcomes"] = JsonSerializer.SerializeToNode(draft.PatientOutcomes, Options);
        return root.ToJsonString(Options);
    }

    public static IntakePatientOutcomes Issue(NoteWorkspaceV2Payload source, NoteType type) => Issue(
        source.Subjective.FunctionalLimitations.Select(f => f.BodyPart).Append(source.Objective.PrimaryBodyPart), type);

    private static IntakePatientOutcomes Issue(IEnumerable<BodyPart> regions, NoteType type)
    {
        var recorded = regions.Where(r => r != BodyPart.Other && Enum.IsDefined(r)).Distinct().ToList();
        return new() { RecordedRegions = recorded, Choices = PatientOutcomeSelection.Relevant(PatientOutcomeRelease.Catalog, recorded, type).ToList() };
    }

    public static void Prepare(IntakeForm intake, IntakeResponseDraft incoming, IntakeStructuredDataDto? authoritativeStructuredData = null)
    {
        var saved = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, Options)?.PatientOutcomes;
        var supplied = incoming.PatientOutcomes;
        if (supplied is null && saved?.Responses.Count > 0)
            throw new InvalidOperationException("Reload this intake with a compatible client to preserve outcome answers.");
        if (supplied is not null && (supplied.Version != 1 || supplied.SelectionPolicy != PatientOutcomeSelection.Policy
                || supplied.Responses is null || supplied.Responses.Count > 13))
            throw new InvalidOperationException("This outcome questionnaire version is not supported.");
        var type = IntakeQuestionnaireDefinitions.NoteTypeFor(intake.QuestionnaireDefinitionId) ?? NoteType.Evaluation;
        var regions = saved?.RecordedRegions ?? [];
        var bodySelections = (authoritativeStructuredData ?? incoming.StructuredData)?.BodyPartSelections;
        var relevant = type == NoteType.Evaluation
            ? regions.Concat(bodySelections?.Select(s => BodyParts.MapBodyPartId(s.BodyPartId)) ?? [])
            : regions;
        // These explicit intake regions have no BodyPart enum equivalent. Offer the broad lower-limb
        // instrument without relabeling a thigh/calf/shin complaint as a hip, knee or ankle diagnosis.
        var otherLegRegion = type == NoteType.Evaluation && bodySelections
            ?.Any(s => s.BodyPartId is "thigh" or "calf" or "shin") == true;
        // Retain an already-issued definition when future releases add a new version of an instrument.
        // Older scorers must remain registered for the lifetime of those issued questionnaires.
        var catalog = (saved?.Choices ?? []).Concat(PatientOutcomeRelease.Catalog)
            .DistinctBy(q => q.Definition.InstrumentId);
        var choices = PatientOutcomeSelection.Relevant(catalog, relevant, type, otherLegRegion).ToList();
        var result = new IntakePatientOutcomes { RecordedRegions = regions.ToList(), Choices = choices };
        foreach (var response in supplied?.Responses ?? [])
        {
            if (response is null || (!response.Answers.IsDefault && response.Answers.Length > 64)
                || result.Responses.Any(r => r.InstrumentId == response.InstrumentId))
                throw new InvalidOperationException("Outcome questionnaire responses must be unique and valid.");
            if (!response.Selected && saved?.Responses.FirstOrDefault(r => r.InstrumentId == response.InstrumentId
                    && r.DefinitionVersion == response.DefinitionVersion) is { } previous)
            {
                result.Responses.Add(previous with { Selected = false });
                continue;
            }
            var definition = choices.FirstOrDefault(c => c.Definition.InstrumentId == response.InstrumentId
                && c.Definition.Version == response.DefinitionVersion)?.Definition;
            if (definition is null || saved?.Choices.Any(c => c.Definition.InstrumentId == response.InstrumentId
                    && c.Definition.Version == response.DefinitionVersion) != true)
                throw new InvalidOperationException("Choose a questionnaire offered for this visit. If the body region changed, remove the prior selection first.");
            var scored = PatientOutcomeRelease.Registry.Score(response.InstrumentId, response.DefinitionVersion,
                OutcomeAdministration.PatientReported, response.Answers);
            if (scored.Result is null || scored.Errors.Length > 0)
                throw new InvalidOperationException("One or more outcome answers are invalid. Review the selected questionnaire.");
            result.Responses.Add(response with { Answers = scored.Result.Answers, Result = scored.Result });
        }
        incoming.PatientOutcomes = result;
    }
}
