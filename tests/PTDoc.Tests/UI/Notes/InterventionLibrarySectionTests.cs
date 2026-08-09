using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.Evaluation;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class InterventionLibrarySectionTests : TestContext
{
    private readonly Mock<INoteWorkspaceService> _workspaceService = new(MockBehavior.Loose);

    public InterventionLibrarySectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        _workspaceService
            .Setup(service => service.GetInterventionLibraryCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCatalog());
        _workspaceService
            .Setup(service => service.SearchCptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CodeLookupEntry { Code = "97140", Description = "Manual therapy techniques" }]);
        Services.AddSingleton(_workspaceService.Object);
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
    public void ExerciseDialog_NoResultsSupportsClearFilters()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-exercise']").Click();
        cut.Find("[data-testid='exercise-search']").Input("no-match");

        Assert.Contains(cut.FindAll("[role='status']"), state => state.TextContent.Contains("No exercises match your search and filters.", StringComparison.Ordinal));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Clear filters").Click();
        Assert.Equal(6, cut.FindAll("[data-testid='exercise-library-result']").Count);
    }

    [Fact]
    public void ExerciseDialog_LoadFailureShowsInlineRetryState()
    {
        _workspaceService
            .SetupSequence(service => service.GetInterventionLibraryCatalogAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"))
            .ReturnsAsync(CreateCatalog());
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-exercise']").Click();

        var alert = cut.Find("[role='alert']");
        Assert.Contains("We couldn’t load the intervention library. Try again.", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("Retry", alert.TextContent, StringComparison.Ordinal);
        alert.QuerySelector("button")!.Click();
        Assert.Equal(6, cut.FindAll("[data-testid='exercise-library-result']").Count);
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
        Assert.NotNull(cut.Find("textarea[placeholder='Special instructions or modifications...']"));
        cut.Find("input[placeholder='e.g., Resistance Band Exercises']").Input("Wall slides");
        cut.Find("textarea[placeholder='Special instructions or modifications...']").Input("Pain-free range only");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Add Custom Exercise", StringComparison.Ordinal)).Click();

        Assert.Single(objective.ExerciseRows);
        Assert.Equal("Wall slides", objective.ExerciseRows[0].ActualExercisePerformed);
        Assert.Equal("Pain-free range only", objective.ExerciseRows[0].Notes);
        Assert.NotNull(objective.ExerciseRows[0].Prescription);
        Assert.False(objective.ExerciseRows[0].IncludeInHomeExerciseProgram);
        Assert.Equal(1, callbackCount);
        Assert.Contains("Wall slides", cut.Find("[data-testid='exercise-prescription-card']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ExerciseDialog_TabsSupportArrowKeyNavigationAndAssociatedPanels()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-exercise']").Click();
        cut.Find("[role='tablist']").KeyDown("ArrowRight");

        var customTab = cut.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == "Custom Exercise");
        Assert.Equal("true", customTab.GetAttribute("aria-selected"));
        Assert.Equal("custom-exercise-panel", customTab.GetAttribute("aria-controls"));
        Assert.NotNull(cut.Find("#custom-exercise-panel[role='tabpanel']"));
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
                    Category = "Range of Motion",
                    InterventionRegion = InterventionRegion.Shoulder,
                    IsSourceBacked = true,
                    Prescription = new ExercisePrescriptionEntry
                    {
                        Sets = 3,
                        Repetitions = 10,
                        Frequency = "3x/week"
                    }
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
    public void ManualTechniqueDialog_NoRegionResultsSupportsShowAll()
    {
        var cut = Render(new ObjectiveVm(), new PlanVm());

        cut.Find("[data-testid='add-technique']").Click();
        cut.FindAll(".intervention-region-filter").Single(button => button.TextContent.Trim() == "Hip").Click();

        Assert.Contains(cut.FindAll("[role='status']"), state => state.TextContent.Contains("No manual techniques match the selected body region.", StringComparison.Ordinal));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Show all techniques").Click();
        Assert.Equal(7, cut.FindAll("[data-testid='technique-library-result']").Count);
    }

    [Fact]
    public void CustomTechniqueTab_ValidatesAndAddsCompleteTechnique()
    {
        var plan = new PlanVm();
        var cut = Render(new ObjectiveVm(), plan);

        cut.Find("[data-testid='add-technique']").Click();
        cut.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == "Custom Technique").Click();

        cut.Find("[data-testid='custom-technique-panel'] button").Click();
        var panel = cut.Find("[data-testid='custom-technique-panel']");
        Assert.Contains("Technique name is required.", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains("Body region is required.", panel.TextContent, StringComparison.Ordinal);

        cut.Find("[data-testid='custom-technique-panel'] input[type='text']").Input("Posterior glide");
        cut.Find("[data-testid='custom-technique-panel'] select").Change(InterventionRegion.Shoulder.ToString());
        cut.Find("[data-testid='custom-technique-panel'] textarea").Input("Grade II");
        cut.Find("[data-testid='custom-technique-panel'] button").Click();

        var technique = Assert.Single(plan.GeneralInterventions);
        Assert.Equal(InterventionKind.ManualTechnique, technique.Kind);
        Assert.Equal(InterventionRegion.Shoulder, technique.InterventionRegion);
        Assert.Equal("Grade II", technique.Notes);
        Assert.False(technique.IsSourceBacked);
    }

    [Fact]
    public void ManualTechniqueCard_SupportsDeepDuplicateCollapseRemoveAndReadOnlyBehavior()
    {
        var plan = new PlanVm
        {
            GeneralInterventions =
            [
                new GeneralInterventionEntry
                {
                    Kind = InterventionKind.ManualTechnique,
                    Name = "Glenohumeral Joint Mobilization",
                    Category = "Manual Work Technique",
                    InterventionRegion = InterventionRegion.Shoulder,
                    Notes = "Grade III",
                    Response = "Improved mobility"
                }
            ]
        };
        var cut = Render(new ObjectiveVm(), plan);

        Assert.Equal("Grade III", cut.Find("[data-testid='manual-technique-card'] textarea").GetAttribute("value"));
        cut.Find("button[aria-label='Collapse Glenohumeral Joint Mobilization']").Click();
        Assert.Empty(cut.FindAll(".technique-detail-grid"));
        cut.Find("button[aria-label='Duplicate Glenohumeral Joint Mobilization']").Click();
        Assert.Equal(2, plan.GeneralInterventions.Count);
        Assert.Equal("Grade III", plan.GeneralInterventions[1].Notes);
        Assert.Equal("Improved mobility", plan.GeneralInterventions[1].Response);
        cut.FindAll("button[aria-label='Remove Glenohumeral Joint Mobilization']").First().Click();
        Assert.Single(plan.GeneralInterventions);

        var readOnly = RenderComponent<InterventionLibrarySection>(parameters => parameters
            .Add(component => component.Objective, new ObjectiveVm())
            .Add(component => component.Plan, plan)
            .Add(component => component.IsReadOnly, true));
        Assert.Empty(readOnly.FindAll("[data-testid='add-technique']"));
        Assert.Empty(readOnly.FindAll("button[aria-label^='Duplicate']"));
        Assert.Empty(readOnly.FindAll("button[aria-label^='Remove']"));
        Assert.All(readOnly.FindAll("textarea, input, select"), field => Assert.True(field.HasAttribute("disabled")));
    }

    private static InterventionLibraryCatalog CreateCatalog() => new()
    {
        Version = "test-v1",
        Provenance = new ReferenceDataProvenance
        {
            DocumentPath = "tests/fixtures/interventions.json",
            Version = "test-v1"
        },
        Items =
        [
            Exercise("exercise-pendulum", "Pendulum Exercise", "Range of Motion", InterventionRegion.Shoulder),
            Exercise("exercise-scapular-retraction", "Scapular Retraction", "Strengthening", InterventionRegion.Shoulder, "scap"),
            Exercise("exercise-cervical-retraction", "Cervical Retraction", "Mobility", InterventionRegion.CervicalSpine),
            Exercise("exercise-heel-slide", "Heel Slide", "Range of Motion", InterventionRegion.Knee),
            Exercise("exercise-ankle-pump", "Ankle Pump", "Mobility", InterventionRegion.AnkleFoot),
            Exercise("exercise-bridge", "Bridge", "Strengthening", InterventionRegion.LumbarSpine),
            Technique("manual-glenohumeral-posterior-glide", "Glenohumeral Posterior Glide", InterventionRegion.Shoulder),
            Technique("manual-glenohumeral-inferior-glide", "Glenohumeral Inferior Glide", InterventionRegion.Shoulder),
            Technique("manual-scapular-mobilization", "Scapular Mobilization", InterventionRegion.Shoulder),
            Technique("manual-pec-minor-release", "Pectoralis Minor Release", InterventionRegion.Shoulder),
            Technique("manual-upper-trapezius-release", "Upper Trapezius Release", InterventionRegion.Shoulder),
            Technique("manual-patellar-mobilization", "Patellar Mobilization", InterventionRegion.Knee),
            Technique("manual-cervical-upglide", "Cervical Upglide", InterventionRegion.CervicalSpine)
        ]
    };

    private static InterventionLibraryItem Exercise(string id, string name, string category, InterventionRegion region, params string[] aliases) => new()
    {
        Id = id,
        Kind = InterventionKind.Exercise,
        Name = name,
        Category = category,
        Region = region,
        SearchAliases = aliases.ToList()
    };

    private static InterventionLibraryItem Technique(string id, string name, InterventionRegion region) => new()
    {
        Id = id,
        Kind = InterventionKind.ManualTechnique,
        Name = name,
        Category = "Manual Work Technique",
        Region = region
    };

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
