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
    private const string CptSource = "docs/clinicrefdata/Commonly used CPT codes and modifiers.md";

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
                    Source = CptSource,
                    ModifierOptions = ["GP", "KX", "CQ"],
                    SuggestedModifiers = ["GP"],
                    ModifierSource = CptSource
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
            Assert.Contains(CptSource, cut.Markup, StringComparison.Ordinal);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_RendersQuickPicksWithoutCustomizeButton()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm())
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        Assert.Empty(cut.FindAll("[data-testid='customize-cpt-btn']"));
        Assert.Contains("Quick-pick CPT shortcuts", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(6, cut.FindAll("[data-testid='cpt-quick-pick']").Count);
    }

    [Fact]
    public async Task PlanTab_QuickPickToggle_RemovesExistingEntryUsingNormalizedCodeMatch()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var vm = new PlanVm
        {
            SelectedCptCodes =
            [
                new CptCodeEntry
                {
                    Code = " 97140 ",
                    Description = "Legacy manual therapy",
                    Units = 2
                }
            ]
        };

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        var quickPick = cut.FindAll("[data-testid='cpt-quick-pick']")
            .First(button => button.TextContent.Contains("97140", StringComparison.Ordinal));

        Assert.Contains("pt-card__cpt-tile--active", quickPick.ClassList);
        Assert.Equal("true", quickPick.GetAttribute("aria-pressed"));

        await cut.InvokeAsync(() => quickPick.Click());

        cut.WaitForAssertion(() => Assert.Empty(vm.SelectedCptCodes));
        workspaceService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlanTab_SearchResultNormalizesCodeBeforeApplyingTimedDefaultUnits()
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
                    Code = " 97110 ",
                    Description = "Therapeutic exercises",
                    Source = CptSource,
                    ModifierOptions = ["GP", "KX", "CQ"],
                    SuggestedModifiers = ["GP"],
                    ModifierSource = CptSource
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
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='cpt-search-result']")));

        await cut.InvokeAsync(() => cut.Find("[data-testid='cpt-search-result']").Click());

        cut.WaitForAssertion(() =>
        {
            var selected = Assert.Single(vm.SelectedCptCodes);
            Assert.Equal("97110", selected.Code);
            Assert.Equal(2, selected.Units);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public async Task PlanTab_QuickPicksUseLookupBackedModifierMetadata()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var vm = new PlanVm();
        var quickPickCodes = new[] { "97140", "97110", "97112", "97530", "97116", "97535" };

        foreach (var code in quickPickCodes)
        {
            workspaceService
                .Setup(service => service.SearchCptAsync(code, 5, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new CodeLookupEntry
                    {
                        Code = code,
                        Description = $"Lookup description for {code}",
                        Source = CptSource,
                        ModifierOptions = ["GP", "KX", "CQ"],
                        SuggestedModifiers = ["GP"],
                        ModifierSource = CptSource
                    }
                ]);
        }

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        for (var index = 0; index < quickPickCodes.Length; index++)
        {
            var button = cut.FindAll("[data-testid='cpt-quick-pick']")[index];
            await cut.InvokeAsync(() => button.Click());
        }

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(
                quickPickCodes.OrderBy(value => value).ToArray(),
                vm.SelectedCptCodes.Select(code => code.Code).OrderBy(value => value).ToArray());
            Assert.All(vm.SelectedCptCodes, entry =>
            {
                Assert.Equal(["GP"], entry.Modifiers);
                Assert.Equal(["GP"], entry.SuggestedModifiers);
                Assert.Equal(["CQ", "GP", "KX"], entry.ModifierOptions.OrderBy(value => value).ToArray());
                Assert.Equal(CptSource, entry.ModifierSource);
            });
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

    [Fact]
    public void PlanTab_WhenNoteIsUnsaved_DisablesAllAiButtonsAndShowsSaveFirstReason()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm())
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.Empty)
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.DiagnosisSummary, "Lumbar strain"));

        foreach (var testId in new[]
        {
            "ai-suggest-discharge-btn",
            "ai-suggest-followup-btn",
            "generate-summary-btn"
        })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("Save the note before generating AI plan content.", cut.Find($"[data-testid='{testId}-disabled-reason']").TextContent.Trim());
        }

        aiService.VerifyNoOtherCalls();
    }

    [Fact]
    public void PlanTab_WhenReadOnlyForNonSignedReason_HidesAiButtons()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm())
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, true)
            .Add(component => component.IsNoteSigned, false)
            .Add(component => component.DiagnosisSummary, "Lumbar strain"));

        foreach (var testId in new[]
        {
            "ai-suggest-discharge-btn",
            "ai-suggest-followup-btn",
            "generate-summary-btn"
        })
        {
            Assert.Empty(cut.FindAll($"[data-testid='{testId}']"));
            Assert.Empty(cut.FindAll($"[data-testid='{testId}-disabled-reason']"));
        }

        aiService.VerifyNoOtherCalls();
    }

    [Fact]
    public void PlanTab_WhenSignedEvenIfNotReadOnly_ShowsSignedDisabledReason()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm())
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.IsNoteSigned, true)
            .Add(component => component.DiagnosisSummary, "Lumbar strain"));

        foreach (var testId in new[]
        {
            "ai-suggest-discharge-btn",
            "ai-suggest-followup-btn",
            "generate-summary-btn"
        })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("AI generation is not available on signed notes.", cut.Find($"[data-testid='{testId}-disabled-reason']").TextContent.Trim());
        }

        aiService.VerifyNoOtherCalls();
    }

    [Fact]
    public void PlanTab_DischargeSuggestionAfterManualEdit_RequiresExplicitAcceptance()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessfulPlanResult(noteId, "AI discharge guidance"));

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='discharge-planning-box-textarea']").Input("Manual discharge plan");

        cut.WaitForAssertion(() => Assert.Equal("Manual discharge plan", vm.DischargePlanningNotes));

        cut.Find("[data-testid='ai-suggest-discharge-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='discharge-planning-box-review-banner']");
            Assert.Equal("Manual discharge plan", vm.DischargePlanningNotes);
            Assert.Equal("AI discharge guidance", cut.Find("[data-testid='discharge-planning-box-textarea']").GetAttribute("value"));
        });

        cut.Find("[data-testid='discharge-planning-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Manual discharge plan", vm.DischargePlanningNotes);
            Assert.Equal("Manual discharge plan", cut.Find("[data-testid='discharge-planning-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='discharge-planning-box-review-banner']"));
        });

        cut.Find("[data-testid='ai-suggest-discharge-btn']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='discharge-planning-box-review-banner']"));

        cut.Find("[data-testid='discharge-planning-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("AI discharge guidance", vm.DischargePlanningNotes);
            cut.Find("[data-testid='discharge-planning-box-accepted-note']");
        });

        aiService.Verify(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void PlanTab_FollowUpSuggestionAfterManualEdit_RequiresExplicitAcceptance()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessfulPlanResult(noteId, "AI follow-up instructions"));

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='followup-instructions-box-textarea']").Input("Manual follow-up");

        cut.WaitForAssertion(() => Assert.Equal("Manual follow-up", vm.FollowUpInstructions));

        cut.Find("[data-testid='ai-suggest-followup-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='followup-instructions-box-review-banner']");
            Assert.Equal("Manual follow-up", vm.FollowUpInstructions);
            Assert.Equal("AI follow-up instructions", cut.Find("[data-testid='followup-instructions-box-textarea']").GetAttribute("value"));
        });

        cut.Find("[data-testid='followup-instructions-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Manual follow-up", vm.FollowUpInstructions);
            Assert.Equal("Manual follow-up", cut.Find("[data-testid='followup-instructions-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='followup-instructions-box-review-banner']"));
        });

        cut.Find("[data-testid='ai-suggest-followup-btn']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='followup-instructions-box-review-banner']"));

        cut.Find("[data-testid='followup-instructions-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("AI follow-up instructions", vm.FollowUpInstructions);
            cut.Find("[data-testid='followup-instructions-box-accepted-note']");
        });

        aiService.Verify(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void PlanTab_ClinicalSummarySuggestionAfterManualEdit_RequiresExplicitAcceptance()
    {
        var noteId = Guid.NewGuid();
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessfulPlanResult(noteId, "AI summary draft"));

        workspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceAiAcceptanceResult
            {
                Success = true
            });

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='clinical-summary-box-textarea']").Input("Manual summary");

        cut.WaitForAssertion(() => Assert.Equal("Manual summary", vm.ClinicalSummary));

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='clinical-summary-box-review-banner']");
            Assert.Equal("Manual summary", vm.ClinicalSummary);
            Assert.Equal("AI summary draft", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
        });

        cut.Find("[data-testid='clinical-summary-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Manual summary", vm.ClinicalSummary);
            Assert.Equal("Manual summary", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-review-banner']"));
        });

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='clinical-summary-box-review-banner']"));

        cut.Find("[data-testid='clinical-summary-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("AI summary draft", vm.ClinicalSummary);
            cut.Find("[data-testid='clinical-summary-box-accepted-note']");
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_GenerationFailureShowsVisibleErrorMessage()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain"
                },
                ErrorMessage = "AI generation failed. Please try again or contact support. Reference ID: ai-ref-123",
                Success = false
            });

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='ai-suggest-discharge-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI generation failed. Please try again or contact support. Reference ID: ai-ref-123", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='discharge-planning-box-review-banner']"));
        });

        aiService.VerifyAll();
    }

    [Fact]
    public void PlanTab_ClinicalSummaryAcceptFailure_ShowsVisibleErrorAndPreservesVmValue()
    {
        var noteId = Guid.NewGuid();
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var vm = new PlanVm
        {
            ClinicalSummary = "Original summary"
        };

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessfulPlanResult(noteId, "AI summary draft"));

        workspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceAiAcceptanceResult
            {
                Success = false,
                ErrorMessage = "Unable to accept AI-generated summary content."
            });

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='clinical-summary-box-review-banner']");
            Assert.Equal("Original summary", vm.ClinicalSummary);
        });

        cut.Find("[data-testid='clinical-summary-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to accept AI-generated summary content.", cut.Find("[data-testid='clinical-summary-box-error']").TextContent, StringComparison.Ordinal);
            Assert.Equal("Original summary", vm.ClinicalSummary);
            cut.Find("[data-testid='clinical-summary-box-review-banner']");
            cut.Find("[data-testid='clinical-summary-box-actions']");
            Assert.Equal("AI summary draft", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-accepted-note']"));
        });

        cut.Find("[data-testid='clinical-summary-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Original summary", vm.ClinicalSummary);
            Assert.Equal("Original summary", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-review-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-error']"));
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_ClinicalSummaryAcceptException_ShowsVisibleErrorAndKeepsReviewPending()
    {
        var noteId = Guid.NewGuid();
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var vm = new PlanVm
        {
            ClinicalSummary = "Original summary"
        };

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSuccessfulPlanResult(noteId, "AI summary draft"));

        workspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("accept request failed"));

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService);

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='clinical-summary-box-review-banner']"));

        cut.Find("[data-testid='clinical-summary-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to accept AI-generated summary content.", cut.Find("[data-testid='clinical-summary-box-error']").TextContent, StringComparison.Ordinal);
            Assert.Equal("Original summary", vm.ClinicalSummary);
            cut.Find("[data-testid='clinical-summary-box-review-banner']");
            cut.Find("[data-testid='clinical-summary-box-actions']");
            Assert.Equal("AI summary draft", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-accepted-note']"));
        });

        cut.Find("[data-testid='clinical-summary-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Original summary", vm.ClinicalSummary);
            Assert.Equal("Original summary", cut.Find("[data-testid='clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-review-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-error']"));
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }

    private IRenderedComponent<PlanTab> RenderSavedPlanTab(
        PlanVm vm,
        Guid noteId,
        Mock<INoteWorkspaceService> workspaceService,
        Mock<IAiClinicalGenerationService> aiService)
    {
        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        return RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.DiagnosisSummary, "Lumbar strain"));
    }

    private static PlanGenerationResult CreateSuccessfulPlanResult(Guid noteId, string generatedText)
    {
        return new PlanGenerationResult
        {
            GeneratedText = generatedText,
            Confidence = 0.85,
            SourceInputs = new PlanOfCareGenerationRequest
            {
                NoteId = noteId,
                Diagnosis = "Lumbar strain"
            },
            Success = true
        };
    }
}
