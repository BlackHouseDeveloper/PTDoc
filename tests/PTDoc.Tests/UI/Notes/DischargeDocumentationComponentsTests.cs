using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Components.Notes.Workspace.DischargeNote;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DischargeDocumentationComponentsTests : TestContext
{
    public DischargeDocumentationComponentsTests()
    {
        Services.AddLogging();
        Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton(Mock.Of<IAiClinicalGenerationService>());
        Services.AddSingleton(Mock.Of<INoteWorkspaceService>());
    }

    [Fact]
    public void DischargeSubjectiveSection_CapturesDischargeSpecificPromptsAndReason()
    {
        var subjective = new SubjectiveVm();
        var dischargeSubjective = new DischargeSubjectiveVm();
        var plan = new PlanVm();

        var cut = RenderComponent<DischargeSubjectiveSection>(parameters => parameters
            .Add(component => component.Vm, subjective)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => subjective = updated))
            .Add(component => component.DischargeSubjective, dischargeSubjective)
            .Add(component => component.DischargeSubjectiveChanged, EventCallback.Factory.Create<DischargeSubjectiveVm>(this, updated => dischargeSubjective = updated))
            .Add(component => component.Plan, plan)
            .Add(component => component.PlanChanged, EventCallback.Factory.Create<PlanVm>(this, updated => plan = updated))
            .Add(component => component.IsReadOnly, false));

        Assert.Contains("Discharge Subjective", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Which goals were met or partially met?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("What remaining difficulty does the patient report?", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Changes Since Last Visit", cut.Markup, StringComparison.OrdinalIgnoreCase);

        cut.Find("#discharge-current-pain").Input("2");
        cut.Find("#discharge-percent-improved").Input("85");
        cut.Find("#discharge-goals-met").Input("Independent with home program and stairs goal met.");
        cut.Find("#discharge-remaining-difficulty").Input("Mild difficulty with prolonged kneeling.");
        cut.Find("#discharge-patient-outcome").Input("Patient reports confidence with independent self-management.");
        cut.Find("#discharge-reason").Change("Other");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, subjective.CurrentPainScore);
            Assert.True(subjective.IsPainScoreDocumented);
            Assert.Equal(85, dischargeSubjective.PercentImproved);
            Assert.Equal("Independent with home program and stairs goal met.", dischargeSubjective.GoalsMetStatus);
            Assert.Equal("Mild difficulty with prolonged kneeling.", dischargeSubjective.RemainingDifficulty);
            Assert.Equal("Patient reports confidence with independent self-management.", dischargeSubjective.PatientReportedOutcome);
            Assert.Equal("Other", plan.PrimaryDischargeReason);
            Assert.Contains("Other discharge reason explanation", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DischargePlanSection_RendersReasonFirstAndUsesLinkedHepItems()
    {
        var plan = new PlanVm
        {
            PrimaryDischargeReason = "Reached goals",
            CompletedDischargeChecklistItems =
            [
                "All goals reviewed and final status documented",
                "Home Exercise Program provided to patient"
            ]
        };

        var cut = RenderComponent<DischargePlanSection>(parameters => parameters
            .Add(component => component.Vm, plan)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => plan = updated))
            .Add(component => component.LinkedHepItems, new[] { "Bridge progression" })
            .Add(component => component.IsReadOnly, false));

        var reasonCard = cut.Find("[data-testid='discharge-recommendations-card']");
        Assert.Contains("Primary Discharge Reason", reasonCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Completed plan of care", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Authorization ended", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("1 Exercise", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Bridge progression", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("2/5 Complete", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("0/8 Complete", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DischargePlanSection_NonBillableModeMarksPlanAndShowsCallout()
    {
        var plan = new PlanVm();

        var cut = RenderComponent<DischargePlanSection>(parameters => parameters
            .Add(component => component.Vm, plan)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => plan = updated))
            .Add(component => component.LinkedHepItems, Array.Empty<string>())
            .Add(component => component.IsReadOnly, false));

        cut.Find("#discharge-documentation-mode").Change("Patient self-discharge");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Patient self-discharge", plan.DischargeDocumentationMode);
            Assert.True(plan.IsNonBillableDischarge);
            Assert.Contains("Non-billable discharge", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Clinical subjective, objective, assessment, and plan documentation remains available.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DischargePlanSection_PreservesExplicitNonBillableFlag_WhenModeIsStandard()
    {
        var plan = new PlanVm
        {
            DischargeDocumentationMode = DischargeDocumentationOptions.StandardBillableMode,
            IsNonBillableDischarge = true
        };

        var cut = RenderComponent<DischargePlanSection>(parameters => parameters
            .Add(component => component.Vm, plan)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => plan = updated))
            .Add(component => component.LinkedHepItems, Array.Empty<string>())
            .Add(component => component.IsReadOnly, false));

        Assert.True(plan.IsNonBillableDischarge);
        Assert.Contains("Non-billable discharge", cut.Markup, StringComparison.Ordinal);

        cut.Find("#discharge-recommendations").Input("Continue HEP independently.");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(DischargeDocumentationOptions.StandardBillableMode, plan.DischargeDocumentationMode);
            Assert.True(plan.IsNonBillableDischarge);
            Assert.Equal("Continue HEP independently.", plan.DischargeRecommendations);
        });
    }

    [Fact]
    public void DischargePlanSection_DocumentationModeChangeCanReturnPlanToBillable()
    {
        var plan = new PlanVm();

        var cut = RenderComponent<DischargePlanSection>(parameters => parameters
            .Add(component => component.Vm, plan)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => plan = updated))
            .Add(component => component.LinkedHepItems, Array.Empty<string>())
            .Add(component => component.IsReadOnly, false));

        cut.Find("#discharge-documentation-mode").Change(DischargeDocumentationOptions.PatientSelfDischargeMode);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(DischargeDocumentationOptions.PatientSelfDischargeMode, plan.DischargeDocumentationMode);
            Assert.True(plan.IsNonBillableDischarge);
            Assert.Contains("Non-billable discharge", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("#discharge-documentation-mode").Change(DischargeDocumentationOptions.StandardBillableMode);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(DischargeDocumentationOptions.StandardBillableMode, plan.DischargeDocumentationMode);
            Assert.False(plan.IsNonBillableDischarge);
            Assert.DoesNotContain("Non-billable discharge", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DischargeSummaryCards_RenderBoundDataInsteadOfHardCodedExamples()
    {
        var outcomes = RenderComponent<FinalOutcomesSummaryCard>(parameters => parameters
            .Add(component => component.Subjective, new SubjectiveVm
            {
                CurrentPainScore = 1,
                BestPainScore = 0,
                WorstPainScore = 3,
                IsPainScoreDocumented = true
            })
            .Add(component => component.DischargeSubjective, new DischargeSubjectiveVm
            {
                PercentImproved = 90,
                PatientReportedOutcome = "Returned to walking program.",
                RemainingDifficulty = "Mild soreness after long hikes."
            })
            .Add(component => component.Objective, new ObjectiveVm
            {
                OutcomeMeasures =
                [
                    new OutcomeMeasureEntry
                    {
                        Name = "LEFS",
                        Score = "72/80",
                        Date = new DateTime(2026, 6, 1)
                    }
                ]
            }));

        Assert.Contains("90% Improved", outcomes.Markup, StringComparison.Ordinal);
        Assert.Contains("LEFS", outcomes.Markup, StringComparison.Ordinal);
        Assert.Contains("Returned to walking program.", outcomes.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Knee Flexion ROM", outcomes.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Oswestry Disability Index", outcomes.Markup, StringComparison.Ordinal);

        var goals = RenderComponent<GoalAchievementSummaryCard>(parameters => parameters
            .Add(component => component.Goals, new[]
            {
                new SmartGoalEntry
                {
                    Description = "Return to independent stair negotiation",
                    Category = "Mobility",
                    Timeframe = GoalTimeframe.LongTerm,
                    Status = GoalStatus.Met
                }
            }));

        Assert.Contains("100% Achieved", goals.Markup, StringComparison.Ordinal);
        Assert.Contains("Return to independent stair negotiation", goals.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Increase knee flexion ROM to 120", goals.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AssessmentWorkspaceSection_HidesMotivationAndSupportWhenConfiguredForDischarge()
    {
        var vm = new AssessmentWorkspaceVm();

        var cut = RenderComponent<AssessmentWorkspaceSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.ShowSmartGoals, false)
            .Add(component => component.ShowMotivationAndSupport, false));

        Assert.Empty(cut.FindAll("[data-testid='motivation-card']"));
        Assert.Empty(cut.FindAll("[data-testid='support-system-card']"));
        Assert.Contains("Prognosis", cut.Markup, StringComparison.Ordinal);
    }
}
