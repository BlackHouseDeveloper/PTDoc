using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClonePermissions_SendsEditableTargetVersions_AndPreservesStateOnConflict(bool conflict)
    {
        RolePermissionItem[] targetPermissions =
        [
            new(CapabilityKey.ClinicalNotesView, "View Clinical Notes", "", PermissionLevel.View, PermissionLevel.None, true, 0),
            new(CapabilityKey.AppointmentsCreate, "Create Appointments", "", PermissionLevel.Edit, PermissionLevel.None, true, 7),
            new(CapabilityKey.ClinicalNotesSign, "Sign/Submit Notes", "", PermissionLevel.Full, PermissionLevel.Full, true, 3),
            new(CapabilityKey.ClinicalNotesDelete, "Delete Notes", "", PermissionLevel.None, PermissionLevel.None, false, 4)
        ];
        var target = new RolePermissionSet(Roles.PT, "PT / DPT", false, targetPermissions, 1, 1, 1, 1);
        var source = target with
        {
            RoleKey = Roles.PTA,
            DisplayName = "PTA",
            Permissions = target.Permissions.Select(permission => permission with { Version = 99 }).ToArray()
        };
        var updated = target with
        {
            Permissions = target.Permissions.Select(permission =>
                permission.IsSupported && permission.LockedMinimum == PermissionLevel.None
                    ? permission with { Level = PermissionLevel.None, Version = permission.Version + 1 }
                    : permission).ToArray()
        };
        var requests = new List<CloneRolePermissionsRequest>();
        var roles = new Mock<IRolePermissionAdministrationService>();
        roles.Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RolePermissionsResponse([target, source], AuthorizationRolloutMode.Static));
        roles.Setup(service => service.CloneAsync(
                It.IsAny<Guid>(), Roles.PT, It.IsAny<CloneRolePermissionsRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CloneRolePermissionsRequest, Guid, string, CancellationToken>(
                (_, _, request, _, _, _) => requests.Add(request))
            .ReturnsAsync(conflict
                ? SettingsOperationResult<RolePermissionSet>.Conflict()
                : SettingsOperationResult<RolePermissionSet>.Success(updated));
        var security = new Mock<ISecurityPolicyAdministrationService>();
        security.Setup(service => service.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityPolicyDto(
                MfaEnforcementMode.Off, null, true, 8, 15, true, false, AuthorizationRolloutMode.Static, true, 1));
        Services.AddSingleton(roles.Object);
        Services.AddSingleton(security.Object);

        var cut = RenderComponent<RolesPermissionsSettings>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".role-card").Count));
        cut.Find("#clone-role-select").Change(Roles.PTA);
        cut.Find(".clone-permissions__actions button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(requests);
            Assert.Equal(Roles.PTA, requests[0].SourceRoleKey);
            Assert.Equal(new[]
            {
                new PermissionVersionExpectation(CapabilityKey.ClinicalNotesView, 0),
                new PermissionVersionExpectation(CapabilityKey.AppointmentsCreate, 7)
            }, requests[0].TargetPermissions);
            Assert.Equal("true", cut.Find(conflict
                ? "button[aria-label='View Clinical Notes: View']"
                : "button[aria-label='View Clinical Notes: None']").GetAttribute("aria-checked"));
            Assert.Contains(conflict ? "changed in another session" : "Editable permissions were cloned", cut.Markup, StringComparison.Ordinal);
        });

        // A second attempt uses the returned versions only after a successful clone.
        cut.Find("#clone-role-select").Change(Roles.PTA);
        cut.Find(".clone-permissions__actions button").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, requests.Count);
            Assert.Equal(new[]
            {
                new PermissionVersionExpectation(CapabilityKey.ClinicalNotesView, conflict ? 0 : 1),
                new PermissionVersionExpectation(CapabilityKey.AppointmentsCreate, conflict ? 7 : 8)
            }, requests[1].TargetPermissions);
        });
    }
}
