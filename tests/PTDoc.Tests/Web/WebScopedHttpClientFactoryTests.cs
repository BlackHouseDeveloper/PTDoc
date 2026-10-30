using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;
using PTDoc.Web.Components;

namespace PTDoc.Tests.Web;

[Trait("Category", "CoreCi")]
public sealed class WebScopedHttpClientFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedClients_IsolateRejectedUsers_WhileSharingTransport(bool requestScope)
    {
        await using var host = new FactoryHost();
        using var rejectedScope = host.Services.CreateScope();
        using var validScope = host.Services.CreateScope();
        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        var rejected = Principal("rejected-token");
        var valid = Principal("valid-token");
        SetPrincipal(rejectedScope.ServiceProvider, rejected);
        SetPrincipal(validScope.ServiceProvider, valid);
        var firstState = rejectedScope.ServiceProvider.GetRequiredService<WebCircuitSessionState>();
        var secondState = validScope.ServiceProvider.GetRequiredService<WebCircuitSessionState>();
        Assert.NotSame(firstState, secondState);
        using var first = rejectedScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
        using var second = validScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
        try
        {
            accessor.HttpContext = requestScope ? new DefaultHttpContext { User = rejected, RequestServices = rejectedScope.ServiceProvider } : null;
            using var denied = await first.GetAsync("/diagnostics/development/communications");
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal("SessionEnded", firstState.EndedReason);
            Assert.Null(secondState.EndedReason);

            // Another named client in A's scope must share A's terminal state.
            using var sameScope = rejectedScope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
            using var blocked = await sameScope.PostAsync("/api/v1/notes", new StringContent("draft"));
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
            Assert.Single(host.Transport.Tokens);

            // Disposal of A's wrappers/scope must not dispose B's pooled transport.
            first.Dispose();
            sameScope.Dispose();
            rejectedScope.Dispose();
            accessor.HttpContext = requestScope ? new DefaultHttpContext { User = valid, RequestServices = validScope.ServiceProvider } : null;
            using var diagnostics = await second.GetAsync("/diagnostics/development/communications");
            using var recovery = await second.PostAsync("/api/v1/auth/mfa/recovery-codes/regenerate", new StringContent("{}"));
            Assert.Equal(HttpStatusCode.OK, diagnostics.StatusCode);
            Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
            Assert.Null(secondState.EndedReason);
            Assert.Equal(new[] { "rejected-token", "valid-token", "valid-token" }, host.Transport.Tokens);
            Assert.Equal(1, host.TransportCreations);
            Assert.False(host.Transport.Disposed);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Fact]
    public async Task AuthApiClient_RemainsUsableForRecoveryInEndedScope()
    {
        await using var host = new FactoryHost();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<WebCircuitSessionState>().End("SessionEnded");
        using var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("PTDocAuthApi");

        using var response = await client.PostAsync("/api/v1/auth/pin-login", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(client.BaseAddress);
        Assert.Null(Assert.Single(host.AuthTransport.Tokens));
        client.Dispose();
        Assert.False(host.AuthTransport.Disposed);
    }

    [Theory]
    [InlineData("SessionEnded", "Sign in")]
    [InlineData("SessionChanged", "Reload session")]
    public async Task ActivityCallback_BlocksSiblingComponentAndNamedClient_WithoutLosingDraft(string reason, string action)
    {
        await using var host = new FactoryHost();
        using var scope = host.Services.CreateScope();
        SetPrincipal(scope.ServiceProvider, Principal("original"));
        await OpenCircuitAsync(scope.ServiceProvider);
        using var ui = CreateCircuitRenderer(scope.ServiceProvider);
        var monitor = ui.RenderComponent<WebSessionActivityMonitor>();
        var draft = ui.RenderComponent<DraftProbe>();
        var navigation = ui.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        draft.Find("textarea").Change("unsaved original draft");
        await draft.InvokeAsync(draft.Instance.ReadAsync);
        Assert.Equal(new[] { "original" }, host.Transport.Tokens);

        // Same callback invoked by the real JavaScript module after activity 401/409.
        await monitor.InvokeAsync(() => monitor.Instance.SessionEnded(reason));
        await draft.InvokeAsync(draft.Instance.SaveAsync);
        await draft.InvokeAsync(draft.Instance.ReadAsync);
        using var named = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ServerAPI");
        using var blocked = await named.GetAsync("/diagnostics/development/communications");

        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, draft.Instance.Status);
        Assert.Single(host.Transport.Tokens);
        Assert.Equal("unsaved original draft", draft.Instance.Draft);
        Assert.Equal(originalUri, navigation.Uri);
        monitor.WaitForAssertion(() => Assert.Equal(action, monitor.Find("button").TextContent));

        // A second circuit still works over the same transport.
        using var otherScope = host.Services.CreateScope();
        SetPrincipal(otherScope.ServiceProvider, Principal("other"));
        await OpenCircuitAsync(otherScope.ServiceProvider);
        using var other = otherScope.ServiceProvider.GetRequiredService<HttpClient>();
        using var allowed = await other.GetAsync("/api/v1/notes");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(new[] { "original", "other" }, host.Transport.Tokens);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task ComponentApiResponse_UpdatesSiblingMonitor_OnlyForAuthenticationFailure(HttpStatusCode status, bool ended)
    {
        await using var host = new FactoryHost();
        using var scope = host.Services.CreateScope();
        SetPrincipal(scope.ServiceProvider, Principal("original"));
        await OpenCircuitAsync(scope.ServiceProvider);
        using var ui = CreateCircuitRenderer(scope.ServiceProvider);
        var monitor = ui.RenderComponent<WebSessionActivityMonitor>();
        var draft = ui.RenderComponent<DraftProbe>();
        draft.Find("textarea").Change("keep this draft");
        var navigation = ui.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        host.Transport.Status = status;

        await draft.InvokeAsync(draft.Instance.SaveAsync);
        Assert.Equal(status, draft.Instance.Status);
        if (ended)
            monitor.WaitForAssertion(() => Assert.Equal("Sign in", monitor.Find("button").TextContent));
        else
            Assert.Empty(monitor.FindAll("[role=alert]"));
        host.Transport.Status = HttpStatusCode.OK;
        await draft.InvokeAsync(draft.Instance.ReadAsync);
        Assert.Equal(ended ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, draft.Instance.Status);
        Assert.Equal(ended ? 1 : 2, host.Transport.Tokens.Count);
        Assert.Equal("keep this draft", draft.Instance.Draft);
        Assert.Equal(originalUri, navigation.Uri);
    }

    [Theory]
    [InlineData("replacement", true, "Reload session")]
    [InlineData("replacement", false, "Reload session")]
    [InlineData("anonymous", true, "Sign in")]
    [InlineData("original", true, null)]
    public async Task ReconnectedPrincipal_CannotAuthorizeRetainedDraft(string reconnect, bool notify, string? action)
    {
        await using var host = new FactoryHost();
        using var scope = host.Services.CreateScope();
        var original = Principal("original");
        SetPrincipal(scope.ServiceProvider, original);
        await OpenCircuitAsync(scope.ServiceProvider);
        using var ui = CreateCircuitRenderer(scope.ServiceProvider);
        var monitor = ui.RenderComponent<WebSessionActivityMonitor>();
        var draft = ui.RenderComponent<DraftProbe>();
        draft.Find("textarea").Change("retained draft");
        var navigation = ui.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var authentication = (ScopedAuthentication)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var updated = reconnect == "anonymous" ? new ClaimsPrincipal(new ClaimsIdentity()) : Principal(reconnect);
        // Model ComponentHub.ConnectCircuit -> SetCircuitUser while retaining components.
        // The no-notification case verifies the outbound guard independently of the event.
        await monitor.InvokeAsync(() => authentication.Replace(updated, notify));
        if (notify && action is not null)
            monitor.WaitForAssertion(() => Assert.Equal(action, monitor.Find("button").TextContent));

        var accessor = host.Services.GetRequiredService<IHttpContextAccessor>();
        try
        {
            // A stale handshake principal must not mask the current circuit identity.
            accessor.HttpContext = new DefaultHttpContext { User = original, RequestServices = scope.ServiceProvider };
            await draft.InvokeAsync(draft.Instance.SaveAsync);
            Assert.Equal(action is null ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, draft.Instance.Status);
            if (action is not null)
            {
                Assert.Empty(host.Transport.Tokens);
                monitor.WaitForAssertion(() => Assert.Equal(action, monitor.Find("button").TextContent));
                // A late callback cannot undo the established terminal reason.
                await monitor.InvokeAsync(() => monitor.Instance.SessionEnded("SessionEnded"));
                Assert.Equal(action, monitor.Find("button").TextContent);
            }
            else
            {
                Assert.Equal("original", Assert.Single(host.Transport.Tokens));
                Assert.Empty(monitor.FindAll("[role=alert]"));
            }
            Assert.Equal("retained draft", draft.Instance.Draft);
            Assert.Equal(originalUri, navigation.Uri);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task OpenCircuitAsync(IServiceProvider services)
    {
        var handler = Assert.Single(services.GetServices<CircuitHandler>().OfType<WebSessionCircuitHandler>());
        // The handler does not inspect Circuit; emulate the framework lifecycle with
        // production DI registration, without constructing an internal CircuitHost.
        await handler.OnCircuitOpenedAsync(null!, CancellationToken.None);
    }

    [Theory]
    [InlineData("replacement", "SessionChanged")]
    [InlineData("anonymous", "SessionEnded")]
    [InlineData("original", null)]
    public async Task HostAuthenticationUpdate_PreservesAuthorizedEditor_WhileDenyingChangedIdentity(string replacement, string? reason)
    {
        await using var host = new FactoryHost(useHostAuthentication: true);
        using var scope = host.Services.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var hostAuthentication = Assert.IsAssignableFrom<IHostEnvironmentAuthenticationStateProvider>(authentication);
        hostAuthentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(Principal("original"))));
        await OpenCircuitAsync(scope.ServiceProvider);
        using var ui = CreateCircuitRenderer(scope.ServiceProvider);
        ui.Services.AddAuthorizationCore();
        var monitor = ui.RenderComponent<WebSessionActivityMonitor>();
        var editor = ui.RenderComponent<CascadingAuthenticationState>(parameters => parameters
            .AddChildContent<AuthorizeView>(view => view
                .Add(component => component.Authorized, _ => builder =>
                {
                    builder.OpenComponent<DraftProbe>(0);
                    builder.CloseComponent();
                })
                .Add(component => component.NotAuthorized, _ => builder => builder.AddContent(0, "Editor removed"))));
        var draft = editor.FindComponent<DraftProbe>();
        draft.Find("textarea").Change("authorized retained draft");
        var updated = replacement == "anonymous" ? new ClaimsPrincipal(new ClaimsIdentity()) : Principal(replacement);

        await editor.InvokeAsync(() => hostAuthentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(updated))));

        Assert.Equal(reason, scope.ServiceProvider.GetRequiredService<WebCircuitSessionState>().EndedReason);
        Assert.Same(draft.Instance, editor.FindComponent<DraftProbe>().Instance);
        Assert.Equal("authorized retained draft", draft.Instance.Draft);
        Assert.Equal("original", (await authentication.GetAuthenticationStateAsync()).User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value);
        await draft.InvokeAsync(draft.Instance.SaveAsync);
        Assert.Equal(reason is null ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, draft.Instance.Status);
        Assert.Equal(reason is null ? 1 : 0, host.Transport.Tokens.Count);
        if (reason is not null)
            monitor.WaitForAssertion(() => Assert.Equal(reason == "SessionChanged" ? "Reload session" : "Sign in", monitor.Find("button").TextContent));
    }

    private static Bunit.TestContext CreateCircuitRenderer(IServiceProvider services)
    {
        var ui = new Bunit.TestContext();
        ui.Services.AddSingleton(services.GetRequiredService<AuthenticationStateProvider>());
        ui.Services.AddSingleton(services.GetRequiredService<WebCircuitSessionState>());
        ui.Services.AddSingleton(services.GetRequiredService<HttpClient>());
        var module = ui.JSInterop.SetupModule("./js/sessionActivity.js");
        module.SetupVoid("start", _ => true).SetVoidResult();
        module.SetupVoid("stop").SetVoidResult();
        return ui;
    }

    public sealed class DraftProbe : ComponentBase
    {
        [Inject] public HttpClient Client { get; set; } = default!;
        public string Draft { get; private set; } = "";
        public HttpStatusCode Status { get; private set; }

        public async Task ReadAsync()
        {
            using var response = await Client.GetAsync("/api/v1/notes");
            Status = response.StatusCode;
        }

        public async Task SaveAsync()
        {
            using var response = await Client.PostAsync("/api/v1/notes", new StringContent(Draft));
            Status = response.StatusCode;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "textarea");
            builder.AddAttribute(1, "value", Draft);
            builder.AddAttribute(2, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, args => Draft = args.Value?.ToString() ?? ""));
            builder.CloseElement();
        }
    }

    private static ClaimsPrincipal Principal(string token) => new(new ClaimsIdentity(new[]
    {
        new Claim(PTDocClaimTypes.AuthenticationType, "web_cookie"),
        new Claim(PTDocClaimTypes.ApiAccessToken, token),
        new Claim(PTDocClaimTypes.ApiAccessTokenExpiresAt, DateTimeOffset.UtcNow.AddHours(1).ToString("O")),
        new Claim("ptdoc_web_session_id", token + "-session"),
        new Claim("ptdoc_web_session_started_at", DateTimeOffset.UtcNow.ToString("O")),
        new Claim("ptdoc_web_activity_ticket", "1")
    }, "Cookies"));

    private static void SetPrincipal(IServiceProvider services, ClaimsPrincipal principal) =>
        ((ScopedAuthentication)services.GetRequiredService<AuthenticationStateProvider>()).Principal = principal;

    private sealed class ScopedAuthentication : AuthenticationStateProvider
    {
        public ClaimsPrincipal Principal { get; set; } = new(new ClaimsIdentity());
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(Principal));
        public void Replace(ClaimsPrincipal principal, bool notify)
        {
            Principal = principal;
            if (notify) NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }

    private sealed class FactoryHost(bool useHostAuthentication = false) : WebApplicationFactory<PTDoc.Web.Components.App>
    {
        public RecordingTransport Transport { get; } = new();
        public RecordingTransport AuthTransport { get; } = new();
        public int TransportCreations { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                if (!useHostAuthentication) services.AddScoped<AuthenticationStateProvider>(_ => new ScopedAuthentication());
                services.AddScoped<IUserService>(_ => new Mock<IUserService>(MockBehavior.Strict).Object);
                services.AddHttpClient("PTDocAuthApi").ConfigurePrimaryHttpMessageHandler(() => AuthTransport);
                services.AddHttpClient("ServerAPI").ConfigurePrimaryHttpMessageHandler(() =>
                {
                    TransportCreations++;
                    return Transport;
                });
            });
        }
    }

    private sealed class RecordingTransport : HttpMessageHandler
    {
        public List<string?> Tokens { get; } = [];
        public bool Disposed { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(Disposed);
            var token = request.Headers.Authorization?.Parameter;
            Tokens.Add(token);
            return Task.FromResult(new HttpResponseMessage(token == "rejected-token" ? HttpStatusCode.Unauthorized : Status));
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
