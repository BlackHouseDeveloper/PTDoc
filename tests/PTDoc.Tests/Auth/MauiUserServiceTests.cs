using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Application.LocalData;
using PTDoc.Infrastructure.LocalData;
using PTDoc.Infrastructure.Services;
using PTDoc.Maui.Auth;

namespace PTDoc.Tests.Auth;

[Trait("Category", "CoreCi")]
public sealed class MauiUserServiceTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public async Task AuthenticationFailure_EndsUiSessionAndCacheAccessAfterLease_WithoutDeletingDrafts(bool syncOwnsLease, int failureMode)
    {
        var scope = new LocalDataScope();
        var tokens = CreateTokens();
        TokenResponse? stored = null;
        var tokenStore = CreateTokenStore();
        tokenStore.Setup(store => store.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => stored);
        tokenStore.Setup(store => store.SaveAsync(It.IsAny<TokenResponse>(), It.IsAny<CancellationToken>()))
            .Callback<TokenResponse, CancellationToken>((value, _) => stored = value).Returns(Task.CompletedTask);
        tokenStore.Setup(store => store.ClearAsync(It.IsAny<CancellationToken>()))
            .Callback(() => stored = null).Returns(Task.CompletedTask);
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(Tokens: tokens));
        tokenService.Setup(service => service.RefreshAsync(It.IsAny<RefreshTokenRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failureMode == 2 ? tokens : null);
        using var provider = new MauiAuthenticationStateProvider(tokenStore.Object, tokenService.Object,
            NullLogger<MauiAuthenticationStateProvider>.Instance, scope);
        using var user = new MauiUserService(tokenService.Object, tokenStore.Object, Mock.Of<IHttpClientFactory>(),
            provider, NullLogger<MauiUserService>.Instance);
        var authenticationChanges = new List<Task<AuthenticationState>>();
        provider.AuthenticationStateChanged += state => authenticationChanges.Add(state);
        Assert.True(await user.LoginAsync("clinician", "12345678"));
        var originalKey = scope.Key;
        Assert.NotNull(originalKey);
        await using var db = new LocalDbContext(new DbContextOptionsBuilder<LocalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, scope);
        var repository = new LocalRepository<LocalClinicalNoteDraft>(db);
        var draft = await repository.UpsertAsync(new LocalClinicalNoteDraft { ContentJson = "retained pending work" });
        if (failureMode == 0) stored = tokens with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        using var lease = syncOwnsLease ? await scope.EnterAsync() : null;
        using var handler = new AuthenticatedHttpMessageHandler(tokenStore.Object, tokenService.Object, user, scope)
        { InnerHandler = new UnauthorizedHandler() };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://ptdoc.test/api/v1/patients");
        request.Options.Set(AuthenticatedHttpMessageHandler.LocalScopeLeaseHeld, syncOwnsLease);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(stored);
        if (syncOwnsLease)
        {
            Assert.DoesNotContain(await Task.WhenAll(authenticationChanges), state => state.User.Identity?.IsAuthenticated != true);
            // In-flight sync can still persist retry state before ending its lease.
            draft.SyncState = PTDoc.Core.Models.SyncState.Pending;
            await db.SaveChangesAsync(timeout.Token);
            lease!.Dispose();
        }
        Assert.Single((await Task.WhenAll(authenticationChanges)).Where(state => state.User.Identity?.IsAuthenticated != true));
        Assert.False(user.IsAuthenticated);
        Assert.Null(user.CurrentUser);
        Assert.Null(scope.Key);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetAllAsync(timeout.Token));
        Assert.Equal("retained pending work", (await db.ClinicalNoteDrafts.IgnoreQueryFilters().SingleAsync(timeout.Token)).ContentJson);
        Assert.True(await user.LoginAsync("clinician", "12345678", cancellationToken: timeout.Token));
        Assert.Equal(originalKey, scope.Key);
        Assert.True(user.IsAuthenticated);
        Assert.Equal(draft.LocalId, Assert.Single(await repository.GetAllAsync(timeout.Token)).LocalId);
    }

    private sealed class UnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("?returnUrl=%2Forganizations", null)]
    [InlineData("?clinicId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("?clinicId=00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000")]
    [InlineData("?CLINIC%49D=%7Baaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa%7D#fragment", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task Login_ForwardsSelectedClinic(string query, string? expectedClinic)
    {
        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(service => service.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(StepUp: new TokenAuthenticationStep(AuthStatus.RequiresPinChange, "challenge", 10)));
        var service = CreateService(tokenService, CreateTokenStore(), new TestNavigationManager(query));

        await service.LoginAsync("staff", "12345678");

        var expectedId = expectedClinic is null ? (Guid?)null : Guid.Parse(expectedClinic);
        tokenService.Verify(item => item.LoginAsync(It.Is<LoginRequest>(request => request.ClinicId == expectedId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("?clinicId=invalid")]
    [InlineData("?clinicId=&clinicId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task Login_InvalidOrDuplicateClinic_DoesNotFallBackToHomeClinic(string query)
    {
        var tokenService = new Mock<ITokenService>();
        var service = CreateService(tokenService, CreateTokenStore(), new TestNavigationManager(query));

        Assert.False(await service.LoginAsync("staff", "12345678"));

        tokenService.Verify(item => item.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

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
        tokenStore.Verify(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()), Times.Once);
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
        tokenStore.Verify(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_PersistenceFailureDoesNotPublishAuthenticatedUser()
    {
        var tokenService = new Mock<ITokenService>();
        var tokenStore = CreateTokenStore();
        var tokens = CreateTokens();
        tokenService.Setup(service => service.LoginAsync(
                It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenLoginResult(Tokens: tokens));
        tokenStore.Setup(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secure storage unavailable"));
        var service = CreateService(tokenService, tokenStore);

        var result = await service.LoginAsync("clinician", "1234567890");

        Assert.False(result);
        Assert.False(service.IsAuthenticated);
        Assert.Null(service.CurrentUser);
        tokenStore.Verify(store => store.SaveAsync(tokens, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshToken_PersistsReplacementOnlyOnceBeforePublishingUser()
    {
        var tokenService = new Mock<ITokenService>();
        var tokenStore = CreateTokenStore();
        var current = CreateTokens();
        var refreshed = CreateTokens();
        tokenStore.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        tokenService.Setup(service => service.RefreshAsync(
                new RefreshTokenRequest(current.RefreshToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refreshed);
        var service = CreateService(tokenService, tokenStore);

        var result = await service.RefreshTokenAsync();

        Assert.True(result);
        Assert.True(service.IsAuthenticated);
        tokenStore.Verify(store => store.SaveAsync(refreshed, It.IsAny<CancellationToken>()), Times.Once);
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
        Mock<ITokenStore> tokenStore,
        NavigationManager? navigation = null)
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
            NullLogger<MauiUserService>.Instance,
            navigation);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string query) => Initialize("https://localhost/", "https://localhost/login" + query);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
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
                new Claim("clinic_id", Guid.NewGuid().ToString()),
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
