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
    public QuickAdminSettingsTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutoCheckIn_FailedLoadDisablesWritesUntilBothResourcesReload(bool failPolicy)
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var policy = new AutoCheckInPolicyDto(true, 48, false, true, "default-intake-invite", 2, [], 8);
        var service = new Mock<IAutoCheckInAdministrationService>();
        var scheduling = new Mock<ISchedulingAdministrationService>();
        var retry = false;
        service.Setup(item => item.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() => failPolicy && !retry ? Task.FromException<AutoCheckInPolicyDto>(new HttpRequestException()) : Task.FromResult(policy));
        scheduling.Setup(item => item.GetVisitTypesAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .Returns(() => !failPolicy && !retry
                ? Task.FromException<IReadOnlyList<VisitTypeDto>>(new HttpRequestException())
                : Task.FromResult<IReadOnlyList<VisitTypeDto>>([]));
        UpdateAutoCheckInPolicyRequest? saved = null;
        service.Setup(item => item.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateAutoCheckInPolicyRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateAutoCheckInPolicyRequest, Guid, string, CancellationToken>((_, request, _, _, _) => saved = request)
            .ReturnsAsync(SettingsOperationResult<AutoCheckInPolicyDto>.Success(policy));
        Services.AddSingleton(service.Object);
        Services.AddSingleton(scheduling.Object);

        var cut = RenderComponent<AutoCheckInSettings>();
        Assert.Contains("could not be loaded", cut.Markup, StringComparison.Ordinal);
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));
        Assert.True(cut.Find(".quick-settings__primary").HasAttribute("disabled"));
        cut.Find(".quick-settings__primary").Click();
        Assert.Null(saved);

        retry = true;
        cut.Find(".quick-settings__reload").Click();
        Assert.False(cut.Find(".quick-settings__primary").HasAttribute("disabled"));
        cut.Find(".quick-settings__primary").Click();
        cut.WaitForAssertion(() => Assert.NotNull(saved));
        Assert.Equal(8, saved!.ExpectedVersion);
        Assert.Equal(48, saved.LeadHours);
        Assert.True(saved.IsEnabled);
        Assert.False(saved.EnableEmail);
    }

    [Fact]
    public void AutoCheckIn_RemovesHiddenIneligibleSelectionsOnLoadAndCancel()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var eligible = new VisitTypeDto(Guid.NewGuid(), "eval", "Evaluation", 45, true, false, true, true, 0, 1);
        var inactive = eligible with { Id = Guid.NewGuid(), IsActive = false };
        var noIntake = eligible with { Id = Guid.NewGuid(), RequiresIntake = false };
        var policy = new AutoCheckInPolicyDto(true, 24, true, true, "default", 3, [eligible.Id, inactive.Id, noIntake.Id, Guid.NewGuid()], 2);
        var scheduling = new Mock<ISchedulingAdministrationService>();
        scheduling.Setup(service => service.GetVisitTypesAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([eligible, inactive, noIntake]);
        var service = new Mock<IAutoCheckInAdministrationService>();
        service.Setup(item => item.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        UpdateAutoCheckInPolicyRequest? saved = null;
        service.Setup(item => item.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateAutoCheckInPolicyRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateAutoCheckInPolicyRequest, Guid, string, CancellationToken>((_, request, _, _, _) => saved = request)
            .ReturnsAsync(SettingsOperationResult<AutoCheckInPolicyDto>.Success(policy with { EligibleVisitTypeIds = [eligible.Id] }));
        Services.AddSingleton(service.Object);
        Services.AddSingleton(scheduling.Object);
        var cut = RenderComponent<AutoCheckInSettings>();
        Assert.Single(cut.FindAll("fieldset:last-child input"));
        cut.Find("fieldset:last-child input").Change(false);
        cut.Find(".quick-settings__actions button").Click();
        cut.Find(".quick-settings__primary").Click();
        cut.WaitForAssertion(() => Assert.NotNull(saved));
        Assert.Equal(new[] { eligible.Id }, saved!.EligibleVisitTypeIds);
    }

    [Fact]
    public async Task KioskConfirmation_UsesModalHelperAndDismissesThroughEscape()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
        var modal = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        modal.Mode = JSRuntimeMode.Loose;
        var station = new KioskStationDto(Guid.NewGuid(), "Lobby", true, null, 2);
        var service = new Mock<IKioskCheckInService>();
        service.Setup(item => item.GetStationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([station]);
        Services.AddSingleton(service.Object);
        var cut = RenderComponent<KioskAdministrationSettings>();
        cut.FindAll(".kiosk-admin__actions button").Single(button => button.TextContent == "Rotate Enrollment").Click();
        cut.WaitForAssertion(() => Assert.Contains(modal.Invocations, call => call.Identifier == "registerEscapeHandler" && Equals(call.Arguments[0], "kiosk-confirmation")));
        Assert.Equal("Cancel", cut.Find("#kiosk-confirmation button").TextContent);
        await cut.InvokeAsync(() => cut.Instance.CloseFromJs());
        cut.WaitForAssertion(() => Assert.Contains(modal.Invocations, call => call.Identifier == "unregisterEscapeHandler"));
        Assert.Empty(cut.FindAll(".kiosk-admin__dialog"));
    }

    [Fact]
    public void KioskReEnrollment_PreservesCredentialUntilAdminIssuedCodeIsAccepted()
    {
        JSInterop.Setup<string?>("localStorage.getItem", "ptdoc.kiosk.deviceCredential").SetResult("old-device-credential");
        var service = new Mock<IKioskCheckInService>();
        service.Setup(item => item.CheckInAsync("old-device-credential", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingsOperationResult<KioskCheckInResult>.NotFound());
        service.Setup(item => item.EnrollAsync("replacement-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingsOperationResult<KioskEnrollmentResult>.Success(new KioskEnrollmentResult(Guid.NewGuid(), "Lobby", "replacement-credential")));
        service.Setup(item => item.EnrollAsync("invalid-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SettingsOperationResult<KioskEnrollmentResult>.NotFound());
        Services.AddSingleton(service.Object);
        var cut = RenderComponent<KioskCheckIn>();
        cut.WaitForElement("button[type='button']");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("invalid, expired", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier == "localStorage.removeItem");
        cut.Find("button[type='button']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Replace station enrollment", cut.Markup, StringComparison.Ordinal));
        cut.Find("form input").Change("invalid-code");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("enrollment code is invalid", cut.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier is "localStorage.removeItem" or "localStorage.setItem");
        cut.Find("button[type='button']").Click();
        cut.Find("form").Submit();
        service.Verify(item => item.CheckInAsync("old-device-credential", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        cut.Find("button[type='button']").Click();
        cut.Find("form input").Change("replacement-code");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(JSInterop.Invocations, call => call.Identifier == "localStorage.setItem" && Equals(call.Arguments[1], "replacement-credential")));
        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier == "localStorage.removeItem");
        Assert.Contains("Appointment check-in code", cut.Markup, StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("recovery-codes", "/auth/mfa/recovery-codes")]
    [InlineData("pin-change", "/login")]
    [InlineData("enroll", "/login")]
    [InlineData("verify", "/login")]
    [InlineData("unknown", "/login")]
    public void AuthenticationStepUp_OnlyRoutesToServerAuthentication(string mode, string expectedPath)
    {
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo($"/authentication-step-up?mode={mode}&returnUrl=%2Fsettings");
        var cut = RenderComponent<AuthenticationStepUp>();
        Assert.Equal(expectedPath, new Uri(navigation.Uri).AbsolutePath);
        Assert.Equal("/settings", Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(navigation.Uri).Query)["returnUrl"].ToString());
        Assert.Empty(cut.FindAll("form"));
        Assert.DoesNotContain(JSInterop.Invocations, call => call.Identifier == "ptdocAuth.postStepUp");
    }
}
