using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class PlanTabTests : TestContext
{
    [Fact]
    public void PlanTab_SearchResultAddsSuggestedModifierSelection()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var vm = new PlanVm();

        workspaceService
            .Setup(service => service.SearchCptAsync(
                "97110",
                8,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CodeLookupEntry
                {
                    Code = "97110",
                    Description = "Therapeutic exercises",
                    Source = "Commonly used CPT codes and modifiers.md",
                    ModifierOptions = ["GP", "KX", "CQ"],
                    SuggestedModifiers = ["GP"],
                    ModifierSource = "Commonly used CPT codes and modifiers.md"
                }
            ]);

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        cut.Find("[data-testid='cpt-search-input']").Input("97110");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Suggested: GP", cut.Markup, StringComparison.Ordinal);
            Assert.Single(cut.FindAll("[data-testid='cpt-search-result']"));
        });

        cut.InvokeAsync(() => cut.Find("[data-testid='cpt-search-result']").Click());

        cut.WaitForAssertion(() =>
        {
            var selected = Assert.Single(vm.SelectedCptCodes);
            Assert.Equal("97110", selected.Code);
            Assert.Equal(["GP"], selected.Modifiers);
            Assert.Equal(["GP"], selected.SuggestedModifiers);
            Assert.Equal(["CQ", "GP", "KX"], selected.ModifierOptions.OrderBy(value => value).ToArray());

            var chips = cut.FindAll("[data-testid='modifier-chip']");
            Assert.Equal(3, chips.Count);
            Assert.Contains("pt-card__modifier-chip--active", chips[0].ClassList);
            Assert.Contains("Commonly used CPT codes and modifiers.md", cut.Markup, StringComparison.Ordinal);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_ShowsBillingAdvisoriesForMissingMinutesAndSuggestedModifiers()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm
            {
                SelectedCptCodes =
                [
                    new CptCodeEntry
                    {
                        Code = "97110",
                        Description = "Therapeutic exercises",
                        Units = 2,
                        ModifierOptions = ["GP", "KX"],
                        SuggestedModifiers = ["GP"]
                    }
                ]
            })
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        Assert.Contains("Timed CPT minutes are still needed for: 97110.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Review suggested modifiers before billing: 97110.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanTab_LoadsCatalogBackedTreatmentFocusesAndInterventions()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var vm = new PlanVm();

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                TreatmentFocusOptions = ["Mobility"],
                TreatmentInterventionOptions = ["Manual therapy"]
            });

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.SelectedBodyPart, BodyPart.Knee.ToString())
            .Add(component => component.IsReadOnly, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mobility", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Manual therapy", cut.Markup, StringComparison.Ordinal);
            cut.Find("[data-testid='plan-treatment-focuses']");
            cut.Find("[data-testid='plan-general-intervention-options']");
        });

        cut.FindAll("button").First(button => button.TextContent.Contains("Mobility", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Manual therapy", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mobility", vm.TreatmentFocuses);
            Assert.Single(vm.GeneralInterventions);
            Assert.Equal("Manual therapy", vm.GeneralInterventions[0].Name);
            Assert.True(vm.GeneralInterventions[0].IsSourceBacked);
            Assert.Contains("Manual therapy", cut.Find("[data-testid='plan-general-interventions']").TextContent, StringComparison.Ordinal);
        });

        workspaceService.VerifyAll();
    }
}
