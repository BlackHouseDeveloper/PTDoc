using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PTDoc.Application.Observability;
using Microsoft.Extensions.Logging;

namespace PTDoc.Infrastructure.Observability;

/// <summary>
/// Console-based telemetry sink for development.
/// Logs to console with structured formatting. NO PHI allowed.
/// </summary>
public class ConsoleTelemetrySink : ITelemetrySink
{
    private readonly ILogger<ConsoleTelemetrySink> _logger;

    public ConsoleTelemetrySink(ILogger<ConsoleTelemetrySink> logger)
    {
        _logger = logger;
    }

    public Task LogEventAsync(string eventName, string correlationId, Dictionary<string, object> metadata)
    {
        var metadataStr = string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        _logger.LogInformation(
            "[EVENT] {EventName} | CorrelationId={CorrelationId} | {Metadata}",
            eventName, correlationId, metadataStr);
        return Task.CompletedTask;
    }

    public Task LogMetricAsync(string metricName, double value, Dictionary<string, object>? metadata = null)
    {
        var metadataStr = metadata != null
            ? string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))
            : "none";
        _logger.LogInformation(
            "[METRIC] {MetricName}={Value} | {Metadata}",
            metricName, value, metadataStr);
        return Task.CompletedTask;
    }

    public Task LogExceptionAsync(Exception exception, string correlationId, Dictionary<string, object>? metadata = null)
    {
        var metadataStr = metadata != null
            ? string.Join(", ", metadata.Select(kvp => $"{kvp.Key}={kvp.Value}"))
            : "none";
        _logger.LogError(
            exception,
            "[EXCEPTION] CorrelationId={CorrelationId} | {Metadata}",
            correlationId, metadataStr);
        return Task.CompletedTask;
    }
}
