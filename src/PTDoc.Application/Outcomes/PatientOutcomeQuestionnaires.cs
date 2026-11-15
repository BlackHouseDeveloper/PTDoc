using System.Collections.Immutable;
using PTDoc.Core.Models;

namespace PTDoc.Application.Outcomes;

public sealed record PatientOutcomeQuestionnaire(VersionedOutcomeDefinition Definition,
    string Instructions, ImmutableArray<BodyPart> BodyParts);

/// <summary>An issued, version-pinned choice set. The server owns Choices and all computed results.</summary>
public sealed class IntakePatientOutcomes
{
    public int Version { get; set; } = 1;
    public string SelectionPolicy { get; set; } = PatientOutcomeSelection.Policy;
    public List<BodyPart> RecordedRegions { get; set; } = [];
    public List<PatientOutcomeQuestionnaire> Choices { get; set; } = [];
    public List<PatientOutcomeResponse> Responses { get; set; } = [];
}

public sealed record PatientOutcomeResponse(string InstrumentId, string DefinitionVersion,
    ImmutableArray<OutcomeAnswer> Answers, VersionedOutcomeResult? Result = null, bool Selected = true);

public sealed record WorkspaceIntakeOutcomeSnapshot(Guid IntakeId, DateTime AnswerVersionUtc,
    ImmutableArray<PatientOutcomeQuestionnaire> Questionnaires, ImmutableArray<PatientOutcomeResponse> Responses);

public static class PatientOutcomeSelection
{
    public const string Policy = "patient-choice-by-visit-region-v1";

    public static IReadOnlyList<PatientOutcomeQuestionnaire> Relevant(
        IEnumerable<PatientOutcomeQuestionnaire> catalog, IEnumerable<BodyPart> regions, NoteType noteType,
        bool hasOtherLowerLimbRegion = false)
    {
        if (noteType is not (NoteType.Evaluation or NoteType.ProgressNote or NoteType.Discharge)) return [];
        var affected = regions.Where(r => Enum.IsDefined(r) && r != BodyPart.Other).ToHashSet();
        return catalog.Where(q => q.Definition.Administration == OutcomeAdministration.PatientReported
                && (q.BodyParts.Any(affected.Contains) || hasOtherLowerLimbRegion && q.Definition.InstrumentId == "lefs"))
            .DistinctBy(q => (q.Definition.InstrumentId, q.Definition.Version)).ToArray();
    }
}
