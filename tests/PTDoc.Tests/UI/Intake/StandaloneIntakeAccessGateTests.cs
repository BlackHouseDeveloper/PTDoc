using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Intake;
using PTDoc.Maui.Components;
using PTDoc.UI.Components;
using PTDoc.UI.Components.Intake;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class StandaloneIntakeAccessGateTests : TestContext
{
    public StandaloneIntakeAccessGateTests() => Services.AddScoped<IntakeInviteRenewal>();

    [Fact]
    public void RenewalInvite_RequiresTheSamePatientPathAndVerifiedInvite()
    {
        var uri = new Uri($"https://localhost/intake/{Guid.NewGuid()}");
        var session = IntakeSessionToken.FromVerifiedInvite("session", DateTimeOffset.UtcNow.AddMinutes(5), "first");
        var renewal = new IntakeInviteRenewal();
        renewal.Remember(uri, "first");

        var otherPatient = new Uri($"https://localhost/intake/{Guid.NewGuid()}");
        var staffVisit = new Uri(uri, $"?noteId={Guid.NewGuid()}");
        Assert.Equal("first", renewal.ForSession(uri, null));
        Assert.Null(renewal.ForSession(otherPatient, session));
        Assert.Null(renewal.ForSession(otherPatient, null));
        Assert.Null(renewal.ForSession(staffVisit, session));
        Assert.Null(renewal.ForSession(staffVisit, null));
        Assert.Null(renewal.ForSession(uri, IntakeSessionToken.FromVerifiedInvite("other", session.ExpiresAt, "second")));
        Assert.Null(renewal.ForSession(uri, new IntakeSessionToken("legacy", session.ExpiresAt)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimerExpiryBeforeRemount_RequiresOtpAndRenewsOnlyTheOriginalInvite(bool native)
    {
        this.AddTestAuthorization();
        Services.AddSingleton(Mock.Of<IUserService>());
        var session = IntakeSessionToken.FromVerifiedInvite("verified", DateTimeOffset.UtcNow.AddHours(1), "original");
        IntakeSessionToken? stored = session;
        var store = new Mock<IIntakeSessionStore>();
        store.Setup(item => item.GetAsync(default)).ReturnsAsync(() => stored);
        store.Setup(item => item.ClearAsync(default)).Callback(() => stored = null).Returns(Task.CompletedTask);
        var invites = new Mock<IIntakeInviteService>();
        invites.Setup(item => item.ValidateAccessTokenAsync(session.Token, default)).ReturnsAsync(true);
        invites.Setup(item => item.ValidateInviteTokenAsync("original", default))
            .ReturnsAsync(new IntakeInviteValidationResponse(true, null, null));
        invites.Setup(item => item.SendOtpAsync("original", "patient@example.com", OtpChannel.Sms, default)).ReturnsAsync(true);
        Services.AddSingleton(store.Object);
        Services.AddSingleton(invites.Object);
        ComponentFactories.AddStub<RouteView>();
        var nav = Services.GetRequiredService<NavigationManager>();
        var path = $"/intake/{Guid.NewGuid()}?mode=patient";
        nav.NavigateTo(path + "&invite=original");
        IRenderedFragment router = native ? RenderComponent<MauiRoutes>() : RenderComponent<Routes>();
        router.WaitForAssertion(() => Assert.Single(router.FindComponents<Stub<RouteView>>()));
        var gate = router.FindComponent<StandaloneIntakeAccessGate>();

        var originalGate = gate.Instance;
        await ExpireGateAsync(gate);
        Assert.Null(stored);
        router.WaitForAssertion(() => Assert.Contains("Session Expired", router.Markup));
        await router.InvokeAsync(() => nav.NavigateTo(path));

        router.WaitForAssertion(() => Assert.NotSame(originalGate, router.FindComponent<StandaloneIntakeAccessGate>().Instance));
        router.WaitForElement("#intake-contact-input").Input("patient@example.com");
        var send = router.Find("button.intake-access-gate__btn--primary");
        Assert.False(send.HasAttribute("disabled"));
        send.Click();
        invites.Verify(item => item.SendOtpAsync("original", "patient@example.com", OtpChannel.Sms, default), Times.Once);
        Assert.Empty(router.FindComponents<Stub<RouteView>>());
        Assert.Null(stored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BareIntake_RequiresAnInviteBoundSessionThatSurvivesValidation(bool inviteBound)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        IntakeSessionToken? stored = inviteBound
            ? IntakeSessionToken.FromVerifiedInvite("access", expires, "original")
            : new IntakeSessionToken("access", expires);
        var validation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<IIntakeSessionStore>();
        store.Setup(item => item.GetAsync(default)).ReturnsAsync(() => stored);
        store.Setup(item => item.ClearAsync(default)).Callback(() => stored = null).Returns(Task.CompletedTask);
        var invites = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        invites.Setup(item => item.ValidateAccessTokenAsync("access", default)).Returns(validation.Task);
        Services.AddSingleton(store.Object);
        Services.AddSingleton(invites.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake?mode=patient");

        var gate = RenderComponent<StandaloneIntakeAccessGate>(
            parameters => parameters.AddChildContent("<p>Authorized intake</p>"));

        if (inviteBound)
        {
            gate.WaitForAssertion(() =>
                invites.Verify(item => item.ValidateAccessTokenAsync("access", default), Times.Once));
            await gate.InvokeAsync(() => store.Object.ClearAsync());
        }

        validation.SetResult(true);
        gate.WaitForAssertion(() => Assert.Contains("Verify Your Identity", gate.Markup));
        Assert.DoesNotContain("Authorized intake", gate.Markup);
        if (!inviteBound)
            invites.VerifyNoOtherCalls();
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verification_RequiresStoredSession_AndRemembersInviteForRenewal(bool canStoreToken)
    {
        var inviteService = new Mock<IIntakeInviteService>(MockBehavior.Strict);
        var sessionStore = new Mock<IIntakeSessionStore>(MockBehavior.Strict);
        IntakeSessionToken? stored = null;
        sessionStore
            .Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stored);
        sessionStore
            .Setup(store => store.SaveAsync(
                It.Is<IntakeSessionToken>(token => token.Token == "access-token"),
                It.IsAny<CancellationToken>()))
            .Callback<IntakeSessionToken, CancellationToken>((token, _) => stored = canStoreToken ? token : null)
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
        if (!canStoreToken)
        {
            cut.WaitForAssertion(() => Assert.Contains("request a new code", cut.Markup, StringComparison.Ordinal));
            Assert.DoesNotContain("Authorized intake", cut.Markup, StringComparison.Ordinal);
            return;
        }
        cut.WaitForAssertion(() =>
            Assert.Contains("Authorized intake", cut.Markup, StringComparison.Ordinal));
        Assert.True(stored!.MatchesInvite("valid-token"));
        Assert.False(stored.MatchesInvite("different-token"));

        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake");

        await ExpireGateAsync(cut);

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

    private static Task ExpireGateAsync(IRenderedComponent<StandaloneIntakeAccessGate> gate)
    {
        var handler = typeof(StandaloneIntakeAccessGate).GetMethod("HandleTokenExpiredAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(handler);
        return gate.InvokeAsync(() => (Task)handler!.Invoke(gate.Instance, null)!);
    }
}
