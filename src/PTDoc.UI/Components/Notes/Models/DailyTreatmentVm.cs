namespace PTDoc.UI.Components.Notes.Models;

public class DailyTreatmentVm
{
    public string ChangesSinceLastVisit { get; set; } = string.Empty;
    public string PainLevelChanges { get; set; } = string.Empty;
    public string SubjectiveUpdate { get; set; } = string.Empty;
    public string HepAdherence { get; set; } = string.Empty;
    public string HepUpdateNotes { get; set; } = string.Empty;
    public string FunctionalImprovements { get; set; } = string.Empty;
    public string NewOrChangedSymptoms { get; set; } = string.Empty;
    public string BarriersToProgress { get; set; } = string.Empty;
    public string PreviousTreatment { get; set; } = string.Empty;
    public HashSet<string> AssociatedSymptoms { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ResponseToTreatment { get; set; } = string.Empty;
}
