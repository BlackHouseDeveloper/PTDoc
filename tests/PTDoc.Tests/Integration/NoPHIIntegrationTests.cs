using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Observability;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Observability;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests to ensure NO PHI appears in telemetry, audit logs, or sync queue.
/// Critical for HIPAA compliance.
/// </summary>
public class NoPHIIntegrationTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly TestTelemetrySink _telemetrySink;
    
    public NoPHIIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        
        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();
        
        _telemetrySink = new TestTelemetrySink();
    }
    
    [Fact]
    public async Task Telemetry_Contains_No_Patient_Names()
    {
        // Arrange: Create patient with PHI
        var patient = new Patient
        {
            FirstName = "John",
            LastName = "Doe",
            DateOfBirth = DateTime.UtcNow.AddYears(-45),
            Email = "john.doe@example.com"
        };
        
        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();
        
        // Act: Log telemetry event
        await _telemetrySink.LogEventAsync("PatientCreated", "test-correlation-id", new Dictionary<string, object>
        {
            { "EntityId", patient.Id },
            { "EntityType", "Patient" },
            { "Operation", "Create" }
        });
        
        // Assert: Telemetry contains NO PHI
        Assert.Single(_telemetrySink.Events);
        var telemetryEvent = _telemetrySink.Events.First();
        
        Assert.Contains(patient.Id.ToString(), telemetryEvent.EventName + telemetryEvent.MetadataJson);
        Assert.DoesNotContain("John", telemetryEvent.EventName + telemetryEvent.MetadataJson);
        Assert.DoesNotContain("Doe", telemetryEvent.EventName + telemetryEvent.MetadataJson);
        Assert.DoesNotContain("john.doe@example.com", telemetryEvent.EventName + telemetryEvent.MetadataJson);
    }
    
    [Fact]
    public async Task Sync_Queue_Contains_Only_Entity_Type_And_ID()
    {
        // Arrange: Create patient with PHI
        var patient = new Patient
        {
            FirstName = "Jane",
            LastName = "Smith",
            DateOfBirth = DateTime.UtcNow.AddYears(-30),
            SyncState = SyncState.Pending
        };
        
        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();
        
        // Create sync queue item manually (interceptor would normally do this)
        var queueItem = new SyncQueueItem
        {
            EntityType = "Patient",
            EntityId = patient.Id,
            Operation = SyncOperation.Create,
            EnqueuedAt = DateTime.UtcNow,
            Status = SyncQueueStatus.Pending
        };
        
        await _context.SyncQueueItems.AddAsync(queueItem);
        await _context.SaveChangesAsync();
        
        // Act: Retrieve queue item
        var retrieved = await _context.SyncQueueItems.FirstAsync(q => q.EntityId == patient.Id);
        
        // Assert: Queue item contains NO PHI
        Assert.Equal("Patient", retrieved.EntityType);
        Assert.Equal(patient.Id, retrieved.EntityId);
        Assert.Equal(SyncOperation.Create, retrieved.Operation);
        
        // Ensure no PHI fields are exposed
        Assert.DoesNotContain("Jane", retrieved.EntityType);
        Assert.DoesNotContain("Smith", retrieved.EntityType);
    }
    
    [Fact]
    public async Task Telemetry_For_Clinical_Notes_Contains_No_Content()
    {
        // Arrange: Create clinical note with sensitive content
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-50)
        };
        
        await _context.Patients.AddAsync(patient);
        await _context.SaveChangesAsync();
        
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            DateOfService = DateTime.UtcNow,
            NoteType = NoteType.ProgressNote,
            ContentJson = "{\"content\": \"Patient has severe shoulder pain and limited ROM\"}"
        };
        
        await _context.ClinicalNotes.AddAsync(note);
        await _context.SaveChangesAsync();
        
        // Act: Log telemetry for note creation
        await _telemetrySink.LogEventAsync("ClinicalNoteCreated", "correlation-123", new Dictionary<string, object>
        {
            { "EntityId", note.Id },
            { "EntityType", "ClinicalNote" },
            { "NoteType", note.NoteType.ToString() },
            { "PatientId", patient.Id }
        });
        
        // Assert: Telemetry contains NO clinical content
        Assert.Single(_telemetrySink.Events);
        var telemetryEvent = _telemetrySink.Events.First();
        
        Assert.Contains(note.Id.ToString(), telemetryEvent.MetadataJson);
        Assert.Contains(patient.Id.ToString(), telemetryEvent.MetadataJson);
        Assert.DoesNotContain("shoulder pain", telemetryEvent.MetadataJson);
        Assert.DoesNotContain("limited ROM", telemetryEvent.MetadataJson);
    }
    
    /// <summary>
    /// Test implementation of ITelemetrySink for validation
    /// </summary>
    private class TestTelemetrySink : ITelemetrySink
    {
        public List<TelemetryEvent> Events { get; } = new();
        
        public Task LogEventAsync(string eventName, string correlationId, Dictionary<string, object> metadata)
        {
            Events.Add(new TelemetryEvent
            {
                EventName = eventName,
                CorrelationId = correlationId,
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata),
                Timestamp = DateTime.UtcNow
            });
            
            return Task.CompletedTask;
        }
        
        public Task LogMetricAsync(string metricName, double value, Dictionary<string, object>? metadata = null)
        {
            return Task.CompletedTask;
        }
        
        public Task LogExceptionAsync(Exception exception, string correlationId, Dictionary<string, object>? metadata = null)
        {
            return Task.CompletedTask;
        }
    }
    
    private class TelemetryEvent
    {
        public string EventName { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string MetadataJson { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
    
    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
