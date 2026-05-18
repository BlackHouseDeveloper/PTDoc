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
            .ReturnsAsync(new IntakeInviteResult(false, null, null, "The secure invite link is invalid or expired."));

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
}
