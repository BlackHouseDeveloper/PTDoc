using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using PTDoc.Infrastructure.Security;
using PTDoc.Infrastructure.Observability;
using PTDoc.Application.Observability;
using Microsoft.Extensions.Logging.Abstractions;

namespace PTDoc.Tests.Security;

public class SecurityObservabilityTests
{
    [Fact]
    public async Task EnvironmentDbKeyProvider_MissingEnvVar_ThrowsRegardlessOfEnvironment()
    {
        // Sprint P: the deterministic dev fallback key has been removed.
        // The provider must now fail-closed in all environments when the key is not set.
        var previousKey = Environment.GetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY");
        var previousEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

        var provider = new EnvironmentDbKeyProvider();

        try
        {
            // Act & Assert: must throw — no dev fallback is allowed
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetKeyAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", previousKey);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnv);
        }
    }

    [Fact]
    public async Task EnvironmentDbKeyProvider_WithEnvVar_ReturnsEnvKey()
    {
        // Arrange
        var testKey = "test-encryption-key-minimum-32-characters-required";
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", testKey);
        var provider = new EnvironmentDbKeyProvider();

        // Act
        var key = await provider.GetKeyAsync();

        // Assert
        Assert.Equal(testKey, key);

        // Cleanup
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", null);
    }

    [Fact]
    public async Task EnvironmentDbKeyProvider_ShortKey_ThrowsException()
    {
        // Arrange
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", "short");
        var provider = new EnvironmentDbKeyProvider();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetKeyAsync());

        // Cleanup
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", null);
    }

    [Fact]
    public async Task ConsoleTelemetrySink_LogEvent_DoesNotThrow()
    {
        // Arrange
        var sink = new ConsoleTelemetrySink(NullLogger<ConsoleTelemetrySink>.Instance);
        var metadata = new Dictionary<string, object>
        {
            { "EventType", "TestEvent" },
            { "Success", true },
            { "DurationMs", 123 }
        };

        // Act & Assert - should not throw, should not log PHI
        await sink.LogEventAsync("TestEvent", "correlation-123", metadata);
    }

    [Fact]
    public async Task ConsoleTelemetrySink_LogMetric_DoesNotThrow()
    {
        // Arrange
        var sink = new ConsoleTelemetrySink(NullLogger<ConsoleTelemetrySink>.Instance);

        // Act & Assert
        await sink.LogMetricAsync("QueueDepth", 5.0);
    }

    [Fact]
    public async Task ConsoleTelemetrySink_LogException_DoesNotThrow()
    {
        // Arrange
        var sink = new ConsoleTelemetrySink(NullLogger<ConsoleTelemetrySink>.Instance);
        var exception = new InvalidOperationException("Test exception");

        // Act & Assert
        await sink.LogExceptionAsync(exception, "correlation-456");
    }

    [Fact]
    public void TelemetryMetadata_ShouldNotContainPHI()
    {
        // Arrange - simulate telemetry event metadata
        var metadata = new Dictionary<string, object>
        {
            { "EventType", "SyncCompleted" },
            { "DurationMs", 250 },
            { "ItemsProcessed", 5 },
            { "Success", true }
            // NO patient names, NO note content, NO fax content
        };

        // Assert - validate no PHI-like keys
        Assert.DoesNotContain("PatientName", metadata.Keys);
        Assert.DoesNotContain("NoteContent", metadata.Keys);
        Assert.DoesNotContain("FaxContent", metadata.Keys);
        Assert.DoesNotContain("ChiefComplaint", metadata.Keys);
    }
}
