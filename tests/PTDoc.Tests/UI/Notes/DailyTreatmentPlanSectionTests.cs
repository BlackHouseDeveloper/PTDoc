using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailyTreatmentPlanSectionTests : TestContext
{
    public DailyTreatmentPlanSectionTests()
    {
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void PlanSection_AddInterventionSeedsEditableEntryThatSurvivesAutosavePruning()
    {
        var vm = new PlanVm();
        var dailyTreatment = new DailyTreatmentVm();

        Services.AddLogging();
        Services.AddSingleton(Mock.Of<IAiClinicalGenerationService>());
        Services.AddSingleton(Mock.Of<INoteWorkspaceService>());

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.DailyTreatment, dailyTreatment)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(
                component => component.DailyTreatmentChanged,
                EventCallback.Factory.Create<DailyTreatmentVm>(this, updated => dailyTreatment = updated))
            .Add(component => component.IsReadOnly, false));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Add Intervention", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var intervention = Assert.Single(vm.GeneralInterventions);
            Assert.Equal("Skilled intervention", intervention.Name);
            Assert.Contains("Skilled intervention", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("No interventions documented yet.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PlanSection_InterventionRowsCaptureCptAssistanceCueingAndHepLink()
    {
        var vm = new PlanVm
        {
            GeneralInterventions =
            [
                new GeneralInterventionEntry
                {
                    Name = "Lateral step down"
                }
            ]
        };
        var dailyTreatment = new DailyTreatmentVm();

        Services.AddLogging();
        Services.AddSingleton(Mock.Of<IAiClinicalGenerationService>());
        Services.AddSingleton(Mock.Of<INoteWorkspaceService>());

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.DailyTreatment, dailyTreatment)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(
                component => component.DailyTreatmentChanged,
                EventCallback.Factory.Create<DailyTreatmentVm>(this, updated => dailyTreatment = updated))
            .Add(component => component.IsReadOnly, false));

        cut.Find("[data-testid='daily-intervention-cpt']").Change("97530");
        cut.Find("[data-testid='daily-intervention-minutes']").Change("10");
        cut.Find("[data-testid='daily-intervention-assistance']").Change("Standby Assist");
        cut.Find("[data-testid='daily-intervention-cueing']").Change("Visual cueing");
        cut.Find("[data-testid='daily-intervention-response']").Input("Better eccentric control");
        cut.Find("[data-testid='daily-intervention-hep-link']").Change(true);

        cut.WaitForAssertion(() =>
        {
            var intervention = Assert.Single(vm.GeneralInterventions);
            Assert.Equal("97530", intervention.CptCode);
            Assert.Equal("Functional activities", intervention.CptDescription);
            Assert.Equal(10, intervention.TimeMinutes);
            Assert.Equal("Standby Assist", intervention.AssistanceLevel);
            Assert.Equal("Visual cueing", intervention.Cueing);
            Assert.Equal("Better eccentric control", intervention.Response);
            Assert.True(intervention.IncludeInHomeExerciseProgram);

            var selected = Assert.Single(vm.SelectedCptCodes);
            Assert.Equal("97530", selected.Code);
        });
    }
}
