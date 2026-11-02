using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task CloseWhileRegistrationPending_WaitsToRemoveHandlerAndDoesNotFocusClosedDialog(string action)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        var register = module.SetupVoid("registerEscapeHandler", _ => true);
        var cut = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["registerEscapeHandler"]));

        Task closing;
        if (action == "parent")
        {
            cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, false));
            closing = Task.CompletedTask;
        }
        else if (action == "dispose")
        {
            closing = cut.InvokeAsync(async () => await cut.Instance.DisposeAsync());
        }
        else
        {
            closing = cut.Find($"[data-testid='incomplete-note-{action}']").ClickAsync(new MouseEventArgs());
        }

        // Drain the renderer queue while the interop completion is deliberately withheld.
        await cut.InvokeAsync(() => Task.CompletedTask);
        Assert.Empty(module.Invocations["unregisterEscapeHandler"]);
        await cut.InvokeAsync(() => register.SetVoidResult());
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["unregisterEscapeHandler"]));
        Assert.Empty(JSInterop.Invocations.Where(invocation => invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(module.Invocations["registerEscapeHandler"].Single().Arguments[0],
            module.Invocations["unregisterEscapeHandler"].Single().Arguments[0]);
    }

    [Fact]
    public async Task ReopenWhileRegistrationPending_CleansOldHandlerBeforeRegisteringAgain()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        var secondRegistration = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCleanup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registrationCalls = 0;
        var cleanupCalls = 0;
        var register = module.SetupVoid("registerEscapeHandler", _ =>
        {
            if (Interlocked.Increment(ref registrationCalls) == 2)
            {
                secondRegistration.TrySetResult(true);
            }
            return true;
        });
        var unregister = module.SetupVoid("unregisterEscapeHandler", _ =>
        {
            var call = Interlocked.Increment(ref cleanupCalls);
            if (call == 1)
            {
                firstCleanup.TrySetResult(true);
            }
            else if (call == 2)
            {
                secondCleanup.TrySetResult(true);
            }
            return true;
        });
        var cut = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));
        cut.WaitForAssertion(() => Assert.Single(module.Invocations["registerEscapeHandler"]));

        cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, false));
        cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, true));
        await cut.InvokeAsync(() => Task.CompletedTask);
        Assert.Empty(module.Invocations["unregisterEscapeHandler"]);
        Assert.Single(module.Invocations["registerEscapeHandler"]);

        await cut.InvokeAsync(() => register.SetVoidResult());
        await firstCleanup.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Single(module.Invocations["unregisterEscapeHandler"]);
        // The reopened generation must also wait for the old cleanup to finish.
        Assert.Single(module.Invocations["registerEscapeHandler"]);
        await cut.InvokeAsync(() => unregister.SetVoidResult());
        // Interop can resume after the render that queued it, without another render.
        await secondRegistration.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, module.Invocations["registerEscapeHandler"].Count);
        Assert.Single(module.Invocations["unregisterEscapeHandler"]);

        cut.SetParametersAndRender(parameters => parameters.Add(component => component.IsOpen, false));
        await secondCleanup.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, module.Invocations["unregisterEscapeHandler"].Count);
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

    [Fact]
    public void Registration_DisablesDefaultAutofocus_WhenCancelOwnsInitialFocus()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        var cut = RenderComponent<IncompleteNoteNavigationModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.CompletionState, Missing));

        cut.WaitForAssertion(() => Assert.Single(module.Invocations["registerEscapeHandler"]));
        var invocation = module.Invocations["registerEscapeHandler"].Single();
        Assert.Equal(false, invocation.Arguments[2]);
    }
}
