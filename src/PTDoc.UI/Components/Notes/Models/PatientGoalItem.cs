using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// Goal item displayed in the SOAP workspace sidebar.
/// </summary>
public class PatientGoalItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "ST"; // ST / LT
    public string Category { get; set; } = string.Empty;
    public string Timeline { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public string TargetDescription { get; set; } = string.Empty;
    public GoalTimeframe Timeframe { get; set; } = GoalTimeframe.ShortTerm;
    public GoalStatus Status { get; set; } = GoalStatus.Active;
    public bool HasQuantitativeProgress { get; set; }
}
