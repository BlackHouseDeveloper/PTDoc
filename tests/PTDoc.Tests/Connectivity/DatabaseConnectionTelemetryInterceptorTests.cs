using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PTDoc.Infrastructure.Data.Interceptors;

namespace PTDoc.Tests.Connectivity;

[Trait("Category", "CoreCi")]
public sealed class DatabaseConnectionTelemetryInterceptorTests
{
    [Fact]
    public void ConnectionFailure_LogsCorrelationFields_WithoutExceptionMessageOrConnectionString()
    {
        var logger = new CapturingLogger<DatabaseConnectionTelemetryInterceptor>();
        var interceptor = new DatabaseConnectionTelemetryInterceptor(logger);
        using var activity = new Activity("database-connection-test");
        activity.SetTag("http.route", "/api/v1/sync/status");
        activity.Start();

        interceptor.LogConnectionFailure(
            new InvalidOperationException(
                "Patient Jane Doe failed on Server=tcp:secret.database.windows.net;Password=do-not-log"),
            "SqlConnection",
            "11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222",
            isAsync: true);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Contains("ProviderConnectionType=SqlConnection", entry.Message, StringComparison.Ordinal);
        Assert.Contains("Route=/api/v1/sync/status", entry.Message, StringComparison.Ordinal);
        Assert.Contains("TraceId=", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DbConnectionId=11111111-1111-1111-1111-111111111111", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DbContextId=22222222-2222-2222-2222-222222222222", entry.Message, StringComparison.Ordinal);
        Assert.Contains("IsAsync=True", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ExceptionType=System.InvalidOperationException", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Jane Doe", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.database.windows.net", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-log", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionFailure_NormalizesUntrustedDiagnosticValues()
    {
        var logger = new CapturingLogger<DatabaseConnectionTelemetryInterceptor>();
        var interceptor = new DatabaseConnectionTelemetryInterceptor(logger);

        interceptor.LogConnectionFailure(
            new InvalidOperationException("not logged"),
            "SqlConnection\r\nForgedLogEntry",
            string.Empty,
            string.Empty,
            isAsync: false);

        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain("\r", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ProviderConnectionType=SqlConnection  ForgedLogEntry", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DbConnectionId=unknown", entry.Message, StringComparison.Ordinal);
        Assert.Contains("DbContextId=unknown", entry.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
