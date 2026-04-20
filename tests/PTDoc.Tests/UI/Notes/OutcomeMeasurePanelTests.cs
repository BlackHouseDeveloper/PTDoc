using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class OutcomeMeasurePanelTests : TestContext
{
    public OutcomeMeasurePanelTests()
    {
        Services.AddSingleton<PTDoc.Application.Outcomes.IOutcomeMeasureRegistry, OutcomeMeasureRegistry>();
    }

    [Fact]
    public void OutcomeMeasurePanel_RendersSuggestedMeasuresSeparatelyFromRecordedScores()
    {
        var cut = RenderComponent<OutcomeMeasurePanel>(parameters => parameters
            .Add(component => component.PatientId, Guid.NewGuid())
            .Add(component => component.SuggestedMeasures, new[] { "LEFS", "KOOS", "VAS/NPRS", "QuickDASH" })
            .Add(component => component.RecordedEntries, new[]
            {
                new OutcomeMeasureEntry
                {
                    Name = "LEFS",
                    Score = "45",
                    Date = new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc)
                }
            }));

        Assert.Contains("Suggested from Intake", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QuickDASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KOOS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VAS/NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll("[data-testid='outcome-history-row']"));
        Assert.Empty(cut.FindAll("[data-testid='outcome-history-legacy-copy']"));
        Assert.Contains("Recorded Scores", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("45.0 points", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutcomeMeasurePanel_FilteredDropdown_UsesSelectableSetWithoutVas()
    {
        var cut = RenderComponent<OutcomeMeasurePanel>(parameters => parameters
            .Add(component => component.PatientId, Guid.NewGuid())
            .Add(component => component.FilterBodyPart, BodyPart.Shoulder));

        var options = cut.FindAll("option").Select(option => option.TextContent.Trim()).ToList();

        Assert.Contains(options, option => option.Contains("DASH", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options, option => option.Contains("QuickDASH", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options, option => option.Contains("VAS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OutcomeMeasurePanel_HistoryStillRendersHistoricalVasRows()
    {
        var cut = RenderComponent<OutcomeMeasurePanel>(parameters => parameters
            .Add(component => component.PatientId, Guid.NewGuid())
            .Add(component => component.RecordedEntries, new[]
            {
                new OutcomeMeasureEntry
                {
                    Name = "VAS",
                    Score = "5",
                    Date = new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc)
                }
        }));

        Assert.Contains("VAS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("5.0 /10", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Legacy measures such as VAS may still appear in recorded history.", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll("[data-testid='outcome-history-legacy-copy']"));
    }
}
