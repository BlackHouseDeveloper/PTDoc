namespace PTDoc.Application.Settings;

public sealed record CanonicalVisitType(
    string Code,
    string Name,
    int DurationMinutes,
    bool RequiresIntake,
    bool PtaAllowed,
    bool IsBillable,
    int DisplayOrder);

public static class SchedulingDefaults
{
    public static readonly IReadOnlyList<CanonicalVisitType> VisitTypes =
    [
        new("initial-evaluation", "Initial Evaluation", 60, true, false, true, 1),
        new("re-evaluation", "Re-Evaluation", 60, false, false, true, 2),
        new("daily-treatment", "Daily Treatment", 45, false, true, true, 3),
        new("progress-note", "Progress Note", 45, false, true, true, 4),
        new("discharge", "Discharge", 30, false, false, true, 5),
        new("follow-up", "Follow-Up", 30, false, true, true, 6),
        new("group-therapy", "Group Therapy", 60, false, true, true, 7),
        new("dry-needling", "Dry Needling", 30, false, false, true, 8),
        new("telehealth-visit", "Telehealth Visit", 30, false, true, true, 9),
        new("home-health-visit", "Home Health Visit", 60, false, true, true, 10),
        new("consultation-non-billable", "Consultation (Non-Billable)", 15, false, false, false, 11),
        new("no-show", "No Show", 0, false, false, false, 12)
    ];

    public static readonly IReadOnlyList<(DayOfWeek Day, bool IsOpen)> WeeklyHours =
    [
        (DayOfWeek.Sunday, false),
        (DayOfWeek.Monday, true),
        (DayOfWeek.Tuesday, true),
        (DayOfWeek.Wednesday, true),
        (DayOfWeek.Thursday, true),
        (DayOfWeek.Friday, true),
        (DayOfWeek.Saturday, false)
    ];
}
