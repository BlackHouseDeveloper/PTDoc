using PTDoc.Application.Notes.Workspace;
using PTDoc.Infrastructure.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "Unit")]
public sealed class PlanOfCareCalculatorTests
{
    private readonly IPlanOfCareCalculator _calculator = new PlanOfCareCalculator();

    [Fact]
    public void Compute_NormalizesRangesAndBuildsDueDates()
    {
        var result = _calculator.Compute(new PlanOfCareComputationRequest
        {
            NoteDate = new DateTime(2026, 3, 30),
            FrequencyDaysPerWeek = new[] { 3, 2, 3 },
            DurationWeeks = new[] { 8, 6, 8 }
        });

        Assert.Equal("2-3x/week", result.FrequencyDisplay);
        Assert.Equal("6-8 weeks", result.DurationDisplay);
        Assert.Equal(new DateTime(2026, 3, 30), result.StartDate);
        Assert.Equal(new DateTime(2026, 5, 24), result.EndDate);
        Assert.Equal(new[] { new DateTime(2026, 4, 29) }, result.ProgressNoteDueDates);
    }

    [Fact]
    public void Compute_AlignsLastDueDateToNearestPriorVisit()
    {
        var result = _calculator.Compute(new PlanOfCareComputationRequest
        {
            NoteDate = new DateTime(2026, 3, 30),
            FrequencyDaysPerWeek = new[] { 2 },
            DurationWeeks = new[] { 8 },
            ScheduledVisits = new[]
            {
                new DateTime(2026, 4, 27),
                new DateTime(2026, 4, 28),
                new DateTime(2026, 5, 2)
            }
        });

        Assert.Equal(new DateTime(2026, 4, 28), result.ScheduledVisitAlignedProgressNoteDate);
    }
}
