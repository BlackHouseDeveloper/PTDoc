using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
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
    public void RemoveCptCode_ClearsInterventionRowsReferencingRemovedCode()
    {
        var objective = new ObjectiveVm
        {
            ExerciseRows =
            [
                new ExerciseRowEntry
                {
                    SuggestedExercise = "Heel slides",
                    CptCode = "97110",
                    CptDescription = "Exercise",
                    TimeMinutes = 12
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
                    Units = 1
                }
            ],
            GeneralInterventions =
            [
                new GeneralInterventionEntry
                {
                    Name = "Manual therapy",
                    CptCode = "97110",
                    CptDescription = "Exercise",
                    TimeMinutes = 8
                }
            ]
        };

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false));

        cut.Find("button[aria-label='Remove CPT code 97110']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(plan.SelectedCptCodes);
            Assert.Null(objective.ExerciseRows[0].CptCode);
            Assert.Null(objective.ExerciseRows[0].CptDescription);
            Assert.Null(objective.ExerciseRows[0].TimeMinutes);
            Assert.Null(plan.GeneralInterventions[0].CptCode);
            Assert.Null(plan.GeneralInterventions[0].CptDescription);
            Assert.Null(plan.GeneralInterventions[0].TimeMinutes);
        });
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

    [Fact]
    public void CatalogLoadFailure_ClearsCachedBodyPartSoPreviousBodyPartCanReload()
    {
        var objective = new ObjectiveVm();
        var plan = new PlanVm();
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);

        noteWorkspaceService
            .SetupSequence(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                TreatmentInterventionOptions = ["Manual therapy"]
            })
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                TreatmentInterventionOptions = ["Manual therapy"]
            });
        noteWorkspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Shoulder, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Catalog unavailable."));

        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<EvaluationInterventionsSection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create<ObjectiveVm>(this, value => objective = value))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, value => plan = value))
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.SelectedBodyPart, BodyPart.Knee.ToString()));

        cut.WaitForAssertion(() => Assert.Contains("Manual therapy", cut.Markup, StringComparison.Ordinal));

        cut.SetParametersAndRender(parameters => parameters.Add(component => component.SelectedBodyPart, BodyPart.Shoulder.ToString()));

        cut.WaitForAssertion(() => Assert.Contains("Unable to load the structured intervention catalog right now.", cut.Markup, StringComparison.Ordinal));

        cut.SetParametersAndRender(parameters => parameters.Add(component => component.SelectedBodyPart, BodyPart.Knee.ToString()));

        cut.WaitForAssertion(() => Assert.Contains("Manual therapy", cut.Markup, StringComparison.Ordinal));
        noteWorkspaceService.Verify(
            service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
