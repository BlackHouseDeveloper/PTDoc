using Bunit;
using Microsoft.Extensions.DependencyInjection;
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
            .Add(component => component.SuggestedMeasures, new[] { "LEFS", "KOOS" })
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
        Assert.Contains("KOOS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Single(cut.FindAll("[data-testid='outcome-history-row']"));
        Assert.Contains("Recorded Scores", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("45.0 points", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
