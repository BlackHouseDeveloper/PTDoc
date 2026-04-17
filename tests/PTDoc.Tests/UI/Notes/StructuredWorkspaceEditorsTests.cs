using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.ReferenceData;
using Moq;
using PTDoc.Application.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class StructuredWorkspaceEditorsTests : TestContext
{
    public StructuredWorkspaceEditorsTests()
    {
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
    }

    [Fact]
    public void SubjectiveTab_LoadsCatalogBackedFunctionalLimitations()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new SubjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString()
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                FunctionalLimitationCategories =
                [
                    new CatalogCategory
                    {
                        Name = "Mobility",
                        Items = ["Difficulty squatting", "Difficulty with stairs"]
                    }
                ]
            });

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<SubjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        cut.WaitForAssertion(() => Assert.Contains("Difficulty squatting", cut.Markup, StringComparison.Ordinal));

        cut.Find("input[name='functional-limitations']").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.Single(vm.StructuredFunctionalLimitations);
            Assert.Equal("Mobility", vm.StructuredFunctionalLimitations[0].Category);
            Assert.Equal("Difficulty squatting", vm.StructuredFunctionalLimitations[0].Description);
            Assert.Equal(BodyPart.Knee.ToString(), vm.StructuredFunctionalLimitations[0].BodyPart);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void SubjectiveTab_UsesIntakeCatalogsForSupplementalSelections()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new SubjectiveVm();

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<SubjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        Assert.Contains("Lives alone", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Hypertension (High Blood Pressure)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", cut.Markup, StringComparison.Ordinal);

        cut.FindAll("button").First(button => button.TextContent.Contains("Lives alone", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Hypertension (High Blood Pressure)", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Single-Story Home: Bedroom and bathroom on main floor", StringComparison.Ordinal)).Click();

        cut.FindAll("input[name='assistive-device']").First(input => input.GetAttribute("value") == "true").Change(true);
        cut.Find("[data-testid='subjective-assistive-device-search']").Input("Cane");
        cut.FindAll("button").First(button => string.Equals(button.TextContent.Trim(), "Cane", StringComparison.Ordinal)).Click();

        cut.FindAll("input[name='medications']").First(input => input.GetAttribute("value") == "true").Change(true);
        cut.Find("[data-testid='subjective-medication-search']").Input("Zestril");
        cut.FindAll("button").First(button => button.TextContent.Contains("Zestril / Lisinopril", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Lives alone", vm.LivingSituation);
            Assert.Contains("Hypertension (High Blood Pressure)", vm.Comorbidities);
            Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", vm.SelectedHouseLayoutLabels);
            Assert.True(vm.UsesAssistiveDevice);
            Assert.Contains("Cane", vm.SelectedAssistiveDeviceLabels);
            Assert.True(vm.TakingMedications);
            Assert.Contains("Zestril / Lisinopril", vm.SelectedMedicationLabels);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void SubjectiveTab_RendersCanonicalLabels_ForLegacyCatalogIds()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService());
        var vm = mapper.MapToUiPayload(new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Subjective = new WorkspaceSubjectiveV2
            {
                AssistiveDevice = new AssistiveDeviceDetailsV2
                {
                    UsesAssistiveDevice = true,
                    Devices = ["cane"]
                },
                LivingSituation = ["lives-alone"],
                OtherLivingSituation = "single-story-main-floor-bed-bath; Basement laundry",
                Comorbidities = ["hypertension"],
                TakingMedications = true,
                Medications = [new MedicationEntryV2 { Name = "zestril-lisinopril" }]
            }
        }).Subjective;

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<SubjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        Assert.Contains("Cane", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Lives alone", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Hypertension (High Blood Pressure)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Zestril / Lisinopril", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("lives-alone", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("single-story-main-floor-bed-bath", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("zestril-lisinopril", cut.Markup, StringComparison.Ordinal);

        workspaceService.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_LoadsCatalogBackedEditors()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString()
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                NormalRangeOfMotionOptions = ["Knee flexion 0-135"],
                SpecialTestsOptions = ["McMurray"],
                TenderMuscleOptions = ["Quadriceps"],
                ExerciseOptions = ["Heel slides"],
                MmtGradeOptions = ["4/5", "5/5"]
            });

        outcomeRegistry
            .Setup(registry => registry.GetMeasuresForBodyPart(BodyPart.Knee))
            .Returns(Array.Empty<OutcomeMeasureDefinition>());

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(outcomeRegistry.Object);

        var cut = RenderComponent<ObjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<ObjectiveVm>(this, updated => vm = updated))
            .Add(component => component.PatientId, Guid.NewGuid().ToString())
            .Add(component => component.IsReadOnly, false));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Knee flexion 0-135", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("McMurray", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Heel slides", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button").First(button => button.TextContent.Contains("Knee flexion 0-135", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("McMurray", StringComparison.Ordinal)).Click();
        cut.FindAll("button").First(button => button.TextContent.Contains("Heel slides", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(vm.Metrics);
            Assert.Single(vm.SpecialTests);
            Assert.Single(vm.ExerciseRows);
            Assert.Equal("Heel slides", vm.ExerciseRows[0].SuggestedExercise);
            Assert.True(vm.ExerciseRows[0].IsSourceBacked);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }
}
