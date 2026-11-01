using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.NoteTemplates;
using PTDoc.Application.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsPageAuthorizationTests : TestContext
{
    [Theory]
    [InlineData(Roles.Admin, "/settings/auto-check-in", "Auto Check-In Messaging")]
    [InlineData(Roles.Admin, "/settings/kiosk", "Kiosk Check-In")]
    [InlineData(Roles.Owner, "/settings/kiosk", "Kiosk Check-In")]
    public async Task InitialRoute_IsAppliedAfterDeferredAuthorization(string role, string route, string heading)
    {
        var authentication = new DeferredAuthenticationStateProvider();
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo(route);
        var cut = RenderComponent<global::PTDoc.UI.Pages.Settings>();
        Assert.Contains("Loading settings", cut.Markup, StringComparison.Ordinal);
        await cut.InvokeAsync(() => authentication.Complete(role));
        cut.WaitForAssertion(() => Assert.Equal(heading, cut.Find(".settings-main-content h1").TextContent));
    }

    [Fact]
    public async Task PendingAuthentication_DoesNotRenderAdministrativeSettingsForPtUser()
    {
        var authentication = new DeferredAuthenticationStateProvider();
        var templates = new Mock<INoteTemplateAdministrationService>(MockBehavior.Strict);
        templates
            .Setup(service => service.ListForClinicalReviewAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NoteTemplateSummaryDto>());

        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton(templates.Object);
        Services.AddSingleton(Mock.Of<IToastService>());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Settings>();

        Assert.Contains("Loading settings", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Roles & Permissions", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduling & Visit Types", cut.Markup, StringComparison.Ordinal);

        await cut.InvokeAsync(() => authentication.Complete(Roles.PT));

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading settings", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Documentation Templates", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Roles & Permissions", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Scheduling & Visit Types", cut.Markup, StringComparison.Ordinal);
        });
    }

    private sealed class DeferredAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly TaskCompletionSource<AuthenticationState> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => completion.Task;

        public void Complete(string role)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test-user"), new Claim(ClaimTypes.Role, role)],
                "test");
            completion.TrySetResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
