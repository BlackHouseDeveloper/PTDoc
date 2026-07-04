using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class AssessmentWorkspaceSectionPrognosisTests : TestContext
{
    public AssessmentWorkspaceSectionPrognosisTests()
    {
        Services.AddLogging();
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void PrognosisAi_UsesDedicatedPrognosisServiceAndUpdatesNarrative()
    {
        var noteId = Guid.NewGuid();
        var vm = new AssessmentWorkspaceVm
        {
            AssessmentNarrative = "Findings support skilled PT.",
            FindingsSummary = "Limited lumbar flexion and lifting tolerance.",
            FunctionalLimitations = "Difficulty lifting and prolonged sitting",
            SupportSystemLevel = "Strong - reliable network available"
        };
        vm.Goals.Add(new SmartGoalEntry { Description = "Return to work duties" });
        vm.BarriersToRecovery.Add("Work schedule conflicts");

        PrognosisGenerationRequest? capturedRequest = null;
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        aiService
            .Setup(service => service.GeneratePrognosisAsync(It.IsAny<PrognosisGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PrognosisGenerationRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new PrognosisGenerationResult
            {
                GeneratedText = "Dedicated prognosis draft",
                Confidence = 0.85,
                SourceInputs = new PrognosisGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = true
            });

        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);

        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<AssessmentWorkspaceSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, value => vm = value))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.ChiefComplaint, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.ExaminationFindings, "Lumbar flexion limited")
            .Add(component => component.SubjectiveInputs, new[]
            {
                new AiStructuredInput { Label = "Prior functional level", Value = "Independent with work", BodyPart = "Lumbar" },
                new AiStructuredInput { Label = "Current level of function", Value = "Unable to tolerate full shift", BodyPart = "Lumbar" }
            })
            .Add(component => component.ObjectiveInputs, new[]
            {
                new AiStructuredInput { Label = "ROM", Value = "Flexion limited", BodyPart = "Lumbar" }
            }));

        cut.Find("[data-testid='ai-suggest-prognosis-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Dedicated prognosis draft", vm.PrognosisNarrative);
            Assert.NotNull(capturedRequest);
            Assert.Equal(noteId, capturedRequest!.NoteId);
            Assert.Equal("Lumbar", capturedRequest.SelectedBodyPart);
            Assert.Equal("Lumbar strain", capturedRequest.Diagnosis);
            Assert.Equal("Difficulty lifting and prolonged sitting", capturedRequest.FunctionalLimitations);
            Assert.Contains("Return to work duties", capturedRequest.Goals, StringComparison.Ordinal);
            Assert.Contains("Work schedule conflicts", capturedRequest.Barriers, StringComparison.Ordinal);
            Assert.Equal("Independent with work", capturedRequest.PriorLevelOfFunction);
            Assert.Equal("Unable to tolerate full shift", capturedRequest.CurrentLevelOfFunction);
            Assert.Contains(capturedRequest.StructuredInputs, input => input.Label == "Prior functional level");
        });

        aiService.Verify(service => service.GeneratePrognosisAsync(It.IsAny<PrognosisGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        aiService.Verify(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void PrognosisAi_FailureRendersErrorInPrognosisCard()
    {
        var noteId = Guid.NewGuid();
        var vm = new AssessmentWorkspaceVm
        {
            AssessmentNarrative = "Findings support skilled PT.",
            FindingsSummary = "Limited lumbar flexion.",
            FunctionalLimitations = "Difficulty lifting"
        };

        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        aiService
            .Setup(service => service.GeneratePrognosisAsync(It.IsAny<PrognosisGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrognosisGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = new PrognosisGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Lumbar strain",
                    SelectedBodyPart = "Lumbar"
                },
                Success = false,
                ErrorMessage = "Unable to generate prognosis from current note context."
            });

        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);

        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<AssessmentWorkspaceSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, value => vm = value))
            .Add(component => component.NoteId, noteId)
            .Add(component => component.IsReadOnly, false)
            .Add(component => component.ChiefComplaint, "Lumbar strain")
            .Add(component => component.SelectedBodyPart, "Lumbar")
            .Add(component => component.ExaminationFindings, "Lumbar flexion limited"));

        cut.Find("[data-testid='ai-suggest-prognosis-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to generate prognosis from current note context.", cut.Find("[data-testid='prognosis-card']").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Unable to generate prognosis from current note context.", cut.Find("[data-testid='smart-goals-card']").TextContent, StringComparison.Ordinal);
        });
    }
}
