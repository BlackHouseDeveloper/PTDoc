using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Services;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;
using PTDoc.UI.Components.Notes.Workspace.DischargeNote;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class PlanNarrativeFieldTests : TestContext
{
    private readonly Mock<INoteNarrativeClient> _client = new();
    private readonly Guid _id = Guid.NewGuid();
    private readonly DateTime _version = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    public PlanNarrativeFieldTests() => Services.AddSingleton(_client.Object);
    private NoteNarrativeResult Result(NoteNarrativeRequest r) => new()
    {
        Success = true, NoteId = r.NoteId, Purpose = r.Purpose, SourceLastModifiedUtc = r.ExpectedLastModifiedUtc,
        SourceSha256 = "synthetic-source", Sections = [new(r.Purpose, "Reviewed draft", null)]
    };
    private IRenderedComponent<PlanNarrativeField> Render(PlanVm vm, NoteNarrativePurpose purpose, Action<PlanVm>? changed = null) =>
        RenderComponent<PlanNarrativeField>(p => p.Add(c => c.Vm, vm).Add(c => c.Purpose, purpose)
            .Add(c => c.NoteId, _id).Add(c => c.SourceLastModifiedUtc, _version)
            .Add(c => c.VmChanged, EventCallback.Factory.Create<PlanVm>(this, v => changed?.Invoke(v))));

    [Theory]
    [InlineData(NoteNarrativePurpose.NextVisitPlan, "FollowUpInstructions")]
    [InlineData(NoteNarrativePurpose.DischargeRecommendations, "DischargeRecommendations")]
    [InlineData(NoteNarrativePurpose.DischargeFollowUp, "PostDischargeInstructions")]
    [InlineData(NoteNarrativePurpose.DischargeSummary, "FullDischargeSummary")]
    [InlineData(NoteNarrativePurpose.SoapSummary, "ClinicalSummary")]
    public void EachPurpose_ReviewsDiscardsAndAcceptsOnlyItsField(NoteNarrativePurpose purpose, string field)
    {
        _client.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteNarrativeRequest r, CancellationToken _) => Result(r));
        var vm = new PlanVm { FollowUpInstructions = "Original", DischargeRecommendations = "Original", PostDischargeInstructions = "Original", FullDischargeSummary = "Original", ClinicalSummary = "Original" };
        var notifications = 0; var cut = Render(vm, purpose, _ => notifications++);
        cut.Find("button").Click();
        Assert.Equal(0, notifications); Assert.Equal("Original", typeof(PlanVm).GetProperty(field)!.GetValue(vm));
        cut.Find("[data-testid='plan-narrative-editor-discard-btn']").Click();
        Assert.Equal("Original", cut.Find("textarea").GetAttribute("value"));
        cut.Find("button").Click(); cut.Find("textarea").Input("Clinician edited draft");
        Assert.Equal(0, notifications);
        cut.Find("[data-testid='plan-narrative-editor-accept-btn']").Click();
        Assert.Equal(1, notifications); Assert.Equal("Clinician edited draft", typeof(PlanVm).GetProperty(field)!.GetValue(vm));
        Assert.Equal(purpose, Assert.Single(vm.AcceptedNarrativeProvenance).Value.Purpose);
        Assert.Equal("synthetic-source", vm.AcceptedNarrativeProvenance[field].SourceSha256);
        foreach (var other in new[] { "FollowUpInstructions", "DischargeRecommendations", "PostDischargeInstructions", "FullDischargeSummary", "ClinicalSummary" }.Where(n => n != field))
            Assert.Equal("Original", typeof(PlanVm).GetProperty(other)!.GetValue(vm));
        _client.Verify(c => c.GenerateAsync(new NoteNarrativeRequest(_id, _version, purpose), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("note")]
    [InlineData("version")]
    [InlineData("dirty")]
    [InlineData("readonly")]
    [InlineData("dispose")]
    [InlineData("manual")]
    public async Task LateResponse_DoesNotOverwriteChangedContext(string change)
    {
        var pending = new TaskCompletionSource<NoteNarrativeResult>();
        _client.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>())).Returns(pending.Task);
        var vm = new PlanVm { FollowUpInstructions = "Original" }; var cut = Render(vm, NoteNarrativePurpose.NextVisitPlan);
        var click = cut.InvokeAsync(() => cut.Find("button").Click());
        await Task.Yield();
        if (change == "note") cut.SetParametersAndRender(p => p.Add(c => c.NoteId, Guid.NewGuid()));
        if (change == "version") cut.SetParametersAndRender(p => p.Add(c => c.SourceLastModifiedUtc, _version.AddSeconds(1)));
        if (change == "dirty") cut.SetParametersAndRender(p => p.Add(c => c.HasUnsavedChanges, true));
        if (change == "readonly") cut.SetParametersAndRender(p => p.Add(c => c.IsReadOnly, true));
        if (change == "manual") cut.Find("textarea").Input("New manual text");
        if (change == "dispose") cut.Dispose();
        pending.SetResult(Result(new(_id, _version, NoteNarrativePurpose.NextVisitPlan))); await click;
        Assert.Equal(change == "manual" ? "New manual text" : "Original", vm.FollowUpInstructions);
        Assert.Empty(vm.AcceptedNarrativeProvenance);
        if (change != "dispose") Assert.Empty(cut.FindAll("[data-testid='plan-narrative-editor-accept-btn']"));
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("error")]
    [InlineData("wrong-purpose")]
    [InlineData("blank")]
    public void FailedGeneration_PreservesManualTextAndAllowsRetry(string kind)
    {
        _client.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>()))
            .Returns((NoteNarrativeRequest r, CancellationToken _) => kind switch
            {
                "error" => Task.FromException<NoteNarrativeResult>(new HttpRequestException()),
                "wrong-purpose" => Task.FromResult(Result(r with { Purpose = NoteNarrativePurpose.SoapSummary })),
                "blank" => Task.FromResult(Result(r) with { Sections = [] }),
                _ => Task.FromResult(new NoteNarrativeResult { Success = false, ErrorMessage = "AI is disabled. Continue editing manually." })
            });
        var vm = new PlanVm { FollowUpInstructions = "Original" };var cut = Render(vm, NoteNarrativePurpose.NextVisitPlan);
        cut.Find("button").Click(); Assert.Equal("Original", vm.FollowUpInstructions);
        Assert.Single(cut.FindAll("[role='alert']")); Assert.False(cut.Find("button").HasAttribute("disabled"));
        cut.Find("textarea").Input("Still editable"); Assert.Equal("Still editable", vm.FollowUpInstructions);
    }

    [Fact]
    public void PendingReview_IsInvalidatedWhenNoteChanges()
    {
        _client.Setup(c => c.GenerateAsync(It.IsAny<NoteNarrativeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((NoteNarrativeRequest r, CancellationToken _) => Result(r));
        var vm = new PlanVm { FollowUpInstructions = "Original" };var cut = Render(vm, NoteNarrativePurpose.NextVisitPlan);
        cut.Find("button").Click();cut.SetParametersAndRender(p => p.Add(c => c.HasUnsavedChanges, true));
        Assert.Empty(cut.FindAll("[data-testid='plan-narrative-editor-accept-btn']"));
        Assert.Equal("Original", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void ParentControls_WireAllPurposesAndKeepDailyNextVisitManual()
    {
        Services.AddSingleton(Mock.Of<IAiClinicalGenerationService>());Services.AddSingleton(Mock.Of<INoteWorkspaceService>());
        Services.AddSingleton<IToastService, ToastService>();Services.AddLogging();
        var discharge = RenderComponent<DischargePlanSection>(p => p.Add(c => c.NoteId, _id).Add(c => c.SourceLastModifiedUtc, _version));
        Assert.Equal(new[] { NoteNarrativePurpose.DischargeRecommendations, NoteNarrativePurpose.DischargeFollowUp, NoteNarrativePurpose.DischargeSummary, NoteNarrativePurpose.SoapSummary }, discharge.FindComponents<PlanNarrativeField>().Select(f => f.Instance.Purpose));
        var progress = RenderComponent<PlanSection>(p => p.Add(c => c.IsProgressNote, true).Add(c => c.NoteId, _id).Add(c => c.SourceLastModifiedUtc, _version));
        Assert.Equal(new[] { NoteNarrativePurpose.NextVisitPlan, NoteNarrativePurpose.SoapSummary }, progress.FindComponents<PlanNarrativeField>().Select(f => f.Instance.Purpose));
        progress.SetParametersAndRender(p => p.Add(c => c.IsProgressNote, false));
        Assert.Empty(progress.FindComponents<PlanNarrativeField>());
    }

    [Fact]
    public void DischargeSource_SeparatesPatientReportAndClinicianDecision()
    {
        var p = new NoteWorkspaceV2Payload { NoteType = NoteType.Discharge };
        p.Discharge.PatientReportedOutcome = "Patient report";p.Discharge.PercentImproved = 0;
        p.Discharge.GoalsMetStatus = "Patient thinks goals met";p.Discharge.RemainingDifficulty = "Stairs";
        p.Plan.PrimaryDischargeReason = "Clinician reason";p.Plan.PostDischargeInstructions = "Documented follow-up";
        var subjective = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.SubjectiveSummary);
        Assert.Contains(subjective, s => s.Value == "0" && s.Label.Contains("Patient-reported"));
        Assert.Contains(subjective, s => s.Value == "Patient thinks goals met" && s.Label.Contains("Patient-reported"));
        Assert.DoesNotContain(subjective, s => s.Value == "Clinician reason");
        var plan = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.PlanSummary);
        Assert.Contains(plan, s => s.Value == "Clinician reason");Assert.Contains(plan, s => s.Value == "Documented follow-up");
    }
}
