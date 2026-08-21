using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.UI.Components.Settings;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class QuickAdminSettingsTests : TestContext
{
    [Fact]
    public void SettingsRoute_ReappliesQuickAdminSectionWhenRouteChanges()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("/settings/auto-check-in");

        var cut = RenderComponent<PTDoc.UI.Pages.Settings>();
        Assert.Contains("Auto Check-In Messaging", cut.Markup, StringComparison.Ordinal);

        navigation.NavigateTo("/settings/kiosk");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Kiosk Check-In", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Documentation &amp; Compliance", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AutoCheckIn_OwnerAccessIsReadOnly()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("owner-user");
        authorization.SetRoles(Roles.Owner);

        var cut = RenderComponent<AutoCheckInSettings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Owner access is read-only", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find(".quick-settings__primary").HasAttribute("disabled"));
            Assert.True(cut.Find("input[type='number']").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void KioskAdministration_OwnerAccessIsReadOnly()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("owner-user");
        authorization.SetRoles(Roles.Owner);

        var cut = RenderComponent<KioskAdministrationSettings>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Owner access is read-only", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
            Assert.True(cut.Find("input").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void KioskAdministration_ConfirmsCredentialRotationBeforeMutation()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var station = new KioskStationDto(Guid.NewGuid(), "Front Desk iPad", true, null, 3);
        var service = new Mock<IKioskCheckInService>();
        service.Setup(item => item.GetStationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([station]);
        Services.AddSingleton(service.Object);

        var cut = RenderComponent<KioskAdministrationSettings>();
        cut.WaitForElement(".kiosk-admin__stations article");
        cut.FindAll(".kiosk-admin__actions button")
            .Single(button => button.TextContent.Trim() == "Rotate Enrollment")
            .Click();

        Assert.Contains("Rotate station enrollment?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("invalidates its current device credential", cut.Markup, StringComparison.Ordinal);
        service.Verify(item => item.RotateEnrollmentAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        cut.Find(".kiosk-admin__dialog-actions button").Click();
        Assert.Empty(cut.FindAll(".kiosk-admin__dialog"));
    }

    [Fact]
    public void KioskAdministration_RenamesStationWithExpectedVersion()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var station = new KioskStationDto(Guid.NewGuid(), "Front Desk iPad", true, null, 3);
        var service = new Mock<IKioskCheckInService>();
        service.Setup(item => item.GetStationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([station]);
        service.Setup(item => item.UpdateStationAsync(
                It.IsAny<Guid>(), station.Id, It.IsAny<UpdateKioskStationRequest>(), It.IsAny<Guid>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingsOperationResult<KioskStationDto>.Success(station with { Name = "Lobby iPad", Version = 4 }));
        Services.AddSingleton(service.Object);

        var cut = RenderComponent<KioskAdministrationSettings>();
        cut.WaitForElement(".kiosk-admin__stations article");
        cut.FindAll(".kiosk-admin__actions button")[0].Click();
        cut.Find(".kiosk-admin__stations input").Change("Lobby iPad");
        cut.FindAll(".kiosk-admin__actions button")[0].Click();

        cut.WaitForAssertion(() => service.Verify(item => item.UpdateStationAsync(
            It.IsAny<Guid>(), station.Id,
            It.Is<UpdateKioskStationRequest>(request => request.Name == "Lobby iPad" && request.ExpectedVersion == 3),
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once));
    }

    [Fact]
    public void KioskAdministration_CreatesAppointmentTokenForValidId()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var appointmentId = Guid.NewGuid();
        var service = new Mock<IKioskCheckInService>();
        service.Setup(item => item.GetStationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        service.Setup(item => item.CreateCheckInTokenAsync(
                It.IsAny<Guid>(), appointmentId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingsOperationResult<KioskCheckInTokenDto>.Success(
                new KioskCheckInTokenDto(appointmentId, "12345678", "token-id.secret", DateTime.UtcNow.AddMinutes(10))));
        Services.AddSingleton(service.Object);

        var cut = RenderComponent<KioskAdministrationSettings>();
        cut.Find("#kiosk-appointment-id").Change(appointmentId.ToString());
        cut.Find(".kiosk-admin__token button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("12345678", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("token-id.secret", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AuthenticationStepUp_RecoveryCodeModeExplainsInvalidation()
    {
        var cut = RenderComponent<AuthenticationStepUp>();
        cut.Instance.Mode = "recovery-codes";
        cut.Render();

        Assert.Contains("Regenerate recovery codes", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("invalidates every previous recovery code", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Invalidate and regenerate codes", cut.Markup, StringComparison.Ordinal);
    }
}
