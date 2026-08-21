using Bunit;
using Bunit.TestDoubles;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class RolesPermissionsSettingsTests : TestContext
{
    public RolesPermissionsSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
    }

    [Fact]
    public void DefaultState_RendersDocumentedRolePermissionMatrix()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        Assert.Equal(30, cut.FindAll(".permission-row").Count);
        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: View']").GetAttribute("aria-checked"));
        Assert.Equal("0", cut.Find("button[aria-label='View Clinical Notes: View']").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("tabindex"));
        Assert.Equal("0", cut.Find("#role-permissions-tab").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("#security-settings-tab").GetAttribute("tabindex"));
        Assert.Null(cut.Find("#roles-settings-panel").GetAttribute("tabindex"));
        Assert.Equal("true", cut.Find("button[aria-label=\"Edit Others' Notes: View\"]").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Sign/Submit Notes: Full']").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Delete Draft Notes: None']").GetAttribute("aria-checked"));
        Assert.Contains("Remove unsigned draft clinical documentation", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "2", "9", "7", "12" },
            cut.FindAll(".permission-summary__metrics strong")
                .Select(metric => metric.TextContent)
                .ToArray());
    }

    [Fact]
    public void PermissionAndRoleSelections_UpdateVisibleUiState()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        cut.Find("button[aria-label='View Clinical Notes: None']").Click();
        cut.FindAll(".role-card")
            .Single(button => button.TextContent.Contains("Practice Manager", StringComparison.Ordinal))
            .Click();

        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("aria-checked"));
        Assert.Equal("0", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("button[aria-label='View Clinical Notes: View']").GetAttribute("tabindex"));
        Assert.Equal(
            "true",
            cut.FindAll(".role-card")
                .Single(button => button.TextContent.Contains("Practice Manager", StringComparison.Ordinal))
                .GetAttribute("aria-pressed"));
        Assert.Contains("Copy permissions from another role to Practice Manager", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityTab_RendersDocumentedStates_AndTogglesLocally()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        cut.Find("#security-settings-tab").Click();

        Assert.Equal("-1", cut.Find("#role-permissions-tab").GetAttribute("tabindex"));
        Assert.Equal("0", cut.Find("#security-settings-tab").GetAttribute("tabindex"));

        var switches = cut.FindAll("button[role='switch']");
        Assert.Equal(4, switches.Count);
        Assert.Equal("false", cut.Find("button[aria-label='Require Multi-Factor Authentication']").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal("15", cut.Find("#auto-lockout-time").GetAttribute("value"));
        Assert.Equal("8", cut.Find("#minimum-pin-length").GetAttribute("value"));
        Assert.Contains("Enabled · Locked", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Save Changes", cut.Find(".security-action-bar").TextContent, StringComparison.Ordinal);

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();

        Assert.Equal("true", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Null(cut.Find("#roles-settings-panel").GetAttribute("tabindex"));
    }

    [Fact]
    public void PermissionLevels_SupportStandardRadioGroupKeyboardNavigation()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        cut.Find("button[aria-label='View Clinical Notes: View']").KeyDown("ArrowLeft");

        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("aria-checked"));
        Assert.Equal("0", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("tabindex"));

        cut.Find("button[aria-label='View Clinical Notes: None']").KeyDown("ArrowDown");

        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: View']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Tabs_SupportKeyboardNavigationAndReferenceTheRenderedPanel()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        Assert.All(
            cut.FindAll("[role='tab']"),
            tab => Assert.Equal("roles-settings-panel", tab.GetAttribute("aria-controls")));
        Assert.Equal("role-permissions-tab", cut.Find("#roles-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#role-permissions-tab").KeyDown("ArrowRight");

        Assert.Equal("true", cut.Find("#security-settings-tab").GetAttribute("aria-selected"));
        Assert.Equal("0", cut.Find("#security-settings-tab").GetAttribute("tabindex"));
        Assert.Equal("security-settings-tab", cut.Find("#roles-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#security-settings-tab").KeyDown("Home");

        Assert.Equal("true", cut.Find("#role-permissions-tab").GetAttribute("aria-selected"));
        Assert.Equal("role-permissions-tab", cut.Find("#roles-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#role-permissions-tab").KeyDown("ArrowLeft");

        Assert.Equal("true", cut.Find("#security-settings-tab").GetAttribute("aria-selected"));
    }
}
