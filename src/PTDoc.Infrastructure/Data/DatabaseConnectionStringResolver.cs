using Microsoft.Extensions.Configuration;

namespace PTDoc.Infrastructure.Data;

public sealed record DatabaseConnectionStringResolution(string ConnectionString, string SourceKey, bool IsLegacySource);

public static class DatabaseConnectionStringResolver
{
    private static readonly (string ConfigKey, string? EnvironmentKey, bool IsLegacySource)[] PreferredSources =
    [
        ("ConnectionStrings:DefaultConnection", "ConnectionStrings__DefaultConnection", false),
        ("DefaultConnection", "DefaultConnection", true),
        ("Database:ConnectionString", "Database__ConnectionString", true),
        ("ConnectionStrings:PTDocsServer", "ConnectionStrings__PTDocsServer", true)
    ];

    public static DatabaseConnectionStringResolution Resolve(IConfiguration configuration)
    {
        foreach (var source in PreferredSources)
        {
            var value = configuration[source.ConfigKey];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new DatabaseConnectionStringResolution(value, source.ConfigKey, source.IsLegacySource);
            }
        }

        throw new InvalidOperationException(
            "Database connection string is not configured. Preferred key: ConnectionStrings:DefaultConnection. " +
            "Legacy fallbacks supported temporarily: DefaultConnection, Database:ConnectionString, ConnectionStrings:PTDocsServer.");
    }

    public static DatabaseConnectionStringResolution ResolveFromEnvironment()
    {
        foreach (var source in PreferredSources.Where(source => !string.IsNullOrWhiteSpace(source.EnvironmentKey)))
        {
            var value = Environment.GetEnvironmentVariable(source.EnvironmentKey!);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return new DatabaseConnectionStringResolution(value, source.EnvironmentKey!, source.IsLegacySource);
            }
        }

        throw new InvalidOperationException(
            "Database connection string is not configured. Preferred environment variable: ConnectionStrings__DefaultConnection. " +
            "Legacy fallbacks supported temporarily: DefaultConnection, Database__ConnectionString, ConnectionStrings__PTDocsServer.");
    }
}