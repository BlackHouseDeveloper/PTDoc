using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Application.AI;

public enum NoteNarrativePurpose
{
    SubjectiveSummary = 1, ObjectiveSummary = 2, AssessmentSummary = 3, PlanSummary = 4,
    SoapSummary = 5, NextVisitPlan = 6, DischargeRecommendations = 7, DischargeFollowUp = 8, DischargeSummary = 9
}

// Only identity/version/purpose cross the client API. Sources are loaded from the authorized saved note.
public sealed record NoteNarrativeRequest(Guid NoteId, DateTime ExpectedLastModifiedUtc, NoteNarrativePurpose Purpose);
public sealed record NoteNarrativeInput(string Label, string Value);
public sealed record NoteNarrativeSection(NoteNarrativePurpose Purpose, string Text, AiPromptMetadata? Metadata);
public sealed record NoteNarrativeProvenance(NoteNarrativePurpose Purpose, DateTime SourceLastModifiedUtc,
    string SourceSha256, IReadOnlyList<string> TemplateVersions, IReadOnlyList<string> Models);
public sealed record NoteNarrativeResult
{
    public NoteNarrativeProvenance ToProvenance() => new(Purpose, SourceLastModifiedUtc, SourceSha256,
        Sections.Where(s => s.Metadata is not null).Select(s => s.Metadata!.TemplateVersion).Distinct().ToArray(),
        Sections.Where(s => s.Metadata is not null).Select(s => s.Metadata!.Model).Distinct().ToArray());
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public Guid NoteId { get; init; }
    public NoteNarrativePurpose Purpose { get; init; }
    public DateTime SourceLastModifiedUtc { get; init; }
    public string SourceSha256 { get; init; } = string.Empty;
    public IReadOnlyList<NoteNarrativeSection> Sections { get; init; } = Array.Empty<NoteNarrativeSection>();
    public string GeneratedText => string.Join("\n\n", Sections.Select(s =>
        Sections.Count == 1 ? s.Text : $"{NoteNarrativeContextBuilder.Title(s.Purpose)}:\n{s.Text}"));
}

