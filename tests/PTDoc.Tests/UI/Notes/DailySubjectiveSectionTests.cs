using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailySubjectiveSectionTests : TestContext
{
    [Fact]
    public void SubjectiveSection_HidesPreviousTreatmentQuestionAndShowsCarryForwardContext()
    {
        var subjective = new SubjectiveVm
        {
            Problems = ["Knee pain"],
            Locations = ["Right knee"]
        };
        var dailyTreatment = new DailyTreatmentVm
        {
            PreviousTreatment = "Prior visit focused on mobility."
        };

        var cut = RenderComponent<SubjectiveSection>(parameters => parameters
            .Add(component => component.Vm, subjective)
            .Add(component => component.DailyTreatment, dailyTreatment)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<SubjectiveVm>(this, updated => subjective = updated))
            .Add(component => component.DailyTreatmentChanged, EventCallback.Factory.Create<DailyTreatmentVm>(this, updated => dailyTreatment = updated)));

        Assert.Contains("Carried-forward chief complaint", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Knee pain", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous Treatment", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Prior visit focused on mobility.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Pain Level Changes", cut.Markup, StringComparison.Ordinal);
    }
}
