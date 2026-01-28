namespace PTDoc.Infrastructure.Services;

using System.Net.Http.Json;
using PTDoc.Application.Auth;

public sealed class TokenService : ITokenService
{
    private readonly HttpClient httpClient;

    public TokenService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/token", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    public async Task<TokenResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/refresh", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/logout", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}