namespace PTDoc.UI.Components.Appointments;

internal static class AppointmentsDateFormatter
{
    public static DateTime GetStartOfWeek(DateTime date)
    {
        var daysFromSunday = (int)date.DayOfWeek;
        return date.Date.AddDays(-daysFromSunday);
    }

    public static string FormatWeekRange(DateTime selectedDate)
    {
        var startOfWeek = GetStartOfWeek(selectedDate);
        var endOfWeek = startOfWeek.AddDays(6);

        if (startOfWeek.Year != endOfWeek.Year)
        {
            return $"{startOfWeek:MMM d, yyyy} - {endOfWeek:MMM d, yyyy}";
        }

        if (startOfWeek.Month != endOfWeek.Month)
        {
            return $"{startOfWeek:MMM d} - {endOfWeek:MMM d, yyyy}";
        }

        return $"{startOfWeek:MMMM d} - {endOfWeek:%d}, {endOfWeek:yyyy}";
    }
}
