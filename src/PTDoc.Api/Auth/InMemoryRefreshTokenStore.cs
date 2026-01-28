namespace PTDoc.Api.Auth;

using System.Collections.Concurrent;
using System.Security.Claims;

public interface IRefreshTokenStore
{
    Task StoreAsync(string refreshToken, RefreshTokenRecord record, CancellationToken cancellationToken);
    Task<RefreshTokenRecord?> GetAsync(string refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed record RefreshTokenRecord(
    string Subject,
    IReadOnlyCollection<Claim> Claims,
    DateTimeOffset ExpiresAtUtc);

public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshTokenRecord> tokens = new();

    public Task StoreAsync(string refreshToken, RefreshTokenRecord record, CancellationToken cancellationToken)
    {
        tokens[refreshToken] = record;
        return Task.CompletedTask;
    }

    public Task<RefreshTokenRecord?> GetAsync(string refreshToken, CancellationToken cancellationToken)
    {
        tokens.TryGetValue(refreshToken, out var record);
        return Task.FromResult(record);
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        tokens.TryRemove(refreshToken, out _);
        return Task.CompletedTask;
    }
}