using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.ProgressNote;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class ProgressSubjectiveSectionTests : TestContext
{
    [Fact]
    public void ProgressSubjectiveSection_UsesSliderForCurrentPain()
    {
        var subjective = new SubjectiveVm();
        var progress = new ProgressSubjectiveVm();

        var cut = RenderComponent<ProgressSubjectiveSection>(parameters => parameters
            .Add(component => component.Vm, subjective)
            .Add(component => component.ProgressSubjective, progress)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => subjective = updated))
            .Add(component => component.ProgressSubjectiveChanged, EventCallback.Factory.Create<ProgressSubjectiveVm>(this, updated => progress = updated)));

        var painSlider = cut.Find("#progress-current-pain");

        Assert.Equal("range", painSlider.GetAttribute("type"));
    }

    [Fact]
    public void ProgressSubjectiveSection_PersistsQuestionnaireAnswers()
    {
        var subjective = new SubjectiveVm();
        var progress = new ProgressSubjectiveVm();

        var cut = RenderComponent<ProgressSubjectiveSection>(parameters => parameters
            .Add(component => component.Vm, subjective)
            .Add(component => component.ProgressSubjective, progress)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => subjective = updated))
            .Add(component => component.ProgressSubjectiveChanged, EventCallback.Factory.Create<ProgressSubjectiveVm>(this, updated => progress = updated)));

        cut.Find("#progress-current-pain").Input("6");
        cut.Find("#progress-additional-information").Input("Patient reports new difficulty carrying groceries.");

        cut.WaitForAssertion(() =>
        {
            Assert.True(subjective.IsPainScoreDocumented);
            Assert.Equal(6, subjective.CurrentPainScore);
            Assert.Equal("Patient reports new difficulty carrying groceries.", progress.AdditionalInformation);
        });
    }
}
