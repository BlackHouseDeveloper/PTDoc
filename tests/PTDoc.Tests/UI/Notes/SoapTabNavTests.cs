using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class SoapTabNavTests : TestContext
{
    [Fact]
    public void ReadOnlyTabs_ExposeViewerStyling_WithoutBreakingNavigation()
    {
        SoapSection? selectedSection = null;

        var cut = RenderComponent<SoapTabNav>(parameters => parameters
            .Add(component => component.ActiveSection, SoapSection.Subjective)
            .Add(component => component.IsReadOnly, true)
            .Add(component => component.ActiveSectionChanged, EventCallback.Factory.Create<SoapSection>(this, section => selectedSection = section)));

        var nav = cut.Find("[data-testid='soap-tab-nav']");
        Assert.Equal("true", nav.GetAttribute("aria-readonly"));
        Assert.Contains("soap-tab-nav--readonly", nav.ClassList);

        var objectiveTab = cut.Find("[data-testid='soap-tab-objective']");
        Assert.Contains("soap-tab-nav__tab--readonly", objectiveTab.ClassList);

        objectiveTab.Click();

        Assert.Equal(SoapSection.Objective, selectedSection);
    }
}
