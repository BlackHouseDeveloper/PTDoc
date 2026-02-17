namespace PTDoc.Application.Common;

/// <summary>
/// Interface for telemetry and observability.
/// </summary>
public interface ITelemetrySink
{
    /// <summary>
    /// Tracks a custom event.
    /// </summary>
    void TrackEvent(string eventName, Dictionary<string, string>? properties = null, Dictionary<string, double>? metrics = null);
    
    /// <summary>
    /// Tracks a performance metric.
    /// </summary>
    void TrackMetric(string metricName, double value, Dictionary<string, string>? properties = null);
    
    /// <summary>
    /// Tracks an exception.
    /// </summary>
    void TrackException(Exception exception, Dictionary<string, string>? properties = null);
    
    /// <summary>
    /// Tracks a dependency call (external API, database, etc.).
    /// </summary>
    void TrackDependency(string dependencyName, string commandName, DateTimeOffset startTime, TimeSpan duration, bool success);
    
    /// <summary>
    /// Tracks an HTTP request.
    /// </summary>
    void TrackRequest(string name, DateTimeOffset startTime, TimeSpan duration, string responseCode, bool success);
}
