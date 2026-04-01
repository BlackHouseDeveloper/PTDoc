namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProviderGoalProgressVm
{
    public string ProviderName { get; init; } = string.Empty;
    public int Achieved { get; init; }
    public int InProgress { get; init; }
}
