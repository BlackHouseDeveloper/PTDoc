using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class PlanTabTests : TestContext
{
    private static readonly DateTime SavedVersion = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private const string CptSource = "docs/clinicrefdata/Commonly used CPT codes and modifiers.md";

    public PlanTabTests()
    {
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void PlanTab_SearchResultAddsSuggestedModifierSelection()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);

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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
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
    public void PlanTab_NormalizesLegacyTimedCodesForMinutesInputAndBillingAdvisories()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);

        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, new PlanVm
            {
                SelectedCptCodes =
                [
                    new CptCodeEntry
                    {
                        Code = " 97110 ",
                        Description = "Legacy therapeutic exercises",
                        Units = 2
                    }
                ]
            })
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, Guid.NewGuid())
            .Add(component => component.IsReadOnly, false));

        Assert.Single(cut.FindAll("[data-testid='cpt-minutes-input']"));
        Assert.Contains("Timed CPT minutes are still needed", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("97110", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlanTab_QuickPicksUseLookupBackedModifierMetadata()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);

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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
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
    public void PlanTab_InterventionRowsCaptureCptAssistanceCueingAndHepLink()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Loose);
        var vm = new PlanVm
        {
            GeneralInterventions =
            [
                new GeneralInterventionEntry
                {
                    Name = "Step ups"
                }
            ]
        };

        workspaceService
            .Setup(service => service.SearchCptAsync("97110", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CodeLookupEntry
                {
                    Code = "97110",
                    Description = "Exercise",
                    Source = CptSource,
                    ModifierOptions = ["GP"],
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

        cut.Find("[data-testid='plan-intervention-cpt']").Change("97110");
        cut.Find("[data-testid='plan-intervention-minutes']").Change("12");
        cut.Find("[data-testid='plan-intervention-assistance']").Change("Min Assist");
        cut.Find("[data-testid='plan-intervention-cueing']").Change("Verbal cueing");
        cut.Find("[data-testid='plan-intervention-response']").Input("Improved form");
        cut.Find("[data-testid='plan-intervention-hep-link']").Change(true);

        cut.WaitForAssertion(() =>
        {
            var intervention = Assert.Single(vm.GeneralInterventions);
            Assert.Equal("97110", intervention.CptCode);
            Assert.Equal("Exercise", intervention.CptDescription);
            Assert.Equal(12, intervention.TimeMinutes);
            Assert.Equal("Min Assist", intervention.AssistanceLevel);
            Assert.Equal("Verbal cueing", intervention.Cueing);
            Assert.Equal("Improved form", intervention.Response);
            Assert.True(intervention.IncludeInHomeExerciseProgram);

            var selected = Assert.Single(vm.SelectedCptCodes);
            Assert.Equal("97110", selected.Code);
            Assert.Contains("Linked HEP activities", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Step ups", cut.Find("[data-testid='plan-hep-linked-interventions']").TextContent, StringComparison.Ordinal);
        });

        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_WhenNoteIsUnsaved_DisablesAllAiButtonsAndShowsSaveFirstReason()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);

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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);

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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);

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
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI discharge guidance"));

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

        aiService.Verify(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void PlanTab_FollowUpSuggestionAfterManualEdit_RequiresExplicitAcceptance()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI follow-up instructions"));

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

        aiService.Verify(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void PlanTab_ClinicalSummarySuggestionAfterManualEdit_RequiresExplicitAcceptance()
    {
        var noteId = Guid.NewGuid();
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI summary draft"));

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
            Assert.Equal("source-hash", vm.AcceptedNarrativeProvenance[nameof(PlanVm.ClinicalSummary)].SourceSha256);
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }

    [Fact]
    public void PlanTab_RequestContainsOnlySavedIdentityVersionAndPurpose()
    {
        var noteId = Guid.NewGuid();
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var vm = new PlanVm
        {
            ClinicalSummary = "Existing summary",
            TreatmentFrequency = "2x/week",
            TreatmentDuration = "6 weeks",
            HomeExerciseProgramNotes = "Continue knee HEP"
        };
        vm.TreatmentFocuses.Add("Strength");
        vm.SelectedCptCodes.Add(new CptCodeEntry { Code = "97110", Description = "Therapeutic exercise" });

        NoteNarrativeRequest? capturedRequest = null;
        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NoteNarrativeRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI summary draft"));

        var structuredInputs = new[]
        {
            new AiStructuredInput { Label = "Treatment frequency", Value = "2x/week", BodyPart = "Lumbar" },
            new AiStructuredInput { Label = "Treatment duration", Value = "6 weeks", BodyPart = "Lumbar" },
            new AiStructuredInput { Label = "Treatment focuses", Value = "Strength", BodyPart = "Lumbar" },
            new AiStructuredInput { Label = "Home exercise program", Value = "Continue knee HEP", BodyPart = "Lumbar" },
            new AiStructuredInput { Label = "Selected CPT codes", Value = "97110 Therapeutic exercise", BodyPart = "Lumbar" }
        };

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService, structuredInputs: structuredInputs, selectedBodyPart: " Lumbar ");

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(capturedRequest);
            Assert.Equal(noteId, capturedRequest!.NoteId);
            Assert.Equal(SavedVersion, capturedRequest.ExpectedLastModifiedUtc);
            Assert.Equal(NoteNarrativePurpose.SoapSummary, capturedRequest.Purpose);
            Assert.Equal(3, typeof(NoteNarrativeRequest).GetProperties().Length);
        });
    }

    [Fact]
    public void PlanTab_GenerationFailureShowsVisibleErrorMessage()
    {
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();
        var vm = new PlanVm();

        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteNarrativeResult
            {
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanTab_ChangedSourceInvalidatesPendingAcceptance(bool signed)
    {
        var workspace = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var client = new Mock<INoteNarrativeClient>();
        client.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI draft"));
        var vm = new PlanVm { ClinicalSummary = "Original" };
        var cut = RenderSavedPlanTab(vm, Guid.NewGuid(), workspace, client);
        cut.Find("[data-testid='generate-summary-btn']").Click();
        cut.SetParametersAndRender(p => p.Add(c => c.HasUnsavedChanges, !signed).Add(c => c.IsNoteSigned, signed));
        Assert.Equal("Original", vm.ClinicalSummary);
        Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-accept-btn']"));
        workspace.Verify(service => service.AcceptAiSuggestionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void PlanTab_GenerateSummary_WhenReviewUnavailable_ShowsErrorToastOnly()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new PlanVm
        {
            ClinicalSummary = "Original summary"
        };

        aiService
            .Setup(service => service.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest request, CancellationToken _) => CreateSuccessfulPlanResult(request, "AI summary draft"));

        var cut = RenderSavedPlanTab(vm, noteId, workspaceService, aiService, forceAiReviewUnavailable: true);

        cut.Find("[data-testid='generate-summary-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to open AI review for the clinical summary. Please try again.", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-review-banner']"));
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Unable to open AI review for the clinical summary. Please try again.", toast.Message);
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("version")]
    [InlineData("note")]
    [InlineData("signed")]
    [InlineData("disposed")]
    public async Task PlanTab_DropsLateSuggestionWhenSourceContextChanges(string change)
    {
        var noteId = Guid.NewGuid();
        var vm = new PlanVm { ClinicalSummary = "Accepted original" };
        var workspace = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var ai = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var pending = new TaskCompletionSource<NoteNarrativeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        NoteNarrativeRequest? request = null;
        ai.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<NoteNarrativeRequest, CancellationToken>((r, ct) => { request = r; token = ct; })
            .Returns(pending.Task);
        var cut = RenderSavedPlanTab(vm, noteId, workspace, ai);
        var click = cut.Find("[data-testid='generate-summary-btn']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.NotNull(request));
        if (change == "disposed") await cut.InvokeAsync(cut.Instance.Dispose);
        else cut.SetParametersAndRender(p =>
        {
            if (change == "dirty") p.Add(c => c.HasUnsavedChanges, true);
            if (change == "version") p.Add(c => c.SourceLastModifiedUtc, SavedVersion.AddSeconds(1));
            if (change == "note") p.Add(c => c.NoteId, Guid.NewGuid());
            if (change == "signed") p.Add(c => c.IsNoteSigned, true);
        });
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(CreateSuccessfulPlanResult(request!, "Late suggestion"));
        await click;
        Assert.Equal("Accepted original", vm.ClinicalSummary);
        Assert.Empty(vm.AcceptedNarrativeProvenance);
        if (change != "disposed") Assert.Empty(cut.FindAll("[data-testid='clinical-summary-box-review-banner']"));
    }

    [Fact]
    public void PlanTab_DirtySavedNoteCannotGenerateAndManualTextRemainsEditable()
    {
        var ai = new Mock<INoteNarrativeClient>(MockBehavior.Strict);
        var vm = new PlanVm();
        var cut = RenderSavedPlanTab(vm, Guid.NewGuid(), new Mock<INoteWorkspaceService>(MockBehavior.Loose), ai);
        cut.SetParametersAndRender(p => p.Add(c => c.HasUnsavedChanges, true));
        Assert.True(cut.Find("[data-testid='generate-summary-btn']").HasAttribute("disabled"));
        cut.Find("[data-testid='clinical-summary-box-textarea']").Input("Manual editing works");
        Assert.Equal("Manual editing works", vm.ClinicalSummary);
        ai.VerifyNoOtherCalls();
    }

    private IRenderedComponent<PlanTab> RenderSavedPlanTab(
        PlanVm vm,
        Guid noteId,
        Mock<INoteWorkspaceService> workspaceService,
        Mock<INoteNarrativeClient> aiService,
        bool forceAiReviewUnavailable = false,
        IReadOnlyList<AiStructuredInput>? structuredInputs = null,
        string selectedBodyPart = "Lumbar")
    {
        workspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Lumbar, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Lumbar
            });

        Services.AddLogging();
        Services.AddSingleton(workspaceService.Object);
        Services.AddSingleton(aiService.Object);

        var cut = RenderComponent<PlanTab>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, _ => { }))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.SourceLastModifiedUtc, SavedVersion)
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.SelectedBodyPart, selectedBodyPart)
            .Add(component => component.DiagnosisSummary, "Lumbar strain")
            .Add(component => component.StructuredInputs, structuredInputs ?? Array.Empty<AiStructuredInput>()));

        cut.Instance.TreatAiReviewBoxAsUnavailable = forceAiReviewUnavailable;
        return cut;
    }

    private static NoteNarrativeResult CreateSuccessfulPlanResult(NoteNarrativeRequest request, string generatedText) => new()
    {
        NoteId = request.NoteId, Purpose = request.Purpose, SourceLastModifiedUtc = request.ExpectedLastModifiedUtc,
        SourceSha256 = "source-hash",
        Sections = [new(request.Purpose, generatedText, new AiPromptMetadata { TemplateVersion = "section-narrative-v1", Model = "test-model", GeneratedAtUtc = DateTime.UtcNow })],
        Success = true
    };
}
