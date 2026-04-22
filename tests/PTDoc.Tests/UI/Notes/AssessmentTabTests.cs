using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class AssessmentTabTests : TestContext
{
    [Fact]
    public void AssessmentTab_AcceptException_ShowsVisibleErrorAndKeepsReviewPending()
    {
        var noteId = Guid.NewGuid();
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        var workspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var assessmentText = "Original assessment";
        var functionalLimitations = string.Empty;

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

        workspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "assessment",
                "AI assessment draft",
                "Assessment",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transport failed"));

        Services.AddLogging();
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(workspaceService.Object);

        var cut = RenderComponent<AssessmentTab>(parameters => parameters
            .Add(component => component.NoteId, noteId)
            .Add(component => component.AssessmentText, assessmentText)
            .Add(component => component.AssessmentTextChanged, EventCallback.Factory.Create<string>(this, value => assessmentText = value))
            .Add(component => component.FunctionalLimitations, functionalLimitations)
            .Add(component => component.FunctionalLimitationsChanged, EventCallback.Factory.Create<string>(this, value => functionalLimitations = value))
            .Add(component => component.ChiefComplaint, "Knee pain")
            .Add(component => component.IsNoteSigned, false));

        cut.Find("[data-testid='assessment-generate-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='assessment-narrative-box-review-banner']");
            Assert.Equal("Original assessment", assessmentText);
            Assert.Equal("AI assessment draft", cut.Find("[data-testid='assessment-narrative-box-textarea']").GetAttribute("value"));
        });

        cut.Find("[data-testid='assessment-narrative-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Unable to accept AI-generated assessment content.",
                cut.Find("[data-testid='assessment-tab-error']").TextContent,
                StringComparison.Ordinal);
            Assert.Contains(
                "Unable to accept AI-generated assessment content.",
                cut.Find("[data-testid='assessment-narrative-box-error']").TextContent,
                StringComparison.Ordinal);
            Assert.Equal("Original assessment", assessmentText);
            cut.Find("[data-testid='assessment-narrative-box-review-banner']");
            cut.Find("[data-testid='assessment-narrative-box-actions']");
            Assert.Equal("AI assessment draft", cut.Find("[data-testid='assessment-narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='assessment-narrative-box-accepted-note']"));
        });

        cut.Find("[data-testid='assessment-narrative-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Original assessment", assessmentText);
            Assert.Equal("Original assessment", cut.Find("[data-testid='assessment-narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='assessment-narrative-box-review-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='assessment-narrative-box-error']"));
        });

        aiService.VerifyAll();
        workspaceService.VerifyAll();
    }
}
