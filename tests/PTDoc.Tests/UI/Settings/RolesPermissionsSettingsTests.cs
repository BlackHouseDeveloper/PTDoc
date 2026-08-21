using Bunit;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class RolesPermissionsSettingsTests : TestContext
{
    [Fact]
    public void DefaultState_RendersDocumentedRolePermissionMatrix()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        Assert.Equal(30, cut.FindAll(".permission-row").Count);
        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: View']").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label=\"Edit Others' Notes: View\"]").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Sign/Submit Notes: Full']").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Delete Notes: None']").GetAttribute("aria-checked"));
        Assert.Contains("2", cut.Find(".permission-summary__metrics").TextContent, StringComparison.Ordinal);
        Assert.Contains("7", cut.Find(".permission-summary__metrics").TextContent, StringComparison.Ordinal);
        Assert.Contains("6", cut.Find(".permission-summary__metrics").TextContent, StringComparison.Ordinal);
        Assert.Contains("15", cut.Find(".permission-summary__metrics").TextContent, StringComparison.Ordinal);
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

        var switches = cut.FindAll("button[role='switch']");
        Assert.Equal(5, switches.Count);
        Assert.Equal("true", cut.Find("button[aria-label='Require Multi-Factor Authentication']").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal(string.Empty, cut.Find("#auto-lockout-time").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#password-expiration").GetAttribute("value"));
        Assert.Equal("8", cut.Find("#minimum-password-length").GetAttribute("value"));
        Assert.Contains("Save Changes", cut.Find(".security-action-bar").TextContent, StringComparison.Ordinal);

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();

        Assert.Equal("true", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
    }
}
