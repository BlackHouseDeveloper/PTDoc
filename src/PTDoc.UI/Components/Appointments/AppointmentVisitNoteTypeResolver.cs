namespace PTDoc.UI.Components.Appointments;

public static class AppointmentVisitNoteTypeResolver
{
    public static string Resolve(string appointmentType)
        => ResolveIntent(appointmentType).WorkspaceNoteType;

    public static AppointmentVisitNoteIntent ResolveIntent(string appointmentType)
    {
        var normalizedType = Normalize(appointmentType);

        return normalizedType switch
        {
            "initialevaluation" => new AppointmentVisitNoteIntent("Evaluation Note", AllowEvaluationFallback: false),
            "reevaluation" => new AppointmentVisitNoteIntent("Progress Note", AllowEvaluationFallback: false),
            "discharge" => new AppointmentVisitNoteIntent("Discharge Note", AllowEvaluationFallback: false),
            "followup" or "wellnessvisit" => new AppointmentVisitNoteIntent("Daily Treatment Note", AllowEvaluationFallback: true),
            _ => new AppointmentVisitNoteIntent("Daily Treatment Note", AllowEvaluationFallback: false)
        };
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }
}

public sealed record AppointmentVisitNoteIntent(string WorkspaceNoteType, bool AllowEvaluationFallback);
