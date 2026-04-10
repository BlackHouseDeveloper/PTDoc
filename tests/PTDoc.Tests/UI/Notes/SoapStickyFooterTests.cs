using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes;
using PTDoc.UI.Components.Notes.Models;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class SoapStickyFooterTests : TestContext
{
    [Fact]
    public void ReadOnlyViewer_HidesAuthoringActions_AndShowsReadOnlyState()
    {
        var cut = RenderComponent<SoapStickyFooter>(parameters => parameters
            .Add(component => component.NoteType, "Progress Note")
            .Add(component => component.ActiveSection, SoapSection.Subjective)
            .Add(component => component.SaveState, NoteSaveState.Unsaved)
            .Add(component => component.IsReadOnly, true)
            .Add(component => component.AllowSubmitWhenReadOnly, false)
            .Add(component => component.OnBack, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnPrevSection, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnNextSection, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnGoalsClick, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnReviewClick, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSubmit, EventCallback.Factory.Create(this, () => { })));

        Assert.Contains("Read-only", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='footer-save']"));
        Assert.Empty(cut.FindAll("[data-testid='footer-submit']"));
    }

    [Fact]
    public void ReadOnlyFinalizer_ShowsSubmitWithoutSave()
    {
        var cut = RenderComponent<SoapStickyFooter>(parameters => parameters
            .Add(component => component.NoteType, "Daily Treatment Note")
            .Add(component => component.ActiveSection, SoapSection.Review)
            .Add(component => component.SaveState, NoteSaveState.Saved)
            .Add(component => component.IsReadOnly, true)
            .Add(component => component.AllowSubmitWhenReadOnly, true)
            .Add(component => component.OnBack, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnPrevSection, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnNextSection, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnGoalsClick, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnReviewClick, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSubmit, EventCallback.Factory.Create(this, () => { })));

        Assert.Empty(cut.FindAll("[data-testid='footer-save']"));
        Assert.Single(cut.FindAll("[data-testid='footer-submit']"));
        Assert.DoesNotContain("Read-only", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
    }
}
