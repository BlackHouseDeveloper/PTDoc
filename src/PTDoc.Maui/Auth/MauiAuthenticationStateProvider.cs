namespace PTDoc.Maui.Auth;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.LocalData;
using System.Security.Claims;
using PTDoc.Infrastructure.Services;

public sealed class MauiAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState AnonymousState =
        new AuthenticationState(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity()));

    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim authenticationGate = new(1, 1);
    private readonly LocalDataScope? localScope;
    private readonly ITokenStore tokenStore;
    private readonly ITokenService tokenService;
    private readonly ILogger<MauiAuthenticationStateProvider> logger;

    public MauiAuthenticationStateProvider(
        ITokenStore tokenStore,
        ITokenService tokenService,
        ILogger<MauiAuthenticationStateProvider> logger,
        LocalDataScope? localScope = null)
    {
        this.tokenStore = tokenStore;
        this.tokenService = tokenService;
        this.logger = logger;
        this.localScope = localScope;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await authenticationGate.WaitAsync();
        try
        {
            if (localScope is null) return await ReadAuthenticationStateAsync();
            return await localScope.RestoreAsync(async () =>
            {
                var state = await ReadAuthenticationStateAsync();
                return (state, ScopeKey(state.User));
            });
        }
        finally { authenticationGate.Release(); }
    }

    private async Task<AuthenticationState> ReadAuthenticationStateAsync()
    {
        try
        {
            var tokens = await tokenStore.GetAsync();
            if (tokens is null) return AnonymousState;
            if (tokens.ExpiresAtUtc <= DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                tokens = await tokenService.RefreshAsync(new RefreshTokenRequest(tokens.RefreshToken));
                if (tokens is null)
                {
                    await tokenStore.ClearAsync();
                    return AnonymousState;
                }
                await tokenStore.SaveAsync(tokens);
            }
            var principal = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
            if (principal.Identity?.IsAuthenticated == true) return new AuthenticationState(principal);
            await tokenStore.ClearAsync();
            return AnonymousState;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Native authentication could not be restored.");
            await tokenStore.ClearAsync();
            return AnonymousState;
        }
    }

    public async Task NotifyUserAuthenticationAsync(TokenResponse tokens)
    {
        await authenticationGate.WaitAsync();
        try
        {
            var principal = JwtClaimParser.CreatePrincipal(tokens.AccessToken);
            if (principal.Identity?.IsAuthenticated != true)
            {
                if (localScope is null) await tokenStore.ClearAsync();
                else await localScope.ChangeAsync(null, () => tokenStore.ClearAsync());
                NotifyAuthenticationStateChanged(Task.FromResult(AnonymousState));
                return;
            }

            if (localScope is null) await tokenStore.SaveAsync(tokens);
            else await localScope.ChangeAsync(ScopeKey(principal), () => tokenStore.SaveAsync(tokens));
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
        }
        finally { authenticationGate.Release(); }
    }

    public async Task NotifyUserLogoutAsync()
    {
        await authenticationGate.WaitAsync();
        try
        {
            if (localScope is null) await tokenStore.ClearAsync();
            else await localScope.ChangeAsync(null, () => tokenStore.ClearAsync());
            NotifyAuthenticationStateChanged(Task.FromResult(AnonymousState));
        }
        finally { authenticationGate.Release(); }
    }
    private static string? ScopeKey(ClaimsPrincipal? principal)
    {
        return Guid.TryParse(principal?.FindFirst(PTDocClaimTypes.InternalUserId)?.Value
                ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)
            && Guid.TryParse(principal?.FindFirst("clinic_id")?.Value, out var clinicId) && clinicId != Guid.Empty
            ? $"{userId:N}:{clinicId:N}" : null;
    }

}
