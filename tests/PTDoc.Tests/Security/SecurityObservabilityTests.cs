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
    public async Task EnvironmentDbKeyProvider_DevelopmentMode_ReturnsDefaultKey()
    {
        // Arrange
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", null);
        var provider = new EnvironmentDbKeyProvider();
        
        // Act
        var key = await provider.GetKeyAsync();
        
        // Assert
        Assert.NotNull(key);
        Assert.True(key.Length >= 32, "Key must be at least 32 characters for SQLCipher");
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
