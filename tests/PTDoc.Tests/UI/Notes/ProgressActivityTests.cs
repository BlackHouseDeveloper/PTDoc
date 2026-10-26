using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using PTDoc.UI.Components.Notes.Workspace.ProgressNote;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class ProgressActivityTests : TestContext
{
    public ProgressActivityTests()
    {
        Services.AddLogging(); Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton(new Mock<IAiClinicalGenerationService>().Object);
        var service = new Mock<INoteWorkspaceService>();
        service.Setup(s => s.GetBodyRegionCatalogAsync(It.IsAny<BodyPart>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BodyPart body, CancellationToken _) => Catalog(body));
        Services.AddSingleton(service.Object);
    }

    [Fact]
    public void ActivityEditor_UsesRecordedBaselineAndFullMultiRegionCatalog_WithExclusiveStates()
    {
        var baseline = new ProgressActivityReference("recorded-1", BodyPart.Knee, "Recorded knee activity", "Function", "recorded");
        var value = new ProgressActivityTrackingV1 { Baseline = [baseline], ImprovedIds = [baseline.Id] };
        var cut = RenderComponent<ProgressActivityEditor>(p => p.Add(c => c.Value, value)
            .Add(c => c.BodyParts, new[] { BodyPart.Knee, BodyPart.Shoulder })
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<ProgressActivityTrackingV1>(this, v => value = v)));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll("[data-testid='progress-new-activity']").Count));
        Assert.Single(cut.FindAll("select"));
        cut.Find("select").Change("worse");
        Assert.Empty(value.ImprovedIds); Assert.Contains(baseline.Id, value.WorseIds);
        cut.SetParametersAndRender(p => p.Add(c => c.Value, value));
        cut.FindAll("[data-testid='progress-new-activity']")[0].Change(true);
        Assert.Single(value.NewlyDifficult);
        Assert.DoesNotContain(value.NewlyDifficult, a => a.Id == baseline.Id);
        Assert.Contains("Shoulder", cut.Markup);
    }

    [Fact]
    public void PercentEdit_DoesNotAssertADocumentedPainScore_AndRetainsLegacyAnswers()
    {
        var subjective = new SubjectiveVm(); var progress = new ProgressSubjectiveVm { ImprovedActivities = ["Historical label"] };
        var cut = RenderComponent<ProgressSubjectiveSection>(p => p.Add(c => c.Vm, subjective)
            .Add(c => c.ProgressSubjective, progress).Add(c => c.ProgressSubjectiveChanged,
                EventCallback.Factory.Create<ProgressSubjectiveVm>(this, v => progress = v)));
        cut.Find("#progress-percent-improved").Change("0");
        Assert.Equal(0, progress.PercentImproved); Assert.False(subjective.IsPainScoreDocumented);
        Assert.Contains("Historical label", cut.Find("[data-testid='progress-legacy-activities']").TextContent);
        Assert.Contains("8. Does your condition", cut.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Frequency_IsProgressOnlyAndEditable(bool progress)
    {
        var vm = new PlanVm { TreatmentFrequency = "1/2x/week" };
        var cut = RenderComponent<PlanSection>(p => p.Add(c => c.Vm, vm).Add(c => c.ShowProgressFrequency, progress)
            .Add(c => c.VmChanged, EventCallback.Factory.Create<PlanVm>(this, v => vm = v)));
        if (!progress) { Assert.Empty(cut.FindAll("#progress-plan-frequency")); return; }
        Assert.Contains("1/2x/week", cut.Markup);
        cut.Find("#progress-plan-frequency").Change("3x/week"); Assert.Equal("3x/week", vm.TreatmentFrequency);
    }

    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(100, true)] [InlineData(101, false)]
    public void AllNoteNormalizationPaths_ValidatePercentBounds(int percent, bool valid)
    {
        var json = JsonSerializer.Serialize(new NoteWorkspaceV2Payload { ProgressQuestionnaire = new() { PercentImproved = percent } });
        if (valid) NoteWriteService.NormalizeContentJson(NoteType.ProgressNote, false, DateTime.UtcNow, json);
        else Assert.Throws<InvalidOperationException>(() => NoteWriteService.NormalizeContentJson(NoteType.ProgressNote, false, DateTime.UtcNow, json));
    }

    [Fact]
    public void IdentityAndHistoryGuard_KeepRegionsDistinctAndRejectSilentBaselineReplacement()
    {
        var knee = ProgressActivityTracking.CatalogOptions(Catalog(BodyPart.Knee));
        var shoulder = ProgressActivityTracking.CatalogOptions(Catalog(BodyPart.Shoulder));
        Assert.NotEqual(knee[0].Id, shoulder[0].Id);
        Assert.Equal(knee[0].Id, ProgressActivityTracking.CatalogOptions(Catalog(BodyPart.Knee))[0].Id);
        var before = new NoteWorkspaceV2Payload { ProgressQuestionnaire = new() { Activities = new() { Baseline = [knee[0]] } } };
        var after = new NoteWorkspaceV2Payload { ProgressQuestionnaire = new() { Activities = new() { Baseline = [shoulder[0]] } } };
        Assert.Throws<InvalidOperationException>(() => ProgressActivityTracking.ValidateJsonWrite(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
        after.ProgressQuestionnaire.Activities = null;
        Assert.Throws<InvalidOperationException>(() => ProgressActivityTracking.ValidateJsonWrite(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after)));
    }

    private static BodyRegionCatalog Catalog(BodyPart body) => new()
    {
        BodyPart = body, FunctionalLimitationCategories = [new() { Name = "Function", Items = ["Activity A", "Activity B"] }]
    };
}
