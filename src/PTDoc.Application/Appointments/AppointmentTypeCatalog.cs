using PTDoc.Core.Models;

namespace PTDoc.Application.Appointments;

/// <summary>
/// Authoritative appointment types available to scheduling workflows.
/// </summary>
public static class AppointmentTypeCatalog
{
    public static IReadOnlyList<AppointmentTypeDefinition> All { get; } =
    [
        new(AppointmentType.InitialEvaluation, "Initial Evaluation", "Initial Evaluation"),
        new(AppointmentType.FollowUp, "Follow-up", "Follow Up"),
        new(AppointmentType.ReEvaluation, "Re-evaluation", "Re-Evaluation"),
        new(AppointmentType.Discharge, "Discharge", "Discharge")
    ];

    public static bool TryParse(string? value, out AppointmentType appointmentType)
    {
        var normalizedValue = Normalize(value);
        var match = All.FirstOrDefault(definition =>
            string.Equals(Normalize(definition.DisplayName), normalizedValue, StringComparison.Ordinal)
            || string.Equals(Normalize(definition.FormValue), normalizedValue, StringComparison.Ordinal));

        if (match is null)
        {
            appointmentType = AppointmentType.FollowUp;
            return false;
        }

        appointmentType = match.Value;
        return true;
    }

    public static string GetDisplayName(AppointmentType appointmentType) =>
        All.FirstOrDefault(definition => definition.Value == appointmentType)?.DisplayName
        ?? All.First(definition => definition.Value == AppointmentType.FollowUp).DisplayName;

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}

public sealed record AppointmentTypeDefinition(
    AppointmentType Value,
    string DisplayName,
    string FormValue);
