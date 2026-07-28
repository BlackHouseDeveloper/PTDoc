using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PTDoc.Infrastructure.Data.Interceptors;

/// <summary>
/// Emits structured, PHI-safe diagnostics when EF Core cannot open a database
/// connection. Exception messages and connection details are deliberately
/// excluded; request and dependency telemetry can be correlated through TraceId.
/// </summary>
public sealed class DatabaseConnectionTelemetryInterceptor : DbConnectionInterceptor
{
    private const int MaxDiagnosticValueLength = 200;
    private readonly ILogger<DatabaseConnectionTelemetryInterceptor> _logger;

    public DatabaseConnectionTelemetryInterceptor(ILogger<DatabaseConnectionTelemetryInterceptor> logger)
    {
        _logger = logger;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        LogConnectionFailure(connection, eventData);
        base.ConnectionFailed(connection, eventData);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        LogConnectionFailure(connection, eventData);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    private void LogConnectionFailure(DbConnection connection, ConnectionErrorEventData eventData)
    {
        LogConnectionFailure(
            eventData.Exception,
            connection.GetType().Name,
            eventData.ConnectionId.ToString("D"),
            eventData.Context?.ContextId.InstanceId.ToString("D") ?? "unknown",
            eventData.IsAsync);
    }

    internal void LogConnectionFailure(
        Exception exception,
        string providerConnectionType,
        string connectionId,
        string contextId,
        bool isAsync)
    {
        var sqlException = FindSqlException(exception);
        var activity = Activity.Current;
        var traceId = activity is null || activity.TraceId == default
            ? "unknown"
            : activity.TraceId.ToString();
        var route = NormalizeDiagnosticValue(activity?.GetTagItem("http.route")?.ToString(), "unknown");
        var appServiceInstanceId = NormalizeDiagnosticValue(
            Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"),
            "local");

        _logger.LogWarning(
            "Database connection attempt failed. ProviderConnectionType={ProviderConnectionType} " +
            "SqlErrorNumber={SqlErrorNumber} SqlErrorClass={SqlErrorClass} SqlErrorState={SqlErrorState} " +
            "ClientConnectionId={ClientConnectionId} TraceId={TraceId} Route={Route} " +
            "DbConnectionId={DbConnectionId} DbContextId={DbContextId} IsAsync={IsAsync} " +
            "AppServiceInstanceId={AppServiceInstanceId} ExceptionType={ExceptionType}",
            NormalizeDiagnosticValue(providerConnectionType, "unknown"),
            sqlException?.Number,
            sqlException?.Class,
            sqlException?.State,
            sqlException?.ClientConnectionId.ToString("D") ?? "unknown",
            traceId,
            route,
            NormalizeDiagnosticValue(connectionId, "unknown"),
            NormalizeDiagnosticValue(contextId, "unknown"),
            isAsync,
            appServiceInstanceId,
            exception.GetType().FullName ?? exception.GetType().Name);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return null;
    }

    private static string NormalizeDiagnosticValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return sanitized.Length <= MaxDiagnosticValueLength
            ? sanitized
            : sanitized[..MaxDiagnosticValueLength];
    }
}
