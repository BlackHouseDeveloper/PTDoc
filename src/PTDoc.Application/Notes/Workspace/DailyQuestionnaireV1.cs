using System.Text.Json;
using System.Text.Json.Serialization;

namespace PTDoc.Application.Notes.Workspace;

/// <summary>Daily-only seven-question update. Pain ratings/frequency stay in WorkspaceSubjectiveV2.</summary>
public sealed class DailyQuestionnaireV1
{
    public int Version { get; set; } = 1;
    public string OverallCondition { get; set; } = string.Empty;
    public string PainChange { get; set; } = string.Empty;
    public string DailyActivityEase { get; set; } = string.Empty;
    public HashSet<string> ImprovedActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SameActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorseActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> NewDifficultyActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string HepAdherence { get; set; } = string.Empty;
    public string AdditionalInformation { get; set; } = string.Empty;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record DailyQuestionDefinition(string Id, int ReferenceNumber, string Label, IReadOnlyList<string> Options);

/// <summary>Labels/options from the reviewed 13-question reference; IDs remain stable when layout changes.</summary>
public static class DailyQuestionnaireDefinition
{
    public const int Version = 1;
    public static readonly DailyQuestionDefinition OverallCondition = new("overall-condition", 1,
        "Since the initial evaluation how would you describe your overall condition?",
        ["Improved significantly", "Improved slightly", "No change", "Worse slightly", "Worse significantly"]);
    public static readonly DailyQuestionDefinition CurrentPain = new("current-pain", 3,
        "How would you rate your current pain level (0-10)?", []);
    public static readonly DailyQuestionDefinition PainChange = new("pain-change", 4,
        "Since your last visit, has your pain:", ["Decreased", "Stayed the same", "Increased"]);
    public static readonly DailyQuestionDefinition PainFrequency = new("pain-frequency", 5,
        "Frequency of pain:", ["Constant", "Intermittent", "Only with specific activities", "Resolved"]);
    public static readonly DailyQuestionDefinition DailyActivityEase = new("daily-activity-ease", 6,
        "Are you able to perform daily activities with:", ["More ease", "The same level of difficulty", "More difficulty"]);
    public static readonly DailyQuestionDefinition ActivityChanges = new("activity-changes", 7,
        "Which of these have improved since your last visit? (Check all that apply)",
        ["Walking", "Stairs", "Transfers (e.g., sit-to-stand)", "Reaching", "Lifting", "Balance", "Endurance", "None"]);
    public static readonly DailyQuestionDefinition HepAdherence = new("hep-adherence", 10,
        "Have you been performing your home exercise program as prescribed?",
        ["Daily", "A few times per week", "Rarely", "Not at all"]);
    public static readonly IReadOnlyList<string> ActivityStateOptions =
        ["Walking", "Stairs", "Transfers (e.g., sit-to-stand)", "Reaching", "Lifting", "Balance", "Endurance", "Self-care", "Work tasks", "Household chores"];
    public static readonly IReadOnlyList<DailyQuestionDefinition> Questions =
        [OverallCondition, CurrentPain, PainChange, PainFrequency, DailyActivityEase, ActivityChanges, HepAdherence];

    public static DailyQuestionnaireV1? Clone(DailyQuestionnaireV1? value) => value is null ? null
        : JsonSerializer.Deserialize<DailyQuestionnaireV1>(JsonSerializer.Serialize(value));

    public static IReadOnlyList<string> Validate(DailyQuestionnaireV1? value)
    {
        if (value is null) return [];
        var errors = new List<string>();
        if (value.Version != Version) errors.Add("This Daily questionnaire version is not supported. Reload with a compatible client.");
        Check(value.OverallCondition, OverallCondition); Check(value.PainChange, PainChange);
        Check(value.DailyActivityEase, DailyActivityEase); Check(value.HepAdherence, HepAdherence);
        if ((value.AdditionalInformation?.Length ?? 0) > 2000) errors.Add("Daily additional information must be 2000 characters or fewer.");
        CheckSet(value.ImprovedActivities, ActivityChanges.Options); CheckSet(value.SameActivities, ActivityStateOptions);
        CheckSet(value.WorseActivities, ActivityStateOptions); CheckSet(value.NewDifficultyActivities, ActivityStateOptions);
        if (value.ImprovedActivities?.Contains("None") == true && value.ImprovedActivities.Count > 1)
            errors.Add("Select None alone for improved activities.");
        return errors;
        void Check(string? answer, DailyQuestionDefinition question)
        {
            if (!string.IsNullOrEmpty(answer) && !question.Options.Contains(answer, StringComparer.Ordinal))
                errors.Add($"Select a supported answer for {question.Id}.");
        }
        void CheckSet(IEnumerable<string>? answers, IReadOnlyList<string> options)
        {
            if (answers?.Any(a => !options.Contains(a, StringComparer.Ordinal)) == true)
                errors.Add("Select supported Daily activity options.");
        }
    }

    public static string Summarize(DailyQuestionnaireV1? value)
    {
        if (value is null) return string.Empty;
        var parts = new List<string>();
        Add("Overall condition", value.OverallCondition); Add("Pain change", value.PainChange);
        Add("Daily activities", value.DailyActivityEase);
        Add("Improved", string.Join(", ", value.ImprovedActivities ?? []));
        Add("Same", string.Join(", ", value.SameActivities ?? []));
        Add("Worse", string.Join(", ", value.WorseActivities ?? []));
        Add("Newly difficult", string.Join(", ", value.NewDifficultyActivities ?? []));
        Add("HEP performed", value.HepAdherence); Add("Additional information", value.AdditionalInformation);
        return string.Join("; ", parts);
        void Add(string label, string? text) { if (!string.IsNullOrWhiteSpace(text)) parts.Add($"{label}: {text}"); }
    }
}
