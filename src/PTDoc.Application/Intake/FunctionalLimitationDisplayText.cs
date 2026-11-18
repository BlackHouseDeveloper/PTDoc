namespace PTDoc.Application.Intake;

/// <summary>Versioned presentation of app-authored intake activities, independent of canonical values and instrument wording.</summary>
public static class FunctionalLimitationDisplayText
{
    public const string DifficultyV1 = "difficulty-v1";

    public static string Format(string activity, string? wordingVersion) =>
        wordingVersion == DifficultyV1 && activity.StartsWith("Unable to ", StringComparison.OrdinalIgnoreCase)
            ? $"Difficulty with: {activity[10..]}"
            : activity;
}
