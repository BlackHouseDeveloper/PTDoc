using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailyTreatmentPlanSectionTests : TestContext
{
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
}
