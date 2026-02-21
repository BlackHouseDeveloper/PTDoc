using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PTDoc.Application.Observability;

/// <summary>
/// Telemetry sink for logging application events with NO PHI.
/// All implementations must ensure patient data is never logged.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>
    /// Logs a structured event with metadata (NO PHI allowed).
    /// </summary>
    /// <param name="eventName">Event name (e.g., "SyncCompleted", "RuleViolation")</param>
    /// <param name="correlationId">Correlation ID for request tracking</param>
    /// <param name="metadata">Event metadata - MUST NOT contain PHI</param>
    Task LogEventAsync(string eventName, string correlationId, Dictionary<string, object> metadata);

    /// <summary>
    /// Logs a metric value.
    /// </summary>
    /// <param name="metricName">Metric name (e.g., "SyncDurationMs", "QueueDepth")</param>
    /// <param name="value">Metric value</param>
    /// <param name="metadata">Additional metadata - MUST NOT contain PHI</param>
    Task LogMetricAsync(string metricName, double value, Dictionary<string, object>? metadata = null);

    /// <summary>
    /// Logs an exception with context (NO PHI in exception message or metadata).
    /// </summary>
    /// <param name="exception">The exception to log</param>
    /// <param name="correlationId">Correlation ID for request tracking</param>
    /// <param name="metadata">Exception metadata - MUST NOT contain PHI</param>
    Task LogExceptionAsync(Exception exception, string correlationId, Dictionary<string, object>? metadata = null);
}
