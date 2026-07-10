using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Completion;
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

    [Fact]
    public void Tabs_UseProvidedNoteSpecificSections()
    {
        var cut = RenderComponent<SoapTabNav>(parameters => parameters
            .Add(component => component.ActiveSection, SoapSection.Interventions)
            .Add(component => component.Sections, new[]
            {
                SoapSection.Subjective,
                SoapSection.Objective,
                SoapSection.Interventions,
                SoapSection.Assessment,
                SoapSection.Plan,
                SoapSection.Review
            })
            .Add(component => component.ActiveSectionChanged, EventCallback.Factory.Create<SoapSection>(this, _ => { })));

        Assert.Single(cut.FindAll("[data-testid='soap-tab-interventions']"));
        Assert.Contains("Interventions", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabs_ShowMissingRequiredCounts_FromCompletionState()
    {
        var missing = new[]
        {
            new MissingRequiredItem(
                "plan-treatment-frequency",
                SoapSection.Plan,
                "Treatment frequency",
                "Select a treatment frequency.",
                "[data-note-field-key='plan-treatment-frequency']")
        };
        var sections = new[]
        {
            SoapSection.Subjective,
            SoapSection.Objective,
            SoapSection.Assessment,
            SoapSection.Plan,
            SoapSection.Review
        };
        var completion = new NoteCompletionState(
            missing,
            NoteCompletionState.BuildSectionStates(sections, missing));

        var cut = RenderComponent<SoapTabNav>(parameters => parameters
            .Add(component => component.ActiveSection, SoapSection.Subjective)
            .Add(component => component.Sections, sections)
            .Add(component => component.CompletionState, completion)
            .Add(component => component.ActiveSectionChanged, EventCallback.Factory.Create<SoapSection>(this, _ => { })));

        var planTab = cut.Find("[data-testid='soap-tab-plan']");
        Assert.Contains("soap-tab-nav__tab--missing", planTab.ClassList);
        Assert.Contains("1", planTab.TextContent, StringComparison.Ordinal);
        Assert.Equal("Plan: 1 required item missing", planTab.GetAttribute("aria-label"));
    }
}
