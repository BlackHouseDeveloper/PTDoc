using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.Evaluation;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class InterventionLibrarySectionTests : TestContext
{
    public InterventionLibrarySectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void EmptyState_UsesDocumentedTitlesCountsAndCopy()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        Assert.Contains("Exercises", cut.Find("#intervention-exercises-title").TextContent, StringComparison.Ordinal);
        Assert.Equal("0 exercises", cut.Find("[data-testid='exercise-count']").TextContent.Trim());
        Assert.Equal("No exercises added yet. Click \"Add Exercise\" to get started.", cut.Find("[data-testid='exercise-empty-state']").TextContent.Trim());
        Assert.Equal("Manual Work Techniques", cut.Find("#intervention-techniques-title").TextContent.Trim());
        Assert.Equal("0 techniques", cut.Find("[data-testid='technique-count']").TextContent.Trim());
        Assert.Equal("No manual techniques added yet. Click \"Add Technique\" to get started.", cut.Find("[data-testid='technique-empty-state']").TextContent.Trim());
    }

    [Fact]
    public void ExerciseDialog_DefaultAndFilteredLibraryMatchDocumentedStates()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-exercise']").Click();

        Assert.Equal("Add Therapeutic Exercise", cut.Find("#intervention-dialog-title").TextContent.Trim());
        Assert.Equal("Select from library or add custom exercise", cut.Find("#intervention-dialog-description").TextContent.Trim());
        Assert.Equal(6, cut.FindAll("[data-testid='exercise-library-result']").Count);
        Assert.Equal("true", cut.FindAll(".intervention-region-filter").Single(button => button.TextContent.Trim() == "All").GetAttribute("aria-pressed"));

        cut.Find("[data-testid='exercise-search']").Input("Scap");
        cut.FindAll(".intervention-region-filter").Single(button => button.TextContent.Trim() == "Shoulder").Click();

        var result = Assert.Single(cut.FindAll("[data-testid='exercise-library-result']"));
        Assert.Contains("Scapular Retraction", result.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", cut.FindAll(".intervention-region-filter").Single(button => button.TextContent.Trim() == "Shoulder").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void CustomExerciseTab_AddsVisiblePrescriptionCardThroughExistingCollection()
    {
        var objective = new ObjectiveVm();
        var callbackCount = 0;
        var cut = Render(objective, new PlanVm(), onObjectiveChanged: _ => callbackCount++);

        cut.Find("[data-testid='add-exercise']").Click();
        cut.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == "Custom Exercise").Click();

        Assert.NotNull(cut.Find("input[placeholder='e.g., Resistance Band Exercises']"));
        Assert.NotNull(cut.Find("input[placeholder='Special instructions or modifications...']"));
        cut.Find("input[placeholder='e.g., Resistance Band Exercises']").Input("Wall slides");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Add Custom Exercise", StringComparison.Ordinal)).Click();

        Assert.Single(objective.ExerciseRows);
        Assert.Equal("Wall slides", objective.ExerciseRows[0].ActualExercisePerformed);
        Assert.Equal(1, callbackCount);
        Assert.Contains("Wall slides", cut.Find("[data-testid='exercise-prescription-card']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void PopulatedExerciseCard_SupportsPrescriptionCollapseDuplicateRemoveAndHep()
    {
        var objective = new ObjectiveVm
        {
            ExerciseRows =
            [
                new ExerciseRowEntry
                {
                    SuggestedExercise = "Pendulum Exercise",
                    ActualExercisePerformed = "Pendulum Exercise",
                    IsSourceBacked = true
                }
            ]
        };
        var cut = Render(objective, new PlanVm());

        var card = cut.Find("[data-testid='exercise-prescription-card']");
        Assert.Contains("Range of Motion", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("Shoulder", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("3", card.QuerySelector("input[aria-label^='Sets']")?.GetAttribute("value"));
        Assert.Equal("10", card.QuerySelector("input[aria-label^='Reps']")?.GetAttribute("value"));
        Assert.Equal("3x/week", card.QuerySelector("input[aria-label^='Frequency']")?.GetAttribute("value"));

        cut.Find("button[aria-label='Include Pendulum Exercise in Home Exercise Program']").Click();
        Assert.True(objective.ExerciseRows[0].IncludeInHomeExerciseProgram);

        cut.Find("button[aria-label='Collapse Pendulum Exercise']").Click();
        Assert.Empty(cut.FindAll("[data-testid='exercise-prescription-fields']"));

        cut.Find("button[aria-label='Duplicate Pendulum Exercise']").Click();
        Assert.Equal(2, objective.ExerciseRows.Count);
        Assert.Equal("2 exercises", cut.Find("[data-testid='exercise-count']").TextContent.Trim());

        cut.FindAll("button[aria-label='Remove Pendulum Exercise']").First().Click();
        Assert.Single(objective.ExerciseRows);
    }

    [Fact]
    public void ManualTechniqueDialog_FiltersShoulderAndAddsViaExistingPlanCollection()
    {
        var plan = new PlanVm();
        var callbackCount = 0;
        var cut = Render(new ObjectiveVm(), plan, onPlanChanged: _ => callbackCount++);

        cut.Find("[data-testid='add-technique']").Click();

        Assert.Equal("Add Manual Technique", cut.Find("#intervention-dialog-title").TextContent.Trim());
        Assert.Equal("Select from library or add custom technique", cut.Find("#intervention-dialog-description").TextContent.Trim());
        Assert.Equal(7, cut.FindAll("[data-testid='technique-library-result']").Count);

        cut.FindAll(".intervention-region-filter").Single(button => button.TextContent.Trim() == "Shoulder").Click();

        Assert.Equal(5, cut.FindAll("[data-testid='technique-library-result']").Count);
        cut.Find("[data-testid='technique-library-result'] button").Click();

        Assert.Single(plan.GeneralInterventions);
        Assert.Equal("Manual Work Technique", plan.GeneralInterventions[0].Category);
        Assert.Equal("1 technique", cut.Find("[data-testid='technique-count']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='technique-empty-state']"));
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public void CustomTechniqueTab_DoesNotInventUnsupportedControls()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-technique']").Click();
        cut.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == "Custom Technique").Click();

        Assert.NotNull(cut.Find("[data-testid='custom-technique-panel']"));
        Assert.Empty(cut.FindAll("[data-testid='custom-technique-panel'] input"));
        Assert.Empty(cut.FindAll("[data-testid='custom-technique-panel'] button"));
    }

    private IRenderedComponent<InterventionLibrarySection> Render(
        ObjectiveVm objective,
        PlanVm plan,
        Action<ObjectiveVm>? onObjectiveChanged = null,
        Action<PlanVm>? onPlanChanged = null) =>
        RenderComponent<InterventionLibrarySection>(parameters => parameters
            .Add(component => component.Objective, objective)
            .Add(component => component.ObjectiveChanged, EventCallback.Factory.Create(this, onObjectiveChanged ?? (_ => { })))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create(this, onPlanChanged ?? (_ => { })))
            .Add(component => component.SelectedBodyPart, "Shoulder")
            .Add(component => component.IsReadOnly, false));
}