public interface INoteNarrativeClient
{
    Task<NoteNarrativeResult> GenerateAsync(NoteNarrativeRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Server-only provider boundary. Input sources are constructed after authorization.</summary>
public interface INoteNarrativeGenerator
{
    Task<AiResult> GenerateSectionAsync(NoteNarrativePurpose purpose, IReadOnlyList<NoteNarrativeInput> inputs,
        CancellationToken cancellationToken = default);
}

/// <summary>Explicit allow-list of documented fields; defaults and unaccepted suggestions are not source facts.</summary>
public static class NoteNarrativeContextBuilder
{
    public static IReadOnlyList<NoteNarrativePurpose> SectionsFor(NoteNarrativePurpose purpose) => purpose switch
    {
        NoteNarrativePurpose.SoapSummary or NoteNarrativePurpose.DischargeSummary =>
            [NoteNarrativePurpose.SubjectiveSummary, NoteNarrativePurpose.ObjectiveSummary, NoteNarrativePurpose.AssessmentSummary, NoteNarrativePurpose.PlanSummary],
        _ when Enum.IsDefined(purpose) => [purpose],
        _ => throw new ArgumentOutOfRangeException(nameof(purpose))
    };

    public static string Title(NoteNarrativePurpose purpose) => purpose switch
    {
        NoteNarrativePurpose.SubjectiveSummary => "Subjective",
        NoteNarrativePurpose.ObjectiveSummary => "Objective",
        NoteNarrativePurpose.AssessmentSummary => "Assessment",
        NoteNarrativePurpose.PlanSummary => "Plan",
        NoteNarrativePurpose.NextVisitPlan => "Next visit plan",
        NoteNarrativePurpose.DischargeRecommendations => "Discharge recommendations",
        NoteNarrativePurpose.DischargeFollowUp => "Discharge follow-up",
        _ => "SOAP summary"
    };

    public static IReadOnlyList<NoteNarrativeInput> Build(NoteWorkspaceV2Payload payload, NoteNarrativePurpose purpose)
    {
        var inputs = new List<NoteNarrativeInput>();
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) inputs.Add(new(label, value)); }
        void AddMany(string label, IEnumerable<string> values) => Add(label, string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v))));
        switch (purpose)
        {
            case NoteNarrativePurpose.SubjectiveSummary:
                var s = payload.Subjective;
                Add("Patient-reported chief complaint", s.NarrativeContext.ChiefComplaint);
                Add("Patient-reported history", s.NarrativeContext.HistoryOfPresentIllness);
                Add("Patient-reported mechanism", s.NarrativeContext.MechanismOfInjury);
                if (s.IsPainScoreDocumented) Add("Documented current pain score /10", s.CurrentPainScore.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add("Overall symptom frequency", s.PainFrequency);
                AddMany("Functional limitations", s.FunctionalLimitations.Select(f => f.Description));
                Add("Additional limitations", s.AdditionalFunctionalLimitations);
                AddMany("Prior function", s.PriorFunctionalLevel);
                Add("Current function", s.CurrentLevelOfFunction);
                Add("Reported imaging findings", s.Imaging.Findings);
                if (payload.NoteType == NoteType.Discharge)
                {
                    Add("Patient-reported discharge outcome", payload.Discharge.PatientReportedOutcome);
                    Add("Patient-reported goals status (not clinician attainment)", payload.Discharge.GoalsMetStatus);
                    Add("Patient-reported remaining difficulty", payload.Discharge.RemainingDifficulty);
                    if (payload.Discharge.PercentImproved is { } percent)
                        Add("Patient-reported percent improved", percent.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                break;
            case NoteNarrativePurpose.ObjectiveSummary:
                var o = payload.Objective;
                foreach (var metric in o.Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Value)))
                    Add($"Documented {metric.BodyPart} {metric.MetricType} {metric.Name}", metric.Value);
                foreach (var test in o.SpecialTests.Where(t => !string.IsNullOrWhiteSpace(t.Result)))
                    Add($"Test {test.Name} {test.Side}", test.Result);
                Add("Clinical observations", o.ClinicalObservationNotes);
                Add("Gait observations", o.GaitObservation.AdditionalObservations);
                AddMany("Posture findings", o.PostureObservation.Findings);
                foreach (var exercise in o.ExerciseRows.Where(e => !string.IsNullOrWhiteSpace(e.ActualExercisePerformed)))
                    Add("Documented performed exercise", exercise.ActualExercisePerformed);
                // Suggested exercises and unchecked/default normal flags do not establish completed treatment.
                break;
            case NoteNarrativePurpose.AssessmentSummary:
                var a = payload.Assessment;
                Add("Clinician assessment", a.AssessmentNarrative); Add("Findings", a.FindingsSummary);
                Add("Deficits", a.DeficitsSummary); AddMany("Documented diagnosis", a.DiagnosisCodes.Select(d => $"{d.Code} {d.Description}"));
                AddMany("Accepted clinical goals", a.Goals.Select(g => g.Description));
                Add("Patient-reported priorities (not measured findings)", a.PatientPersonalGoals);
                Add("Clinician prognosis", a.PrognosisNarrative); Add("Prognosis selection", a.OverallPrognosis);
                Add("Skilled PT justification", a.SkilledPtJustification);
                break;
            case NoteNarrativePurpose.PlanSummary:
                AddPlan(payload.Plan, inputs);
                break;
            case NoteNarrativePurpose.NextVisitPlan:
                Add("Documented next-visit instructions", payload.Plan.FollowUpInstructions);
                Add("Documented plan", payload.Plan.PlanOfCareNarrative);
                AddMany("Planned treatment focus", payload.Plan.TreatmentFocuses);
                Add("Clinician assessment", payload.Assessment.AssessmentNarrative);
                AddMany("Accepted clinical goals", payload.Assessment.Goals.Select(g => g.Description));
                break;
            case NoteNarrativePurpose.DischargeRecommendations:
                Add("Clinician discharge reason", payload.Plan.PrimaryDischargeReason);
                Add("Patient-reported remaining difficulty", payload.Discharge.RemainingDifficulty);
                Add("Documented discharge recommendations", payload.Plan.DischargeRecommendations);
                Add("Documented discharge planning", payload.Plan.DischargePlanningNotes);
                Add("Documented home program", payload.Plan.HomeExerciseProgramNotes);
                break;
            case NoteNarrativePurpose.DischargeFollowUp:
                Add("Clinician discharge reason", payload.Plan.PrimaryDischargeReason);
                Add("Documented home program", payload.Plan.HomeExerciseProgramNotes);
                Add("Documented post-discharge instructions", payload.Plan.PostDischargeInstructions);
                Add("Documented follow-up", payload.Plan.FollowUpInstructions);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(purpose));
        }
        return inputs.AsReadOnly();
    }

    private static void AddPlan(WorkspacePlanV2 p, List<NoteNarrativeInput> inputs)
    {
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) inputs.Add(new(label, value)); }
        Add("Documented plan of care", p.PlanOfCareNarrative);
        if (p.TreatmentFrequencyDaysPerWeek.Count > 0) Add("Planned frequency days/week", string.Join(", ", p.TreatmentFrequencyDaysPerWeek));
        if (p.TreatmentDurationWeeks.Count > 0) Add("Planned duration weeks", string.Join(", ", p.TreatmentDurationWeeks));
        Add("Planned treatment focus", string.Join("; ", p.TreatmentFocuses));
        Add("Planned interventions (not evidence of completed treatment)", string.Join("; ", p.GeneralInterventions.Select(i => i.Name)));
        Add("Home program", p.HomeExerciseProgramNotes); Add("Follow-up", p.FollowUpInstructions);
        Add("Discharge planning", p.DischargePlanningNotes); Add("Discharge recommendations", p.DischargeRecommendations);
        Add("Clinician discharge reason", p.PrimaryDischargeReason); Add("Other discharge reason", p.OtherDischargeReasonExplanation);
        Add("Post-discharge instructions", p.PostDischargeInstructions);
        // Exclude prior generated ClinicalSummary/FullDischargeSummary to avoid recursive summaries.
    }
}
