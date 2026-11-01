using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;
using PTDoc.Web.Components;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class WebSessionActivityMonitorTests : TestContext
{
    private readonly Mock<AuthenticationStateProvider> _authentication = new();
    private readonly ClaimsIdentity _identity;

    public WebSessionActivityMonitorTests()
    {
        Services.AddScoped<WebCircuitSessionState>();
        _identity = new ClaimsIdentity(new[]
        {
            new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
            new Claim("ptdoc_web_session_id", "synthetic-session"),
            new Claim("ptdoc_web_activity_ticket", "1")
        }, "Cookies");
        _authentication.Setup(value => value.GetAuthenticationStateAsync()).ReturnsAsync(new AuthenticationState(new ClaimsPrincipal(_identity)));
        Services.AddSingleton(_authentication.Object);
        var module = JSInterop.SetupModule("./js/sessionActivity.js");
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop").SetVoidResult();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void Monitor_StartsOnlyForCurrentActivityTickets(string? marker, bool starts)
    {
        _identity.RemoveClaim(_identity.FindFirst("ptdoc_web_activity_ticket"));
        if (marker is not null) _identity.AddClaim(new Claim("ptdoc_web_activity_ticket", marker));

        var cut = RenderComponent<WebSessionActivityMonitor>();

        cut.WaitForAssertion(() => Assert.Equal(starts ? 1 : 0,
            JSInterop.Invocations.Count(value => value.Identifier == "import")));
        Assert.Null(Services.GetRequiredService<WebCircuitSessionState>().EndedReason);
        Assert.Empty(cut.FindAll("[role=alert]"));
    }

    [Theory]
    [InlineData("SessionChanged", "Reload session")]
    [InlineData("SessionEnded", "Sign in")]
    public async Task RecoveryCallback_EndsSharedCircuitAndKeepsPageUntilUserChoosesRecovery(string reason, string action)
    {
        var state = Services.GetRequiredService<WebCircuitSessionState>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var cut = RenderComponent<WebSessionActivityMonitor>();

        await cut.InvokeAsync(() => cut.Instance.SessionEnded(reason));

        Assert.Equal(reason, state.EndedReason);
        Assert.Equal(originalUri, navigation.Uri);
        cut.WaitForAssertion(() => Assert.Contains(action, cut.Find("[role=alert]").TextContent));
        cut.Find("button").Click();
        if (reason == "SessionEnded") Assert.EndsWith("/login?session_expired=1", navigation.Uri);
        else Assert.Equal(originalUri, navigation.Uri);
    }

    [Fact]
    public async Task ApiRejection_AlsoShowsRecoveryWithoutWaitingForBrowserCallback()
    {
        var state = Services.GetRequiredService<WebCircuitSessionState>();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var cut = RenderComponent<WebSessionActivityMonitor>();

        await cut.InvokeAsync(() => state.End("SessionEnded"));

        cut.WaitForAssertion(() => Assert.Contains("Sign in", cut.Find("button").TextContent));
        Assert.Equal(originalUri, navigation.Uri);
    }

    [Theory]
    [InlineData("import", false)]
    [InlineData("start", false)]
    [InlineData("import", true)]
    [InlineData("start", true)]
    public async Task FailedStartup_RetriesOnConnectionUp_WithoutRestartingSuccessfulMonitor(string phase, bool canceled)
    {
        var runtime = new StartupRuntime();
        var failures = 0;
        runtime.BeforeInvoke = identifier => identifier == phase && failures++ == 0
            ? Task.FromException(canceled ? new TaskCanceledException() : new JSDisconnectedException("Disconnected"))
            : Task.CompletedTask;
        Services.AddSingleton<IJSRuntime>(runtime);
        using var handler = new WebSessionCircuitHandler(_authentication.Object, Services.GetRequiredService<WebCircuitSessionState>());
        var cut = RenderComponent<WebSessionActivityMonitor>();
        cut.WaitForAssertion(() => Assert.Equal(1, failures));
        Assert.Equal(0, runtime.SuccessfulStarts);

        await cut.InvokeAsync(() => handler.OnConnectionUpAsync(null!, CancellationToken.None));
        cut.WaitForAssertion(() => Assert.Equal(1, runtime.SuccessfulStarts));
        var calls = runtime.Calls.Count;
        await cut.InvokeAsync(() => handler.OnConnectionUpAsync(null!, CancellationToken.None));
        cut.Render();
        Assert.Equal(calls, runtime.Calls.Count);
        Assert.Null(Services.GetRequiredService<WebCircuitSessionState>().EndedReason);
        if (phase == "start")
        {
            var starts = runtime.Calls.Where(call => call.Identifier == "start").ToArray();
            Assert.Equal(2, starts.Length);
            Assert.Same(starts[0].Arguments![1], starts[1].Arguments![1]);
            Assert.Equal(starts[0].Arguments![2], starts[1].Arguments![2]);
        }
    }

    [Theory]
    [InlineData("import")]
    [InlineData("start")]
    public async Task ReconnectDuringPendingStartup_RetriesAfterFailure_WithoutConcurrentStart(string phase)
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StartupRuntime();
        var attempts = 0;
        runtime.BeforeInvoke = identifier => identifier == phase && attempts++ == 0 ? pending.Task : Task.CompletedTask;
        Services.AddSingleton<IJSRuntime>(runtime);
        using var handler = new WebSessionCircuitHandler(_authentication.Object, Services.GetRequiredService<WebCircuitSessionState>());
        var cut = RenderComponent<WebSessionActivityMonitor>();
        cut.WaitForAssertion(() => Assert.Equal(1, attempts));
        await cut.InvokeAsync(() => handler.OnConnectionUpAsync(null!, CancellationToken.None));
        await cut.InvokeAsync(() => handler.OnConnectionUpAsync(null!, CancellationToken.None));
        Assert.Equal(1, attempts);

        pending.SetException(new JSDisconnectedException("Lost startup acknowledgement"));

        cut.WaitForAssertion(() => Assert.Equal(1, runtime.SuccessfulStarts));
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStartup_DoesNotRetryAfterTerminationOrDisposal(bool dispose)
    {
        var runtime = new StartupRuntime { BeforeInvoke = _ => Task.FromException(new JSDisconnectedException("Disconnected")) };
        Services.AddSingleton<IJSRuntime>(runtime);
        var state = Services.GetRequiredService<WebCircuitSessionState>();
        using var handler = new WebSessionCircuitHandler(_authentication.Object, state);
        var cut = RenderComponent<WebSessionActivityMonitor>();
        cut.WaitForAssertion(() => Assert.Single(runtime.Calls));
        if (dispose)
        {
            // Dispose the component through the renderer, not only its bUnit wrapper.
            await cut.InvokeAsync(DisposeComponents);
            Assert.True(cut.IsDisposed);
        }
        else await cut.InvokeAsync(() => state.End("SessionEnded"));

        if (dispose) await handler.OnConnectionUpAsync(null!, CancellationToken.None);
        else await cut.InvokeAsync(() => handler.OnConnectionUpAsync(null!, CancellationToken.None));

        Assert.Single(runtime.Calls);
        Assert.Equal(0, runtime.SuccessfulStarts);
    }

    private sealed class StartupRuntime : IJSRuntime, IJSObjectReference
    {
        public List<(string Identifier, object?[]? Arguments)> Calls { get; } = [];
        public Func<string, Task> BeforeInvoke { get; set; } = _ => Task.CompletedTask;
        public int SuccessfulStarts { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args));
            await BeforeInvoke(identifier);
            if (identifier == "import") return (TValue)(object)this;
            if (identifier == "start") SuccessfulStarts++;
            return default!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
