namespace PTDoc.UI.Components.Intake.Models;

/// <summary>Patient-facing labels for app-authored activities; never used for instrument items or persisted IDs.</summary>
public static class FunctionalLimitationDisplayText
{
    public static string Format(string activity) => activity.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase)
        ? $"Difficulty with: {activity[10..]}"
        : activity;
}
