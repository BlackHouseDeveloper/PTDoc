namespace PTDoc.UI.Components.Appointments;

public static class AppointmentVisitNoteTypeResolver
{
    public static string Resolve(string appointmentType)
    {
        var normalizedType = Normalize(appointmentType);

        return normalizedType switch
        {
            "initialevaluation" => "Evaluation Note",
            "reevaluation" => "Progress Note",
            "discharge" => "Discharge Note",
            _ => "Daily Treatment Note"
        };
    }

    private static string Normalize(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }
}
