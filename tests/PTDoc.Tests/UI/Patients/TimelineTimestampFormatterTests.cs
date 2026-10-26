using System.Globalization;
using System.Reflection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Patients.Profile.Models;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Patients;

[Trait("Category", "CoreCi")]
public sealed class TimelineTimestampFormatterTests
{
    [Theory]
    [InlineData("2026-10-26T01:30:00Z", "10/25/2026", "9:30 PM UTC-04:00")]
    [InlineData("2026-03-08T06:30:00Z", "3/8/2026", "1:30 AM UTC-05:00")]
    [InlineData("2026-03-08T07:30:00Z", "3/8/2026", "3:30 AM UTC-04:00")]
    [InlineData("2026-11-01T05:30:00Z", "11/1/2026", "1:30 AM UTC-04:00")]
    [InlineData("2026-11-01T06:30:00Z", "11/1/2026", "1:30 AM UTC-05:00")]
    public void ViewerZone_HandlesMidnightAndDaylightSavingWithoutChangingInstant(string utc, string date, string time)
    {
        var instant = DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        var entry = new TimelineEntry { TimestampUtc = instant };
        TimelineTimestampFormatter.Apply(entry, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(date, entry.Date);
        Assert.Equal(time, entry.Time);
        Assert.Equal(instant, entry.TimestampUtc);
        Assert.Equal("America/New_York", entry.DisplayTimeZone);
    }

    [Fact]
    public void MissingViewerZone_UsesExplicitUtcInsteadOfHostTime()
    {
        var entry = new TimelineEntry { TimestampUtc = new DateTime(2026, 10, 26, 1, 30, 0) };
        TimelineTimestampFormatter.Apply(entry, null);
        Assert.Equal("10/26/2026", entry.Date);
        Assert.Equal("1:30 AM UTC", entry.Time);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IntakeWithoutSubmission_UsesModifiedTimeOrUnknownWithoutInventingSubmission(bool hasModifiedTime)
    {
        DateTime? modified = hasModifiedTime ? new DateTime(2026, 10, 26, 1, 30, 0, DateTimeKind.Utc) : null;
        var draft = new IntakeResponseDraft { LastModifiedUtc = modified };
        var row = InvokeFactory("CreateIntakeTimelineEntry", draft);
        Assert.Equal(modified, row.TimestampUtc);
        Assert.Equal("Intake Draft", row.Entry.Title);
        TimelineTimestampFormatter.Apply(row.Entry, TimeZoneInfo.Utc);
        Assert.Equal(hasModifiedTime ? "10/26/2026" : "Date unavailable", row.Entry.Date);
        Assert.False(draft.IsSubmitted);
    }

    [Fact]
    public void EventFactoriesUseModifiedSubmittedAndStartInstants_NotDatesOfService()
    {
        var instant = new DateTime(2026, 10, 26, 1, 30, 0, DateTimeKind.Utc);
        var note = InvokeFactory("CreateNoteTimelineEntry", new NoteListItemApiResponse { NoteType = "Evaluation", LastModifiedUtc = instant, DateOfService = instant.AddDays(-2) });
        var appointment = InvokeFactory("CreateAppointmentTimelineEntry", new AppointmentListItemResponse { StartTimeUtc = instant, AppointmentType = "FollowUp" });
        var intake = InvokeFactory("CreateIntakeTimelineEntry", new IntakeResponseDraft { SubmittedAt = instant, LastModifiedUtc = instant.AddDays(1), IsSubmitted = true });
        Assert.All(new[] { note, appointment, intake }, row => Assert.Equal(instant, row.Entry.TimestampUtc));
    }

    private static (DateTime? TimestampUtc, TimelineEntry Entry) InvokeFactory(string name, object value)
    {
        var method = typeof(PatientProfile).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        return ((DateTime?, TimelineEntry))method.Invoke(null, [value])!;
    }
}
