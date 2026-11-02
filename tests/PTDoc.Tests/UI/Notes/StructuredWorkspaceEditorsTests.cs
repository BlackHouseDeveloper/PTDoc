using Bunit;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.ReferenceData;
using Moq;
using PTDoc.Application.Outcomes;
using PTDoc.Infrastructure.Outcomes;
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
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());
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
    public void SubjectiveTab_ContextualFrequencyDeduplicatesProblemsAndUsesUniqueIds()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new SubjectiveVm
        {
            Problems = ["Pain", "A-B", "AB"],
            OtherProblem = "Pain"
        };

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<SubjectiveTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        var frequencySection = cut.Find("[data-testid='subjective-contextual-frequency']");
        var labels = frequencySection.QuerySelectorAll("label").Select(label => label.TextContent.Trim()).ToList();
        var ids = frequencySection.QuerySelectorAll("select").Select(select => select.Id).ToList();

        Assert.Equal(3, labels.Count);
        Assert.Equal(1, labels.Count(label => string.Equals(label, "Pain", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
            Assert.Equal(4, vm.Metrics.Count);
            Assert.All(vm.Metrics, metric => Assert.Equal("Knee", metric.BodyPart));
            Assert.Single(vm.SpecialTests);
            Assert.Single(vm.ExerciseRows);
            Assert.Equal("Knee", vm.Metrics[0].BodyPart);
            Assert.Equal("Heel slides", vm.ExerciseRows[0].SuggestedExercise);
            Assert.True(vm.ExerciseRows[0].IsSourceBacked);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_UnremarkableTogglesClearContradictoryDetails()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString(),
            PalpationComments = "Tender at medial joint line.",
            OtherPostureFinding = "Forward trunk lean.",
            PrimaryGaitPattern = "Antalgic",
            AdditionalGaitObservations = "Limited stance time."
        };
        vm.TenderMuscles.Add("Quadriceps");
        vm.PostureFindings.Add("Forward trunk lean");
        vm.GaitDeviations.Add("Decreased stance time");

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                TenderMuscleOptions = ["Quadriceps"]
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

        cut.Find("[data-testid='objective-tender-muscles-section'] input[type='checkbox']").Change(true);
        cut.Find("[data-testid='objective-posture-section'] input[type='checkbox']").Change(true);
        cut.Find("[data-testid='gait-analysis-section'] input[type='checkbox']").Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.True(vm.IsPalpationUnremarkable);
            Assert.Empty(vm.TenderMuscles);
            Assert.Null(vm.PalpationComments);

            Assert.True(vm.IsPostureUnremarkable);
            Assert.Empty(vm.PostureFindings);
            Assert.Null(vm.OtherPostureFinding);

            Assert.True(vm.IsGaitUnremarkable);
            Assert.Null(vm.PrimaryGaitPattern);
            Assert.Empty(vm.GaitDeviations);
            Assert.Null(vm.AdditionalGaitObservations);
        });

        var palpationSection = cut.Find("[data-testid='objective-tender-muscles-section']");
        Assert.All(
            palpationSection.QuerySelectorAll("input[type='checkbox']").Skip(1),
            input => Assert.NotNull(input.GetAttribute("disabled")));
        Assert.NotNull(palpationSection.QuerySelector("textarea")?.GetAttribute("disabled"));

        var postureSection = cut.Find("[data-testid='objective-posture-section']");
        Assert.All(
            postureSection.QuerySelectorAll("input[type='checkbox']").Skip(1),
            input => Assert.NotNull(input.GetAttribute("disabled")));
        Assert.NotNull(postureSection.QuerySelector("textarea")?.GetAttribute("disabled"));

        var gaitSection = cut.Find("[data-testid='gait-analysis-section']");
        Assert.All(
            gaitSection.QuerySelectorAll("input[type='radio'], input[type='checkbox']").Skip(1),
            input => Assert.NotNull(input.GetAttribute("disabled")));
        Assert.NotNull(gaitSection.QuerySelector("textarea")?.GetAttribute("disabled"));

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_RendersExistingCustomPostureFindings()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString()
        };
        vm.PostureFindings.Add("Legacy pelvic shift");

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee
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

        var postureOptions = cut.Find("[data-testid='objective-posture-options']");
        var customFindingInput = postureOptions
            .QuerySelectorAll("label")
            .First(label => label.TextContent.Contains("Legacy pelvic shift", StringComparison.Ordinal))
            .QuerySelector("input");

        Assert.NotNull(customFindingInput);
        Assert.NotNull(customFindingInput.GetAttribute("checked"));

        customFindingInput.Change(false);

        cut.WaitForAssertion(() => Assert.DoesNotContain("Legacy pelvic shift", vm.PostureFindings));
        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_MmtMovementAddsEmptyPairedGradesWithPinnedRegion()
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
                NormalRangeOfMotionOptions = ["Knee Flexion: 0-135 degrees"]
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

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Add MMT: Knee Flexion", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, vm.Metrics.Count);
            Assert.All(vm.Metrics, metric => { Assert.Equal(MetricType.MMT, metric.MetricType); Assert.Equal("Knee", metric.BodyPart); Assert.Empty(metric.Value); });
            Assert.Equal(new[] { "Left", "Right" }, vm.Metrics.Select(metric => metric.Side));
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_RomPinsRegionWhileLegacyMmtRemainsUnclassified()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString(),
            Metrics =
            [
                new ObjectiveMetricRowEntry
                {
                    Name = "Knee flexion",
                    MetricType = MetricType.ROM,
                    BodyPart = "Knee",
                    Value = "120"
                },
                new ObjectiveMetricRowEntry
                {
                    Name = "Knee extension strength",
                    MetricType = MetricType.MMT,
                    BodyPart = null,
                    Value = "4/5"
                }
            ]
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee
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
            var romRow = cut.Find("[data-testid='objective-rom-row']");
            Assert.Contains("Knee", romRow.QuerySelector("legend")!.TextContent);
            Assert.Empty(romRow.QuerySelectorAll("select"));
            var historical = cut.Find("[data-testid='objective-legacy-grades']");
            Assert.Contains("4/5", historical.TextContent);
            Assert.Empty(historical.QuerySelectorAll("input, select"));
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_RomReferenceIsReadOnlyAndCurrentPriorAreEditable()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var outcomeRegistry = new Mock<IOutcomeMeasureRegistry>(MockBehavior.Strict);
        var vm = new ObjectiveVm
        {
            SelectedBodyPart = BodyPart.Knee.ToString(),
            Metrics =
            [
                new ObjectiveMetricRowEntry
                {
                    Name = "Knee flexion",
                    MetricType = MetricType.ROM,
                    Value = "120",
                    NormValue = "135",
                    PreviousValue = "110"
                },
                new ObjectiveMetricRowEntry
                {
                    Name = "Knee extension strength",
                    MetricType = MetricType.MMT,
                    Value = "4/5",
                    NormValue = "5/5",
                    PreviousValue = "3/5"
                }
            ]
        };

        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee
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

        var romRow = cut.Find("[data-testid='objective-rom-row']");
        Assert.Contains("135", romRow.TextContent);
        var romInputs = romRow.QuerySelectorAll("input:not([type='checkbox'])").OfType<IHtmlInputElement>().ToList();
        Assert.Equal(2, romInputs.Count);
        Assert.All(romInputs, input => Assert.Null(input.GetAttribute("disabled")));
        Bunit.InputEventDispatchExtensions.Input(romInputs[0], "115");
        Bunit.InputEventDispatchExtensions.Input(cut.FindAll("[data-testid='objective-rom-row'] input:not([type='checkbox'])")[1], "125");
        cut.WaitForAssertion(() =>
        {
            var rom = Assert.Single(vm.Metrics.Where(m => m.MetricType == MetricType.ROM));
            var mmt = Assert.Single(vm.Metrics.Where(m => m.MetricType == MetricType.MMT));
            Assert.Equal("135", rom.NormValue);
            Assert.Equal("115", rom.PreviousValue);
            Assert.Equal("125", rom.Value);
            Assert.Equal("5/5", mmt.NormValue);
            Assert.Equal("3/5", mmt.PreviousValue);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();


    }
}
