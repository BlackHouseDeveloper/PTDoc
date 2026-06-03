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
public sealed class DailyTreatmentAiReviewTests : TestContext
{
    public DailyTreatmentAiReviewTests()
    {
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void AssessmentSection_GeneratedNarrativeRequiresExplicitAcceptance()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var vm = new AssessmentWorkspaceVm
        {
            AssessmentNarrative = "Existing assessment"
        };

        aiService
            .Setup(service => service.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentGenerationResult
            {
                GeneratedText = "AI assessment draft",
                Confidence = 0.85,
                SourceInputs = new AssessmentGenerationRequest
                {
                    NoteId = noteId,
                    ChiefComplaint = "Knee pain",
                    SelectedBodyPart = "Knee"
                },
                Success = true
            });

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<AssessmentSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.ChiefComplaint, "Knee pain")
            .Add(component => component.SelectedBodyPart, "Knee")
            .Add(component => component.IsReadOnly, false));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Assessment", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI-generated content", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("Existing assessment", vm.AssessmentNarrative);
        });

        cut.Find("[data-testid='daily-assessment-narrative-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Existing assessment", vm.AssessmentNarrative);
            Assert.DoesNotContain("AI-generated content", cut.Markup, StringComparison.Ordinal);
        });

        aiService.VerifyAll();
    }

    [Fact]
    public void AssessmentSection_Generate_WhenReviewUnavailable_ShowsErrorToastOnly()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var vm = new AssessmentWorkspaceVm
        {
            AssessmentNarrative = "Existing assessment"
        };

        aiService
            .Setup(service => service.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentGenerationResult
            {
                GeneratedText = "AI assessment draft",
                Confidence = 0.85,
                SourceInputs = new AssessmentGenerationRequest
                {
                    NoteId = noteId,
                    ChiefComplaint = "Knee pain",
                    SelectedBodyPart = "Knee"
                },
                Success = true
            });

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<AssessmentSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.ChiefComplaint, "Knee pain")
            .Add(component => component.SelectedBodyPart, "Knee")
            .Add(component => component.IsReadOnly, false));

        cut.Instance.TreatAiReviewBoxAsUnavailable = true;

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Assessment", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to open AI review for the assessment narrative. Please try again.", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='daily-assessment-narrative-box-review-banner']"));
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Unable to open AI review for the assessment narrative. Please try again.", toast.Message);
        });

        aiService.VerifyAll();
    }

    [Fact]
    public void PlanSection_GeneratedSummaryRequiresExplicitAcceptance()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var vm = new PlanVm
        {
            ClinicalSummary = "Existing summary"
        };

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI plan summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = true
            });

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.DiagnosisSummary, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.IsReadOnly, false));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI-generated content", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("Existing summary", vm.ClinicalSummary);
        });

        cut.Find("[data-testid='daily-plan-clinical-summary-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Existing summary", vm.ClinicalSummary);
            Assert.DoesNotContain("AI-generated content", cut.Markup, StringComparison.Ordinal);
        });

        aiService.VerifyAll();
    }

    [Fact]
    public void PlanSection_PlanRequestDeduplicatesStructuredInputs()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var vm = new PlanVm
        {
            ClinicalSummary = "Existing summary",
            HomeExerciseProgramNotes = "Continue HEP",
            FollowUpInstructions = "Progress next visit"
        };
        vm.GeneralInterventions.Add(new GeneralInterventionEntry
        {
            Name = "Therapeutic exercise",
            Notes = "Quad strengthening"
        });
        var dailyTreatment = new DailyTreatmentVm
        {
            ResponseToTreatment = "Tolerated session without symptom increase."
        };
        PlanOfCareGenerationRequest? capturedRequest = null;

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PlanOfCareGenerationRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI plan summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = true
            });

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.DailyTreatment, dailyTreatment)
            .Add(component => component.DailyTreatmentChanged, EventCallback.Factory.Create<DailyTreatmentVm>(this, updated => dailyTreatment = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.DiagnosisSummary, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.StructuredInputs, new[]
            {
                new AiStructuredInput { Label = "Daily treatment plan", Value = "Continue HEP", BodyPart = "Lumbar" },
                new AiStructuredInput { Label = "Daily interventions", Value = "Therapeutic exercise - Quad strengthening", BodyPart = "Lumbar" },
                new AiStructuredInput { Label = "Response to treatment", Value = "Tolerated session without symptom increase.", BodyPart = "Lumbar" },
                new AiStructuredInput { Label = "Follow-up instructions", Value = "Progress next visit", BodyPart = "Lumbar" }
            })
            .Add(component => component.IsReadOnly, false));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(capturedRequest);
            Assert.Equal(4, capturedRequest!.StructuredInputs.Count);
            Assert.All(capturedRequest.StructuredInputs, input =>
            {
                var matchingRows = capturedRequest.StructuredInputs.Count(candidate =>
                    string.Equals(candidate.Label, input.Label, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Value, input.Value, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.BodyPart, input.BodyPart, StringComparison.OrdinalIgnoreCase));
                Assert.Equal(1, matchingRows);
            });
        });
    }

    [Fact]
    public void PlanSection_GenerateSummary_WhenReviewUnavailable_ShowsErrorToastOnly()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);
        var vm = new PlanVm
        {
            ClinicalSummary = "Existing summary"
        };

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI plan summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = true
            });

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.DiagnosisSummary, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.IsReadOnly, false));

        cut.Instance.TreatAiReviewBoxAsUnavailable = true;

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to open AI review for the clinical summary. Please try again.", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='daily-plan-clinical-summary-box-review-banner']"));
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Unable to open AI review for the clinical summary. Please try again.", toast.Message);
        });

        aiService.VerifyAll();
    }

    [Fact]
    public void PlanSection_SummaryAcceptException_ShowsVisibleErrorAndKeepsReviewPending()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var vm = new PlanVm
        {
            ClinicalSummary = "Existing summary"
        };

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI plan summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = true
            });

        workspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI plan summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transport failed"));

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<PlanSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<PlanVm>(this, updated => vm = updated))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.DiagnosisSummary, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.IsReadOnly, false));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Generate Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='daily-plan-clinical-summary-box-review-banner']");
            Assert.Equal("Existing summary", vm.ClinicalSummary);
            Assert.Equal("AI plan summary draft", cut.Find("[data-testid='daily-plan-clinical-summary-box-textarea']").GetAttribute("value"));
        });

        cut.Find("[data-testid='daily-plan-clinical-summary-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to accept the AI-generated summary.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(
                "Unable to accept the AI-generated summary.",
                cut.Find("[data-testid='daily-plan-clinical-summary-box-error']").TextContent,
                StringComparison.Ordinal);
            Assert.Equal("Existing summary", vm.ClinicalSummary);
            cut.Find("[data-testid='daily-plan-clinical-summary-box-review-banner']");
            cut.Find("[data-testid='daily-plan-clinical-summary-box-actions']");
            Assert.Equal("AI plan summary draft", cut.Find("[data-testid='daily-plan-clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='daily-plan-clinical-summary-box-accepted-note']"));
        });

        cut.Find("[data-testid='daily-plan-clinical-summary-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Existing summary", vm.ClinicalSummary);
            Assert.Equal("Existing summary", cut.Find("[data-testid='daily-plan-clinical-summary-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='daily-plan-clinical-summary-box-review-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='daily-plan-clinical-summary-box-error']"));
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }
}
