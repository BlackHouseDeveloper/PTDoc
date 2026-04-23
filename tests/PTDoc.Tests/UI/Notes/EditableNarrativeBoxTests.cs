using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class EditableNarrativeBoxTests : TestContext
{
    [Fact]
    public async Task EditableNarrativeBox_PendingSuggestionStaysLocalUntilAccepted()
    {
        var valueChanges = new List<string>();
        string? acceptedText = null;

        var cut = RenderComponent<EditableNarrativeBox>(parameters => parameters
            .Add(component => component.Value, "Original clinician text")
            .Add(component => component.ValueChanged, EventCallback.Factory.Create<string>(this, value => valueChanges.Add(value)))
            .Add(component => component.OnAccepted, value =>
            {
                acceptedText = value;
                return Task.FromResult(EditableNarrativeAcceptResult.Ok());
            })
            .Add(component => component.TestId, "narrative-box"));

        await cut.InvokeAsync(() => cut.Instance.LoadAiSuggestionAsync("AI draft"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI-generated content", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("AI draft", cut.Find("[data-testid='narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(valueChanges);
        });

        cut.Find("[data-testid='narrative-box-textarea']").Input("AI draft edited by clinician");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("AI draft edited by clinician", cut.Find("[data-testid='narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(valueChanges);
        });

        cut.Find("[data-testid='narrative-box-accept-btn']").Click();

        Assert.Equal("AI draft edited by clinician", acceptedText);
        Assert.Empty(valueChanges);
    }

    [Fact]
    public async Task EditableNarrativeBox_DiscardRestoresOriginalValueWithoutPropagating()
    {
        var valueChanges = new List<string>();
        var discardedCount = 0;

        var cut = RenderComponent<EditableNarrativeBox>(parameters => parameters
            .Add(component => component.Value, "Existing clinician narrative")
            .Add(component => component.ValueChanged, EventCallback.Factory.Create<string>(this, value => valueChanges.Add(value)))
            .Add(component => component.OnDiscarded, EventCallback.Factory.Create(this, () => discardedCount++))
            .Add(component => component.TestId, "narrative-box"));

        await cut.InvokeAsync(() => cut.Instance.LoadAiSuggestionAsync("AI draft"));
        cut.Find("[data-testid='narrative-box-textarea']").Input("AI draft edited");
        cut.Find("[data-testid='narrative-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, discardedCount);
            Assert.Equal("Existing clinician narrative", cut.Find("[data-testid='narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(valueChanges);
            Assert.Empty(cut.FindAll("[data-testid='narrative-box-actions']"));
        });
    }

    [Fact]
    public async Task EditableNarrativeBox_WhenAcceptHandlerThrows_StaysPendingAndAllowsDiscard()
    {
        var cut = RenderComponent<EditableNarrativeBox>(parameters => parameters
            .Add(component => component.Value, "Existing clinician narrative")
            .Add(component => component.OnAccepted, _ => throw new InvalidOperationException("transport failed"))
            .Add(component => component.TestId, "narrative-box"));

        await cut.InvokeAsync(() => cut.Instance.LoadAiSuggestionAsync("AI draft"));
        cut.Find("[data-testid='narrative-box-accept-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='narrative-box-review-banner']");
            cut.Find("[data-testid='narrative-box-actions']");
            Assert.Equal("AI draft", cut.Find("[data-testid='narrative-box-textarea']").GetAttribute("value"));
            Assert.Contains("Unable to accept AI-generated content.", cut.Find("[data-testid='narrative-box-error']").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='narrative-box-accepted-note']"));
        });

        cut.Find("[data-testid='narrative-box-discard-btn']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Existing clinician narrative", cut.Find("[data-testid='narrative-box-textarea']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='narrative-box-review-banner']"));
            Assert.Empty(cut.FindAll("[data-testid='narrative-box-error']"));
        });
    }
}
