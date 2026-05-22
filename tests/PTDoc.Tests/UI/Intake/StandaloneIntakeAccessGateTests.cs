using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Intake;
using PTDoc.UI.Components.Intake;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class StandaloneIntakeAccessGateTests : TestContext
{
    [Fact]
    public void CheckAccess_RunsAfterFirstRender()
    {
        var inviteService = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        var sessionStore = new Mock<IIntakeSessionStore>(MockBehavior.Strict);
        var sessionLookup = new TaskCompletionSource<IntakeSessionToken?>();
        sessionStore
            .Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .Returns(sessionLookup.Task);

        Services.AddLogging();
        Services.AddSingleton(inviteService.Object);
        Services.AddSingleton(sessionStore.Object);

        var cut = RenderComponent<StandaloneIntakeAccessGate>();

        Assert.Contains("Verifying access", cut.Markup, StringComparison.Ordinal);
        cut.WaitForAssertion(() =>
            sessionStore.Verify(store => store.GetAsync(It.IsAny<CancellationToken>()), Times.Once));

        sessionLookup.SetResult(null);
        cut.WaitForAssertion(() =>
            Assert.Contains("Use the secure invite link from your clinic", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void RequestContact_DisablesOtpSend_WhenInviteTokenIsMissing()
    {
        var inviteService = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        var sessionStore = new Mock<IIntakeSessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSessionToken?)null);

        Services.AddLogging();
        Services.AddSingleton(inviteService.Object);
        Services.AddSingleton(sessionStore.Object);

        var cut = RenderComponent<StandaloneIntakeAccessGate>();

        cut.WaitForAssertion(() =>
            Assert.Contains("Use the secure invite link from your clinic", cut.Markup, StringComparison.Ordinal));

        cut.Find("#intake-contact-input").Input("patient@example.com");

        var sendButton = cut.Find("button.intake-access-gate__btn--primary");
        Assert.True(sendButton.HasAttribute("disabled"));
        inviteService.Verify(
            service => service.SendOtpAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<OtpChannel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void RequestContact_DisablesOtpSend_WhenInviteTokenValidationFails()
    {
        var inviteService = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        var sessionStore = new Mock<IIntakeSessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSessionToken?)null);
        inviteService
            .Setup(service => service.ValidateInviteTokenAsync("expired-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeInviteValidationResponse(false, null, "The secure invite link is invalid or expired."));

        Services.AddLogging();
        Services.AddSingleton(inviteService.Object);
        Services.AddSingleton(sessionStore.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake?invite=expired-token");

        var cut = RenderComponent<StandaloneIntakeAccessGate>();

        cut.WaitForAssertion(() =>
            Assert.Contains("The secure invite link is invalid or expired.", cut.Markup, StringComparison.Ordinal));

        cut.Find("#intake-contact-input").Input("patient@example.com");

        var sendButton = cut.Find("button.intake-access-gate__btn--primary");
        Assert.True(sendButton.HasAttribute("disabled"));
        inviteService.Verify(
            service => service.SendOtpAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<OtpChannel>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyAgain_UsesRememberedInviteToken_WhenAuthorizedSessionExpiresAfterNavigation()
    {
        var inviteService = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        var sessionStore = new Mock<IIntakeSessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSessionToken?)null);
        sessionStore
            .Setup(store => store.SaveAsync(
                It.Is<IntakeSessionToken>(token => token.Token == "access-token"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        sessionStore
            .Setup(store => store.ClearAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        inviteService
            .Setup(service => service.ValidateInviteTokenAsync("valid-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeInviteValidationResponse(true, DateTimeOffset.UtcNow.AddHours(1), null));
        inviteService
            .Setup(service => service.SendOtpAsync(
                "valid-token",
                "patient@example.com",
                OtpChannel.Sms,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        inviteService
            .Setup(service => service.VerifyOtpAndIssueAccessTokenAsync(
                "valid-token",
                "patient@example.com",
                OtpChannel.Sms,
                "123456",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeInviteResult(
                true,
                "access-token",
                DateTimeOffset.UtcNow.AddHours(1),
                null));
        inviteService
            .Setup(service => service.ValidateAccessTokenAsync("access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Services.AddLogging();
        Services.AddSingleton(inviteService.Object);
        Services.AddSingleton(sessionStore.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake?invite=valid-token");

        var cut = RenderComponent<StandaloneIntakeAccessGate>(
            parameters => parameters.AddChildContent("<p>Authorized intake</p>"));

        cut.WaitForAssertion(() =>
            Assert.Contains("Verify Your Identity", cut.Markup, StringComparison.Ordinal));

        cut.Find("#intake-contact-input").Input("patient@example.com");
        cut.Find("button.intake-access-gate__btn--primary").Click();
        cut.WaitForAssertion(() =>
            Assert.Contains("Enter Your Code", cut.Markup, StringComparison.Ordinal));

        cut.Find("#intake-otp-input").Input("123456");
        cut.Find("button.intake-access-gate__btn--primary").Click();
        cut.WaitForAssertion(() =>
            Assert.Contains("Authorized intake", cut.Markup, StringComparison.Ordinal));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake");

        var handleTokenExpired = typeof(StandaloneIntakeAccessGate).GetMethod(
            "HandleTokenExpiredAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handleTokenExpired);
        await cut.InvokeAsync(() => (Task)handleTokenExpired!.Invoke(cut.Instance, null)!);

        cut.WaitForAssertion(() =>
            Assert.Contains("Session Expired", cut.Markup, StringComparison.Ordinal));

        inviteService.Invocations.Clear();

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Verify Again", StringComparison.Ordinal))
            .Click();
        cut.Find("#intake-contact-input").Input("patient@example.com");

        var sendButton = cut.Find("button.intake-access-gate__btn--primary");
        Assert.False(sendButton.HasAttribute("disabled"));

        sendButton.Click();

        cut.WaitForAssertion(() =>
            inviteService.Verify(
                service => service.SendOtpAsync(
                    "valid-token",
                    "patient@example.com",
                    OtpChannel.Sms,
                    It.IsAny<CancellationToken>()),
                Times.Once));
    }
}
