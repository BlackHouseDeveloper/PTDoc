using Bunit;
using PTDoc.UI.Components.Notes.Completion;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class IncompleteNoteNavigationModalTests : TestContext
{
    private static NoteCompletionState Missing => new(
        [new MissingRequiredItem("pain", SoapSection.Subjective, "Pain", "Required", "#pain")],
        new Dictionary<SoapSection, NoteSectionCompletionState>());

    [Theory]
    [InlineData("continue")]
    [InlineData("cancel")]
    [InlineData("parent")]
    [InlineData("dispose")]
    public async Task CloseWhileRegistrationPending_RemovesHandlerAndDoesNotFocusClosedDialog(string action)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        var register = module.SetupVoid("registerEscapeHandler", _ => true);
        var cut = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["registerEscapeHandler"]));

        if (action == "parent")
        {
            cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, false));
        }
        else if (action == "dispose")
        {
            await cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());
        }
        else
        {
            cut.Find($"[data-testid='incomplete-note-{action}']").Click();
        }

        cut.WaitForAssertion(() => Assert.Single(module.Invocations["unregisterEscapeHandler"]));
        await cut.InvokeAsync(() => register.SetVoidResult());
        Assert.Empty(JSInterop.Invocations.Where(invocation => invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(module.Invocations["registerEscapeHandler"].Single().Arguments[0],
            module.Invocations["unregisterEscapeHandler"].Single().Arguments[0]);
    }

    [Fact]
    public void ReopenAfterClose_RegistersAgainWithInstanceOwnedHandler()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        var cut = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["registerEscapeHandler"]));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, false));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, true));
        cut.WaitForAssertion(() => Assert.Equal(2, module.Invocations["registerEscapeHandler"].Count));
        var second = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));
        cut.WaitForAssertion(() => Assert.Equal(3, module.Invocations["registerEscapeHandler"].Count));
        Assert.NotEqual(module.Invocations["registerEscapeHandler"][0].Arguments[0],
            module.Invocations["registerEscapeHandler"][2].Arguments[0]);
    }
}
