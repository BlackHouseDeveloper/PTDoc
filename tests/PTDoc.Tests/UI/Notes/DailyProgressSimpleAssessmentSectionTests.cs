using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DailyTreatment;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DailyProgressSimpleAssessmentSectionTests : TestContext
{
    [Fact]
    public void SimpleAssessmentSection_RendersOnlyAdditionalNotesSurface()
    {
        var vm = new AssessmentWorkspaceVm
        {
            AssessmentNarrative = "Visit-specific assessment.",
            DeficitsSummary = "Hidden deficit text.",
            OverallPrognosis = "Hidden prognosis."
        };

        var cut = RenderComponent<SimpleAssessmentSection>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<AssessmentWorkspaceVm>(this, updated => vm = updated)));

        Assert.Contains("Additional Notes", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Visit-specific assessment.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Deficits", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Prognosis", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden deficit text.", cut.Markup, StringComparison.Ordinal);
    }
}
