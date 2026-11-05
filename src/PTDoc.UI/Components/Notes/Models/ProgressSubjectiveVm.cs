namespace PTDoc.UI.Components.Notes.Models;

public sealed class ProgressSubjectiveVm
{
    public decimal? PercentImproved { get; set; }
    public PTDoc.Application.Notes.Workspace.ProgressActivityTrackingV1? Activities { get; set; }
    public string OverallCondition { get; set; } = string.Empty;
    public string GoalProgress { get; set; } = string.Empty;
    public string PainChange { get; set; } = string.Empty;
    public string DailyActivityEase { get; set; } = string.Empty;
    public HashSet<string> ImprovedActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SameActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> WorseActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> NewDifficultyActivities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ImpactedAreas { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ReturnedToActivities { get; set; } = string.Empty;
    public string HepAdherence { get; set; } = string.Empty;
    public string HepResponse { get; set; } = string.Empty;
    public bool? HasSetbacksOrNewSymptoms { get; set; }
    public string? SetbackDetails { get; set; }
    public bool? HasMedicalChanges { get; set; }
    public string? AdditionalInformation { get; set; }
}
