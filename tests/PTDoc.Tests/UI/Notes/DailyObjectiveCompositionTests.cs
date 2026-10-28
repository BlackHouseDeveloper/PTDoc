using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Outcomes;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailyObjectiveCompositionTests : TestContext
{
    public DailyObjectiveCompositionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddSingleton(Mock.Of<INoteWorkspaceService>());
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public void Outcomes_OnlyHistoricalResultsRenderForDaily_AndOtherNoteTypesKeepAuthoring(bool allow, bool historical, bool visible)
    {
        var vm = new ObjectiveVm();
        if (historical) vm.OutcomeMeasures.Add(new() { Name = "LEFS", Score = "44", Date = new DateTime(2026, 1, 1) });
        var cut = RenderComponent<ObjectiveTab>(p => p.Add(c => c.Vm, vm).Add(c => c.AllowOutcomeAuthoring, allow));
        Assert.Equal(visible ? 1 : 0, cut.FindAll("[data-testid='objective-outcome-measures-section']").Count);
        if (historical && !allow)
        {
            Assert.Contains("44", cut.Find("[data-testid='objective-outcome-measures-section']").TextContent);
            Assert.True(cut.FindComponent<PTDoc.UI.Components.Notes.Workspace.OutcomeMeasurePanel>().Instance.IsReadOnly);
        }
    }
}
