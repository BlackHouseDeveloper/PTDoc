using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.Evaluation;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class EvaluationInterventionsSectionTests : TestContext
{
    public EvaluationInterventionsSectionTests()
    {
        Services.AddLogging();
        Services.AddSingleton(Mock.Of<INoteWorkspaceService>());
    }

    [Fact]
    public void QuickPickCptTiles_DisplayDefaultUnitsAsUnits()
    {
        var objective = new ObjectiveVm();
        var plan = new PlanVm();

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false));

        var tileText = cut.Find("[data-testid='evaluation-cpt-quick-pick']").TextContent;

        Assert.Contains("2 units", tileText, StringComparison.Ordinal);
        Assert.DoesNotContain("2 min", tileText, StringComparison.Ordinal);
    }
}
