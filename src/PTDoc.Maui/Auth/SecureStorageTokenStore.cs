namespace PTDoc.Maui.Auth;

using System.Text.Json;
using PTDoc.Application.Auth;

public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string TokenKey = "ptdoc.auth.tokens";

    public async Task<TokenResponse?> GetAsync(CancellationToken cancellationToken = default)
    {
        var json = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<TokenResponse>(json);
    }

    public async Task SaveAsync(TokenResponse tokens, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(tokens);
        await SecureStorage.Default.SetAsync(TokenKey, json);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(TokenKey);
        return Task.CompletedTask;
    }
}