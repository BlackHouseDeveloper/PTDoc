using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using PTDoc.Application.Identity;
using PTDoc.Web.Auth;

namespace PTDoc.Tests.Identity;

public sealed class WebUserServiceTests
{
    [Fact]
    public async Task LoginAsync_UsesLocalFormSubmission_WhenExternalIdentityIsEnabled()
    {
        var jsRuntime = new RecordingJsRuntime();
        var navigationManager = new TestNavigationManager();
        navigationManager.InitializeForTest("https://localhost/", "https://localhost/login");

        var service = CreateService(jsRuntime, navigationManager, externalIdentityEnabled: true);

        var result = await service.LoginAsync("alice", "1234", "/patients");

        Assert.True(result);
        Assert.Equal("ptdocAuth.submitLogin", jsRuntime.LastIdentifier);
        Assert.Equal(new object?[] { "alice", "1234", "/patients" }, jsRuntime.LastArguments);
        Assert.Equal("https://localhost/login", navigationManager.Uri);
    }

    [Fact]
    public async Task BeginExternalLoginAsync_NavigatesToExplicitExternalEndpoint()
    {
        var jsRuntime = new RecordingJsRuntime();
        var navigationManager = new TestNavigationManager();
        navigationManager.InitializeForTest("https://localhost/", "https://localhost/login");

        var service = CreateService(jsRuntime, navigationManager, externalIdentityEnabled: true);

        var started = await service.BeginExternalLoginAsync("/patients");

        Assert.True(started);
        Assert.Equal("https://localhost/auth/external/start?returnUrl=%2Fpatients", navigationManager.Uri);
    }

    [Fact]
    public async Task BeginExternalLoginAsync_ReturnsFalse_WhenExternalIdentityIsDisabled()
    {
        var jsRuntime = new RecordingJsRuntime();
        var navigationManager = new TestNavigationManager();
        navigationManager.InitializeForTest("https://localhost/", "https://localhost/login");

        var service = CreateService(jsRuntime, navigationManager, externalIdentityEnabled: false);

        var started = await service.BeginExternalLoginAsync("/patients");

        Assert.False(started);
        Assert.Equal("https://localhost/login", navigationManager.Uri);
    }

    [Fact]
    public void SupportsSelfServiceRegistration_RemainsEnabled_WhenExternalIdentityIsConfigured()
    {
        var service = CreateService(new RecordingJsRuntime(), CreateNavigationManager(), externalIdentityEnabled: true);

        Assert.True(service.SupportsExternalIdentityLogin);
        Assert.True(service.SupportsSelfServiceRegistration);
        Assert.Equal("Microsoft", service.IdentityProviderDisplayName);
    }

    private static WebUserService CreateService(
        IJSRuntime jsRuntime,
        NavigationManager navigationManager,
        bool externalIdentityEnabled)
    {
        return new WebUserService(
            jsRuntime,
            NullLogger<WebUserService>.Instance,
            navigationManager,
            Options.Create(new EntraExternalIdOptions
            {
                Enabled = externalIdentityEnabled,
                DisplayName = "Microsoft"
            }));
    }

    private static TestNavigationManager CreateNavigationManager()
    {
        var navigationManager = new TestNavigationManager();
        navigationManager.InitializeForTest("https://localhost/", "https://localhost/login");
        return navigationManager;
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public string? LastIdentifier { get; private set; }

        public object?[] LastArguments { get; private set; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            LastIdentifier = identifier;
            LastArguments = args ?? [];
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public void InitializeForTest(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}