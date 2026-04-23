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
public sealed class DailyTreatmentAiReviewTests : TestContext
{
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
                    ChiefComplaint = "Knee pain"
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
                    Diagnosis = "Lumbar strain"
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
                    Diagnosis = "Lumbar strain"
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
