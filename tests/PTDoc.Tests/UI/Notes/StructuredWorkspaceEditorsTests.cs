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
            Assert.Single(vm.Metrics);
            Assert.Single(vm.SpecialTests);
            Assert.Single(vm.ExerciseRows);
            Assert.Null(vm.Metrics[0].BodyPart);
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

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_BlankMmtRowsDefaultToPrimaryBodyPartFallback()
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

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("+ Add Row", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var metric = Assert.Single(vm.Metrics);

            Assert.Equal(MetricType.MMT, metric.MetricType);
            Assert.Null(metric.BodyPart);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_BodyPartEditorsShowPrimaryFallbackAsBlankSelection()
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
                    BodyPart = null,
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
            var romBodyPartSelect = Assert.IsAssignableFrom<IHtmlSelectElement>(
                cut.Find("[data-testid='objective-rom-row'] select.objective-tab__select"));
            var mmtBodyPartSelect = Assert.IsAssignableFrom<IHtmlSelectElement>(
                cut.Find("[data-testid='objective-mmt-row'] select.objective-tab__select"));

            Assert.Equal(string.Empty, romBodyPartSelect.Value);
            Assert.Equal(string.Empty, mmtBodyPartSelect.Value);
            Assert.Contains("Use primary body part", romBodyPartSelect.TextContent, StringComparison.Ordinal);
            Assert.Contains("Use primary body part", mmtBodyPartSelect.TextContent, StringComparison.Ordinal);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();
    }

    [Fact]
    public void ObjectiveTab_AllowsEditingMetricNormalAndPreviousValuesWhenEditable()
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

        var romInputs = cut.Find("[data-testid='objective-rom-row']")
            .QuerySelectorAll("input.objective-tab__text-input")
            .OfType<IHtmlInputElement>()
            .ToList();
        var mmtInputs = cut.Find("[data-testid='objective-mmt-row']")
            .QuerySelectorAll("input.objective-tab__text-input")
            .OfType<IHtmlInputElement>()
            .ToList();

        Assert.Null(romInputs[1].GetAttribute("disabled"));
        Assert.Null(romInputs[2].GetAttribute("disabled"));
        Assert.Null(mmtInputs[1].GetAttribute("disabled"));
        Assert.Null(mmtInputs[2].GetAttribute("disabled"));

        FindMetricInputs("objective-rom-row")[1].Change("140");
        FindMetricInputs("objective-rom-row")[2].Change("115");
        FindMetricInputs("objective-mmt-row")[1].Change("5-/5");
        FindMetricInputs("objective-mmt-row")[2].Change("4-/5");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("140", vm.Metrics[0].NormValue);
            Assert.Equal("115", vm.Metrics[0].PreviousValue);
            Assert.Equal("5-/5", vm.Metrics[1].NormValue);
            Assert.Equal("4-/5", vm.Metrics[1].PreviousValue);
        });

        workspaceService.VerifyAll();
        outcomeRegistry.VerifyAll();

        List<IHtmlInputElement> FindMetricInputs(string testId) =>
            cut.Find($"[data-testid='{testId}']")
                .QuerySelectorAll("input.objective-tab__text-input")
                .OfType<IHtmlInputElement>()
                .ToList();
    }
}
