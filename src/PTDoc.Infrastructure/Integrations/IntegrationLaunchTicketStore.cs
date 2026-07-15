using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;
using StackExchange.Redis;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Stores Wibbi delegated launch URLs only for their short broker lifetime. Redis
/// consumption uses one atomic script; the in-memory fallback uses TryRemove and is
/// appropriate only for development or controlled single-instance deployments.
/// </summary>
public sealed class IntegrationLaunchTicketStore : IIntegrationLaunchTicketStore, IAsyncDisposable
{
    private const string KeyPrefix = "PTDoc:HepLaunch:Distributed:";
    private const string ConsumeScript =
        "local value = redis.call('GET', KEYS[1]); " +
        "if value then redis.call('DEL', KEYS[1]); end; " +
        "return value;";
    private readonly ConcurrentDictionary<string, MemoryTicket> _memory = new(StringComparer.Ordinal);
    private readonly Lazy<Task<ConnectionMultiplexer>>? _redis;

    public IntegrationLaunchTicketStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            _redis = new Lazy<Task<ConnectionMultiplexer>>(
                () => ConnectionMultiplexer.ConnectAsync(connectionString),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    public async Task StoreAsync(
        string token,
        string providerLaunchUrl,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(token);
        if (_redis is not null)
        {
            var connection = await _redis.Value.WaitAsync(cancellationToken);
            var stored = await connection.GetDatabase().StringSetAsync(
                key,
                providerLaunchUrl,
                lifetime,
                When.NotExists);
            if (!stored)
            {
                throw new InvalidOperationException("Launch ticket collision detected.");
            }
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _memory.Where(item => item.Value.ExpiresAtUtc <= now).Select(item => item.Key))
        {
            _memory.TryRemove(expired, out _);
        }
        if (!_memory.TryAdd(key, new MemoryTicket(providerLaunchUrl, now.Add(lifetime))))
        {
            throw new InvalidOperationException("Launch ticket collision detected.");
        }
    }

    public async Task<string?> ConsumeAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(token);
        if (_redis is not null)
        {
            var connection = await _redis.Value.WaitAsync(cancellationToken);
            var result = await connection.GetDatabase().ScriptEvaluateAsync(
                ConsumeScript,
                [new RedisKey(key)]);
            var value = (RedisValue)result;
            return value.HasValue ? value.ToString() : null;
        }

        if (!_memory.TryRemove(key, out var ticket) || ticket.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return null;
        }
        return ticket.ProviderLaunchUrl;
    }

    public async ValueTask DisposeAsync()
    {
        if (_redis is { IsValueCreated: true })
        {
            var connection = await _redis.Value;
            await connection.DisposeAsync();
        }
    }

    private static string BuildKey(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 48 || token.Any(value => !Uri.IsHexDigit(value)))
        {
            throw new InvalidOperationException("Integration launch token is invalid.");
        }

        return $"{KeyPrefix}{token.ToLowerInvariant()}";
    }

    private sealed record MemoryTicket(string ProviderLaunchUrl, DateTimeOffset ExpiresAtUtc);
}
