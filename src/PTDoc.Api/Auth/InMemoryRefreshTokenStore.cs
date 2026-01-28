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

/// <summary>
/// In-memory refresh token store for development and testing.
/// WARNING: This implementation loses all tokens on application restart, forcing users to re-authenticate.
/// TODO: Replace with persistent storage (database, Redis, etc.) for production use.
/// This is particularly important for HIPAA-compliant healthcare applications where unexpected
/// session terminations could disrupt clinical workflows.
/// </summary>
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