using System.Text.Json;
using System.Text.Json.Serialization;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Application.Intake;

/// <summary>Patient-owned visit answers. No assessment, plan, billing or clinician decisions.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class IntakeVisitQuestionnaire
{
    public int Version { get; set; } = 1;
    public int? CurrentPain { get; set; }
    public int? BestPain { get; set; }
    public int? WorstPain { get; set; }
    public string PainFrequency { get; set; } = string.Empty;
    public HashSet<string> Locations { get; set; } = [];
    public string? LocationDetails { get; set; }
    public string? PainDescription { get; set; }
    public HashSet<string> AssociatedSymptoms { get; set; } = [];
    public DailyQuestionnaireV1? Daily { get; set; }
    public WorkspaceProgressNoteQuestionnaireV2? Progress { get; set; }
    public WorkspaceDischargeV2? Discharge { get; set; }
    // Issued by the server; retained unchanged on every write and never selected by a URL.
    public List<ProgressActivityReference> ActivityOptions { get; set; } = [];

    public NoteWorkspaceV2Payload ToWorkspace(NoteType type) => new()
    {
        NoteType = type,
        Subjective = new()
        {
            CurrentPainScore = CurrentPain ?? 0, BestPainScore = BestPain ?? 0, WorstPainScore = WorstPain ?? 0,
            IsPainScoreDocumented = CurrentPain.HasValue, PainFrequency = PainFrequency,
            Locations = Locations, OtherLocation = LocationDetails, KnownCause = PainDescription
        },
        DailyTreatment = new() { Questionnaire = Daily, AssociatedSymptoms = AssociatedSymptoms },
        ProgressQuestionnaire = Progress ?? new(),
        Discharge = Discharge ?? new()
    };

    public static IntakeVisitQuestionnaire FromWorkspace(NoteWorkspaceV2Payload value, IntakeVisitQuestionnaire issued) => new()
    {
        CurrentPain = value.Subjective.IsPainScoreDocumented ? value.Subjective.CurrentPainScore : null,
        BestPain = value.Subjective.BestPainScore,
        WorstPain = value.Subjective.WorstPainScore,
        PainFrequency = value.Subjective.PainFrequency, Locations = value.Subjective.Locations,
        LocationDetails = value.Subjective.OtherLocation, PainDescription = value.Subjective.KnownCause,
        AssociatedSymptoms = value.DailyTreatment.AssociatedSymptoms,
        Daily = value.NoteType == NoteType.Daily ? value.DailyTreatment.Questionnaire : null,
        Progress = value.NoteType == NoteType.ProgressNote ? value.ProgressQuestionnaire : null,
        Discharge = value.NoteType == NoteType.Discharge ? value.Discharge : null,
        ActivityOptions = issued.ActivityOptions
    };

    public IReadOnlyList<string> Validate(NoteType type, bool submitting = false)
    {
        var errors = new List<string>();
        if (Version != 1) errors.Add("This visit questionnaire version is not supported.");
        if (CurrentPain is < 0 or > 10 || BestPain is < 0 or > 10 || WorstPain is < 0 or > 10)
            errors.Add("Pain ratings must be between 0 and 10.");
        if (submitting && CurrentPain is null) errors.Add("Record current pain, including zero when there is no pain.");
        if (Locations is null || AssociatedSymptoms is null || ActivityOptions is null)
            errors.Add("Visit questionnaire collections cannot be empty objects or null.");
        if (type == NoteType.Daily && (Daily is null || Progress is not null || Discharge is not null)
            || type == NoteType.ProgressNote && (Progress is null || Daily is not null || Discharge is not null)
            || type == NoteType.Discharge && (Discharge is null || Daily is not null || Progress is not null))
            errors.Add("Answer only the questionnaire issued for this visit.");
        if (Daily is not null)
        {
            errors.AddRange(DailyQuestionnaireDefinition.Validate(Daily));
            if (Daily.ImprovedActivities is null || Daily.SameActivities is null || Daily.WorseActivities is null || Daily.NewDifficultyActivities is null)
                errors.Add("Daily activity answers cannot be null.");
        }
        if (Progress is not null)
        {
            errors.AddRange(ProgressActivityTracking.Validate(Progress));
            if (Progress.ImprovedActivities is null || Progress.SameActivities is null || Progress.WorseActivities is null
                || Progress.NewDifficultyActivities is null || Progress.ImpactedAreas is null)
                errors.Add("Progress activity answers cannot be null.");
            Choice(Progress.OverallCondition, DailyQuestionnaireDefinition.OverallCondition.Options);
            Choice(Progress.GoalProgress, ["Yes", "Somewhat", "No", "Unsure"]);
            Choice(Progress.PainChange, DailyQuestionnaireDefinition.PainChange.Options);
            Choice(Progress.DailyActivityEase, DailyQuestionnaireDefinition.DailyActivityEase.Options);
            Choice(Progress.ReturnedToActivities, ["Yes - Fully", "Yes - Partially", "Not yet", "Not applicable"]);
            Choice(Progress.HepAdherence, DailyQuestionnaireDefinition.HepAdherence.Options);
            Choice(Progress.HepResponse, ["Helpful", "Too easy", "Too difficult", "Causing discomfort", "Not applicable"]);
        }
        if (Discharge?.PercentImproved is < 0 or > 100) errors.Add("Percent improved must be between 0 and 100.");
        if (!string.IsNullOrEmpty(PainFrequency) && !DailyQuestionnaireDefinition.PainFrequency.Options.Contains(PainFrequency))
            errors.Add("Select a supported pain frequency.");
        // Bound patient text without interpreting blanks as clinical answers.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(this));
        ValidateText(json.RootElement);
        return errors;
        void Choice(string? answer, IReadOnlyList<string> options)
        {
            if (!string.IsNullOrEmpty(answer) && !options.Contains(answer, StringComparer.Ordinal))
                errors.Add("Select a supported visit questionnaire answer.");
        }
        void ValidateText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString()!.Length > 2000)
                errors.Add("Visit answers must be 2000 characters or fewer.");
            else if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject()) ValidateText(property.Value);
            else if (element.ValueKind == JsonValueKind.Array)
            {
                if (element.GetArrayLength() > 500) errors.Add("Too many visit answers.");
                foreach (var item in element.EnumerateArray()) ValidateText(item);
            }
        }
    }
}
