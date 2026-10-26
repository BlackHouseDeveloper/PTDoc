using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class GoalRegenerationTests : TestContext
{
    [Theory]
    [InlineData("Accepted goal 1")]
    [InlineData("   ")]
    public void EightAcceptedGoals_DuplicateOrBlankResponsePreservesPendingAndExplainsNoNewCandidates(string response)
    {
        var (cut, vm, ai) = RenderGoals(response);
        cut.Find("[data-testid='ai-suggest-goals-btn']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(8, vm.Goals.Count(g => g.IsAccepted));
            Assert.Contains(vm.Goals, g => g.Description == "Pending reviewed candidate" && !g.IsAccepted);
            Assert.Contains("No new goal suggestions", cut.Find("[data-testid='smart-goals-card']").TextContent);
        });
        ai.VerifyAll();
    }

    [Fact]
    public void EightAcceptedGoals_NewCandidatesRemainPendingUntilAccepted_AndAcceptedGoalsRoundTrip()
    {
        var (cut, vm, _) = RenderGoals("New ninth candidate\nNew ninth candidate");
        cut.Find("[data-testid='ai-suggest-goals-btn']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(8, vm.Goals.Count(g => g.IsAccepted));
            Assert.Single(vm.Goals.Where(g => g.Description == "New ninth candidate"));
        });
        var mapper = new NoteWorkspacePayloadMapper(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());
        var ui = new NoteWorkspacePayload { Assessment = vm };
        var saved = mapper.MapToV2Payload(ui, NoteType.Evaluation);
        Assert.Equal(8, saved.Assessment.Goals.Count);
        var reloaded = mapper.MapToUiPayload(saved);
        Assert.Equal(8, reloaded.Assessment.Goals.Count(g => g.IsAccepted));
        Assert.DoesNotContain(reloaded.Assessment.Goals, g => g.Description == "New ninth candidate");
    }

    [Fact]
    public void TransientFailureAndRetry_PreserveAcceptedAndPendingState()
    {
        var (cut, vm, ai) = RenderGoals("New after retry");
        ai.SetupSequence(a => a.GenerateGoalNarrativesAsync(It.IsAny<GoalNarrativesGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result("", false)).ReturnsAsync(Result("New after retry", true));
        cut.Find("[data-testid='ai-suggest-goals-btn']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Synthetic transient failure", cut.Markup));
        Assert.Equal(9, vm.Goals.Count);
        cut.Find("[data-testid='ai-suggest-goals-btn']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(8, vm.Goals.Count(g => g.IsAccepted));
            Assert.Contains(vm.Goals, g => g.Description == "Pending reviewed candidate");
            Assert.Contains(vm.Goals, g => g.Description == "New after retry" && !g.IsAccepted);
        });
    }

    private (IRenderedComponent<AssessmentWorkspaceSection>, AssessmentWorkspaceVm, Mock<IAiClinicalGenerationService>) RenderGoals(string response)
    {
        Services.AddLogging(); Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton(new Mock<INoteWorkspaceService>(MockBehavior.Loose).Object);
        var ai = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);
        ai.Setup(a => a.GenerateGoalNarrativesAsync(It.IsAny<GoalNarrativesGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result(response, true));
        Services.AddSingleton(ai.Object);
        var vm = new AssessmentWorkspaceVm();
        for (var i = 1; i <= 8; i++) vm.Goals.Add(new() { Description = $"Accepted goal {i}", IsAiSuggested = true, IsAccepted = true, PatientGoalId = Guid.NewGuid() });
        vm.Goals.Add(new() { Description = "Pending reviewed candidate", IsAiSuggested = true, IsAccepted = false });
        var cut = RenderComponent<AssessmentWorkspaceSection>(p => p.Add(c => c.Vm, vm)
            .Add(c => c.NoteId, Guid.NewGuid()).Add(c => c.SelectedBodyPart, "Lumbar")
            .Add(c => c.ChiefComplaint, "Synthetic complaint").Add(c => c.IsReadOnly, false)
            .Add(c => c.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, _ => { })));
        return (cut, vm, ai);
    }

    private static GoalGenerationResult Result(string text, bool success) => new()
    {
        Success = success, GeneratedText = text, Confidence = success ? .8 : 0,
        ErrorMessage = success ? null : "Synthetic transient failure",
        SourceInputs = new GoalNarrativesGenerationRequest { NoteId = Guid.NewGuid(), Diagnosis = "Synthetic diagnosis", FunctionalLimitations = "Synthetic limitation" }
    };
}
