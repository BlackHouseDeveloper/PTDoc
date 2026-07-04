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

    [Fact]
    public void EditableIconButtons_ExposeDescriptiveAccessibleLabels()
    {
        var objective = new ObjectiveVm
        {
            ExerciseRows =
            [
                new ExerciseRowEntry
                {
                    SuggestedExercise = "Heel slides"
                }
            ]
        };
        var plan = new PlanVm
        {
            SelectedCptCodes =
            [
                new CptCodeEntry
                {
                    Code = "97110",
                    Description = "Therapeutic exercise",
                    Units = 2
                }
            ],
            GeneralInterventions =
            [
                new GeneralInterventionEntry
                {
                    Name = "Manual therapy"
                }
            ]
        };

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false));

        Assert.NotEmpty(cut.FindAll("button[aria-label='Remove CPT code 97110']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Decrease units']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Increase units']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Remove intervention row']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label='Remove general intervention']"));
    }

    [Fact]
    public void BlankCptSearchClearsResultsWithoutCallingLookup()
    {
        var objective = new ObjectiveVm();
        var plan = new PlanVm();
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);

        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false));

        cut.Find("[data-testid='evaluation-cpt-search']").Input(" ");

        Assert.Empty(cut.FindAll("[data-testid='evaluation-cpt-search-result']"));
        Assert.DoesNotContain("Searching CPT codes", cut.Markup, StringComparison.Ordinal);
        noteWorkspaceService.Verify(
            service => service.SearchCptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void LinkedHepActivities_DoNotRenderLeadingSeparatorForBlankExerciseTitles()
    {
        var objective = new ObjectiveVm
        {
            ExerciseRows =
            [
                new ExerciseRowEntry
                {
                    IncludeInHomeExerciseProgram = true,
                    SuggestedExercise = " ",
                    ActualExercisePerformed = " ",
                    SetsRepsDuration = "3x10",
                    CptCode = "97110"
                }
            ]
        };
        var plan = new PlanVm();

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false));

        var linkedHepText = cut.Find("[data-testid='evaluation-hep-card']").TextContent;

        Assert.Contains("Linked HEP activities: 3x10 - CPT 97110", linkedHepText, StringComparison.Ordinal);
        Assert.DoesNotContain("Linked HEP activities:  - ", linkedHepText, StringComparison.Ordinal);
    }
}
