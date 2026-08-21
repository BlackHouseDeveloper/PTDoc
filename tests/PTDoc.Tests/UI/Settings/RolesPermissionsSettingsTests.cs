using Bunit;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class RolesPermissionsSettingsTests : TestContext
{
    public RolesPermissionsSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
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
            ["2", "9", "7", "12"],
            cut.FindAll(".permission-summary__metrics strong").Select(metric => metric.TextContent).ToArray());
    }

    [Fact]
    public void PermissionPreview_IsLimitedToDocumentedPtDptBaselineAndSummaryRemainsLive()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        cut.Find("button[aria-label='View Clinical Notes: None']").Click();
        Assert.Contains("Permission Summary for PT / DPT", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(
            ["2", "9", "6", "13"],
            cut.FindAll(".permission-summary__metrics strong").Select(metric => metric.TextContent).ToArray());

        var roleCards = cut.FindAll(".role-card");
        Assert.Equal(7, roleCards.Count(button => button.HasAttribute("disabled")));
        Assert.False(roleCards.Single(button => button.TextContent.Contains("PT / DPT", StringComparison.Ordinal)).HasAttribute("disabled"));
        Assert.True(roleCards.Single(button => button.TextContent.Contains("Practice Manager", StringComparison.Ordinal)).HasAttribute("disabled"));
        Assert.Equal(7, cut.FindAll(".role-card__description small").Count);
        Assert.True(cut.Find("#clone-role-select").HasAttribute("disabled"));
        Assert.True(cut.Find(".clone-permissions button").HasAttribute("disabled"));
        Assert.Contains("only documented permission baseline", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("remain local to this page and are not persisted", cut.Markup, StringComparison.Ordinal);
        Assert.All(cut.FindAll(".quick-admin__items > button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.Equal(2, cut.FindAll(".quick-admin__items small").Count(item => item.TextContent.Contains("Preview unavailable", StringComparison.Ordinal)));
        Assert.Contains("Permission Summary for PT / DPT", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecurityTab_RendersDocumentedStates_AndTogglesLocally()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();

        cut.Find("#security-settings-tab").Click();

        Assert.Equal("-1", cut.Find("#role-permissions-tab").GetAttribute("tabindex"));
        Assert.Equal("0", cut.Find("#security-settings-tab").GetAttribute("tabindex"));

        var switches = cut.FindAll("button[role='switch']");
        Assert.Equal(5, switches.Count);
        Assert.Equal("true", cut.Find("button[aria-label='Require Multi-Factor Authentication']").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal(string.Empty, cut.Find("#auto-lockout-time").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#password-expiration").GetAttribute("value"));
        Assert.Equal("8", cut.Find("#minimum-password-length").GetAttribute("value"));
        Assert.Contains("Save Preview", cut.Find(".security-action-bar").TextContent, StringComparison.Ordinal);
        Assert.Contains("are not persisted", cut.Find("#security-preview-status").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            "security-preview-status",
            cut.FindAll(".security-action-bar button")
                .Single(button => button.TextContent.Contains("Save Preview", StringComparison.Ordinal))
                .GetAttribute("aria-describedby"));

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();

        Assert.Equal("true", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Null(cut.Find("#roles-settings-panel").GetAttribute("tabindex"));

        cut.Find(".manage-roles-row").Click();

        Assert.Equal("true", cut.Find("#role-permissions-tab").GetAttribute("aria-selected"));
        Assert.Equal("0", cut.Find("#role-permissions-tab").GetAttribute("tabindex"));
    }

    [Fact]
    public void SecurityActions_ResetDefaultsAndCancelToLastSavedSnapshot()
    {
        var cut = RenderComponent<RolesPermissionsSettings>();
        cut.Find("#security-settings-tab").Click();

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();
        cut.Find("#auto-lockout-time").Input("30");
        cut.FindAll(".security-action-bar button")
            .Single(button => button.TextContent.Contains("Cancel", StringComparison.Ordinal))
            .Click();

        Assert.Equal("false", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal(string.Empty, cut.Find("#auto-lockout-time").GetAttribute("value"));

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();
        cut.Find("#auto-lockout-time").Input("30");
        cut.FindAll(".security-action-bar button")
            .Single(button => button.TextContent.Contains("Save Preview", StringComparison.Ordinal))
            .Click();
        Assert.Contains("Security preview baseline updated", cut.Find("#security-preview-status").TextContent, StringComparison.Ordinal);

        cut.Find("button[aria-label='Restrict Schedule Access']").Click();
        cut.Find("#auto-lockout-time").Input("45");
        cut.FindAll(".security-action-bar button")
            .Single(button => button.TextContent.Contains("Cancel", StringComparison.Ordinal))
            .Click();

        Assert.Equal("true", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal("30", cut.Find("#auto-lockout-time").GetAttribute("value"));
        Assert.Contains("last saved security preview was restored", cut.Find("#security-preview-status").TextContent, StringComparison.Ordinal);

        cut.FindAll(".security-action-bar button")
            .Single(button => button.TextContent.Contains("Reset to Default", StringComparison.Ordinal))
            .Click();

        Assert.Equal("false", cut.Find("button[aria-label='Restrict Schedule Access']").GetAttribute("aria-checked"));
        Assert.Equal(string.Empty, cut.Find("#auto-lockout-time").GetAttribute("value"));
        Assert.Contains("Canonical security defaults applied", cut.Find("#security-preview-status").TextContent, StringComparison.Ordinal);
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

        cut.Find("button[aria-label='View Clinical Notes: View']").KeyDown("Home");

        Assert.Equal("true", cut.Find("button[aria-label='View Clinical Notes: None']").GetAttribute("aria-checked"));

        cut.Find("button[aria-label='View Clinical Notes: None']").KeyDown("End");

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
