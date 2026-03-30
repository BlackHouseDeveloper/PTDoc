using PTDoc.Application.Notes.Workspace;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class PlanOfCareCalculator : IPlanOfCareCalculator
{
    private const int ProgressNoteDayInterval = 30;

    public ComputedPlanOfCareV2 Compute(PlanOfCareComputationRequest request)
    {
        var frequency = request.FrequencyDaysPerWeek
            .Where(value => value is >= 1 and <= 7)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        var duration = request.DurationWeeks
            .Where(value => value is >= 1 and <= 12)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        var noteDate = request.NoteDate.Date;
        var maxWeeks = duration.LastOrDefault();
        var endDate = maxWeeks > 0 ? noteDate.AddDays((maxWeeks * 7) - 1) : (DateTime?)null;

        var dueDates = new List<DateTime>();
        if (endDate.HasValue)
        {
            for (var due = noteDate.AddDays(ProgressNoteDayInterval); due <= endDate.Value; due = due.AddDays(ProgressNoteDayInterval))
            {
                dueDates.Add(due);
            }
        }

        var alignedDate = AlignToVisit(dueDates.LastOrDefault(), request.ScheduledVisits);

        return new ComputedPlanOfCareV2
        {
            FrequencyDisplay = FormatRange(frequency, "x/week"),
            DurationDisplay = FormatRange(duration, "weeks"),
            StartDate = noteDate,
            EndDate = endDate,
            ProgressNoteDueDates = dueDates,
            ScheduledVisitAlignedProgressNoteDate = alignedDate
        };
    }

    private static DateTime? AlignToVisit(DateTime dueDate, IReadOnlyCollection<DateTime> scheduledVisits)
    {
        if (dueDate == default || scheduledVisits.Count == 0)
        {
            return null;
        }

        var normalizedDueDate = dueDate.Date;
        return scheduledVisits
            .Select(visit => visit.Date)
            .Where(visit => visit <= normalizedDueDate)
            .OrderByDescending(visit => visit)
            .FirstOrDefault();
    }

    private static string FormatRange(IReadOnlyList<int> values, string suffix)
    {
        if (values.Count == 0)
        {
            return string.Empty;
        }

        if (values.Count == 1)
        {
            return suffix == "weeks"
                ? $"{values[0]} week{(values[0] == 1 ? string.Empty : "s")}"
                : $"{values[0]}{suffix}";
        }

        return suffix == "weeks"
            ? $"{values[0]}-{values[^1]} weeks"
            : $"{values[0]}-{values[^1]}{suffix}";
    }
}
