using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Maui.Auth;

namespace PTDoc.Tests.Auth;

[Trait("Category", "CoreCi")]
public sealed class MauiUserServiceTests
{
    [Fact]
    public async Task PinChangeAndEnrollmentFlow_StoresTokensOnlyAfterRecoveryCodesAreAcknowledged()
    {
        var tokenService = new Mock<ITokenService>();
        var tokenStore = CreateTokenStore();
        tokenService.Setup(service => service.LoginAsync(
                It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(
                StepUp: new TokenAuthenticationStep(AuthStatus.RequiresPinChange, "pin-challenge", 10)));
        tokenService.Setup(service => service.CompletePinChangeAsync(
                "pin-challenge", "1234567890", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(
                StepUp: new TokenAuthenticationStep(AuthStatus.RequiresMfaEnrollment, "enrollment-start")));
        tokenService.Setup(service => service.BeginMfaEnrollmentAsync(
                "enrollment-start", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticatorEnrollmentStart(
                "MANUALKEY", "otpauth://totp/PTDoc:test", "<svg />", "enrollment-challenge"));
        tokenService.Setup(service => service.VerifyMfaEnrollmentAsync(
                "enrollment-challenge", "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthenticatorEnrollmentCompletion(
                ["recovery-one", "recovery-two"], "completion-token"));
        var tokens = CreateTokens();
        tokenService.Setup(service => service.CompleteAuthenticationAsync(
                "completion-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var service = CreateService(tokenService, tokenStore);

        Assert.False(await service.LoginAsync("clinician", "1234567890"));
        Assert.Equal(AuthenticationStepKind.RequiredPinChange, service.PendingAuthenticationStep?.Kind);
        Assert.Equal(10, service.PendingAuthenticationStep?.MinimumPinLength);
        Assert.False(service.IsAuthenticated);
        tokenStore.Verify(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        var pinChange = await service.CompleteRequiredPinChangeAsync("1234567890");

        Assert.True(pinChange.Succeeded);
        Assert.Equal(AuthenticationStepKind.AuthenticatorEnrollment, service.PendingAuthenticationStep?.Kind);
        Assert.Equal("MANUALKEY", service.PendingAuthenticationStep?.ManualKey);
        Assert.False(service.IsAuthenticated);
        tokenStore.Verify(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        var enrollment = await service.VerifyAuthenticatorEnrollmentAsync("123456");

        Assert.True(enrollment.Succeeded);
        Assert.Equal(AuthenticationStepKind.RecoveryCodes, service.PendingAuthenticationStep?.Kind);
        Assert.Equal(new[] { "recovery-one", "recovery-two" }, service.PendingAuthenticationStep?.RecoveryCodes);
        Assert.False(service.IsAuthenticated);
        tokenStore.Verify(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        var completion = await service.CompleteAuthenticatorEnrollmentAsync();

        Assert.True(completion.Succeeded);
        Assert.True(service.IsAuthenticated);
        Assert.Null(service.PendingAuthenticationStep);
        Assert.Equal("clinician@example.com", service.UserEmail);
        tokenStore.Verify(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task MfaVerificationFailure_DoesNotStoreTokensAndSuccessfulRetryCompletesAuthentication()
    {
        var tokenService = new Mock<ITokenService>();
        var tokenStore = CreateTokenStore();
        tokenService.Setup(service => service.LoginAsync(
                It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(
                StepUp: new TokenAuthenticationStep(AuthStatus.RequiresMfaVerification, "mfa-challenge")));
        tokenService.Setup(service => service.VerifyMfaAsync(
                "mfa-challenge", "000000", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCodeVerificationResult(false, ErrorCode: "invalid_code"));
        tokenService.Setup(service => service.VerifyMfaAsync(
                "mfa-challenge", "recovery-code", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaCodeVerificationResult(true, "mfa-completion"));
        var tokens = CreateTokens();
        tokenService.Setup(service => service.CompleteAuthenticationAsync(
                "mfa-completion", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokens);
        var service = CreateService(tokenService, tokenStore);

        Assert.False(await service.LoginAsync("clinician", "1234567890"));
        Assert.Equal(AuthenticationStepKind.MfaVerification, service.PendingAuthenticationStep?.Kind);

        var rejected = await service.VerifyMfaAsync("000000", useRecoveryCode: false);

        Assert.False(rejected.Succeeded);
        Assert.Equal(AuthenticationStepKind.MfaVerification, service.PendingAuthenticationStep?.Kind);
        Assert.False(service.IsAuthenticated);
        tokenStore.Verify(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()), Times.Never);

        var accepted = await service.VerifyMfaAsync("recovery-code", useRecoveryCode: true);

        Assert.True(accepted.Succeeded);
        Assert.True(service.IsAuthenticated);
        Assert.Null(service.PendingAuthenticationStep);
        tokenStore.Verify(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelAuthenticationStep_ClearsChallengeAndPreventsCredentialStorage()
    {
        var tokenService = new Mock<ITokenService>();
        var tokenStore = CreateTokenStore();
        tokenService.Setup(service => service.LoginAsync(
                It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(
                StepUp: new TokenAuthenticationStep(AuthStatus.RequiresPinChange, "pin-challenge", 8)));
        var service = CreateService(tokenService, tokenStore);
        Assert.False(await service.LoginAsync("clinician", "12345678"));

        service.CancelAuthenticationStep();
        var completion = await service.CompleteRequiredPinChangeAsync("87654321");

        Assert.False(completion.Succeeded);
        Assert.Null(service.PendingAuthenticationStep);
        Assert.False(service.IsAuthenticated);
        tokenService.Verify(service => service.CompletePinChangeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        tokenStore.Verify(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static MauiUserService CreateService(
        Mock<ITokenService> tokenService,
        Mock<ITokenStore> tokenStore)
    {
        var stateProvider = new MauiAuthenticationStateProvider(
            tokenStore.Object,
            tokenService.Object,
            NullLogger<MauiAuthenticationStateProvider>.Instance);
        return new MauiUserService(
            tokenService.Object,
            tokenStore.Object,
            Mock.Of<IHttpClientFactory>(),
            stateProvider,
            NullLogger<MauiUserService>.Instance);
    }

    private static Mock<ITokenStore> CreateTokenStore()
    {
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(store => store.SaveAsync(
                It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return tokenStore;
    }

    private static TokenResponse CreateTokens()
    {
        var accessToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, "Clinician"),
                new Claim(ClaimTypes.Email, "clinician@example.com")
            ],
            expires: DateTime.UtcNow.AddMinutes(15)));
        return new TokenResponse(
            accessToken,
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(15));
    }
}
