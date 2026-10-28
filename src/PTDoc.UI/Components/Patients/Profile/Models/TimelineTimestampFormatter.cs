using System.Globalization;

namespace PTDoc.UI.Components.Patients.Profile.Models;

public static class TimelineTimestampFormatter
{
    public static void Apply(TimelineEntry entry, TimeZoneInfo? viewerTimeZone)
    {
        if (entry.TimestampUtc is null || entry.TimestampUtc == default(DateTime))
        {
            entry.Date = "Date unavailable";
            entry.Time = string.Empty;
            entry.DisplayTimeZone = string.Empty;
            return;
        }

        // Timeline API fields are UTC, including legacy values lacking a DateTime kind.
        var utc = DateTime.SpecifyKind(entry.TimestampUtc.Value, DateTimeKind.Utc);
        var zone = viewerTimeZone ?? TimeZoneInfo.Utc;
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var offset = zone.GetUtcOffset(utc);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        var offsetLabel = offset == TimeSpan.Zero
            ? "UTC"
            : $"UTC{sign}{magnitude.Hours:00}:{magnitude.Minutes:00}";
        entry.Date = local.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
        entry.Time = $"{local.ToString("h:mm tt", CultureInfo.InvariantCulture)} {offsetLabel}";
        entry.DisplayTimeZone = zone.Id;
    }
}
