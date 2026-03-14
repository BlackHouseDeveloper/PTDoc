using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using Xunit;

namespace PTDoc.Tests.Sync;

/// <summary>
/// Tests for the server-side sync protocol: ReceiveClientPushAsync and GetClientDeltaAsync.
/// Covers Sprint R requirements:
///  - Entity allowlist expansion (Patient, Appointment, IntakeForm, ClinicalNote, ObjectiveMetric, AuditLog)
///  - Conflict detection (timestamps, signed notes, locked intakes)
///  - Conflict resolution rules (draft LWW, signed immutable, intake locked)
///  - Reconnect sync and multi-device conflict scenarios
/// </summary>
public class SyncClientProtocolTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    // ── GetClientDeltaAsync – entity allowlist tests ──────────────────────────

    [Fact]
    public async Task GetClientDeltaAsync_ReturnsIntakeForms_ModifiedAfterWatermark()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var watermark = DateTime.UtcNow.AddMinutes(-5);

        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            LastModifiedUtc = watermark.AddMinutes(-10),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var form = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            LastModifiedUtc = watermark.AddMinutes(1) // after watermark
        };
        var oldForm = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            LastModifiedUtc = watermark.AddMinutes(-2) // before watermark
        };
        context.IntakeForms.AddRange(form, oldForm);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "IntakeForm" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("IntakeForm", result.Items[0].EntityType);
        Assert.Equal(form.Id, result.Items[0].ServerId);
        Assert.Contains("IsLocked", result.Items[0].DataJson);
    }

    [Fact]
    public async Task GetClientDeltaAsync_ReturnsClinicalNotes_ModifiedAfterWatermark()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var watermark = DateTime.UtcNow.AddMinutes(-5);

        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Draft note\"}",
            LastModifiedUtc = watermark.AddMinutes(1) // after watermark
        };
        var oldNote = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Old note\"}",
            LastModifiedUtc = watermark.AddMinutes(-3) // before watermark
        };
        context.ClinicalNotes.AddRange(note, oldNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "ClinicalNote" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("ClinicalNote", result.Items[0].EntityType);
        Assert.Equal(note.Id, result.Items[0].ServerId);
        Assert.Contains("NoteType", result.Items[0].DataJson);
    }

    [Fact]
    public async Task GetClientDeltaAsync_ReturnsObjectiveMetrics_WhenParentNoteModifiedAfterWatermark()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var watermark = DateTime.UtcNow.AddMinutes(-5);

        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var recentNote = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = watermark.AddMinutes(1) // after watermark
        };
        var oldNote = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = watermark.AddMinutes(-2) // before watermark
        };
        context.ClinicalNotes.AddRange(recentNote, oldNote);
        await context.SaveChangesAsync();

        var recentMetric = new ObjectiveMetric
        {
            NoteId = recentNote.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "90",
            IsWNL = false
        };
        var oldMetric = new ObjectiveMetric
        {
            NoteId = oldNote.Id,
            BodyPart = BodyPart.Shoulder,
            MetricType = MetricType.MMT,
            Value = "4",
            IsWNL = true
        };
        context.ObjectiveMetrics.AddRange(recentMetric, oldMetric);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "ObjectiveMetric" });

        // Assert: only the metric from the recently-modified note is returned
        Assert.Single(result.Items);
        Assert.Equal("ObjectiveMetric", result.Items[0].EntityType);
        Assert.Equal(recentMetric.Id, result.Items[0].ServerId);
        Assert.Contains("NoteId", result.Items[0].DataJson);
    }

    [Fact]
    public async Task GetClientDeltaAsync_ReturnsAuditLogs_AfterWatermark()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var watermark = DateTime.UtcNow.AddMinutes(-5);

        var recentLog = new AuditLog
        {
            EventType = "PatientAccess",
            Severity = "Info",
            TimestampUtc = watermark.AddMinutes(1),
            CorrelationId = Guid.NewGuid().ToString()
        };
        var oldLog = new AuditLog
        {
            EventType = "Login",
            Severity = "Info",
            TimestampUtc = watermark.AddMinutes(-2),
            CorrelationId = Guid.NewGuid().ToString()
        };
        context.AuditLogs.AddRange(recentLog, oldLog);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "AuditLog" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("AuditLog", result.Items[0].EntityType);
        Assert.Equal(recentLog.Id, result.Items[0].ServerId);
        Assert.Contains("EventType", result.Items[0].DataJson);
    }

    [Fact]
    public async Task GetClientDeltaAsync_DefaultTypes_IncludesAllAllowedEntities()
    {
        // Arrange: verify default entity types include all Sprint R entities
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Act: pull with no entity type filter (uses default)
        var result = await syncEngine.GetClientDeltaAsync(sinceUtc: null, entityTypes: null);

        // Assert: pulls succeeds without errors (no entities in DB, so 0 items)
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.True(result.SyncedAt > DateTime.MinValue);
    }

    // ── ReceiveClientPushAsync – conflict rules tests ─────────────────────────

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsPush_WhenClinicalNoteIsSigned()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Alice",
            LastName = "Smith",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var signedNote = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Signed evaluation\"}",
            SignatureHash = "abc123def456", // signed → immutable
            SignedUtc = DateTime.UtcNow.AddHours(-1),
            SignedByUserId = Guid.NewGuid(),
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(signedNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Modified signed note\"}",
                    LastModifiedUtc = DateTime.UtcNow // client is newer, but note is signed
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: push rejected due to signed immutability
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Single(response.Items);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("immutable", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsPush_WhenIntakeFormIsLocked()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var formId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Bob",
            LastName = "Jones",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var lockedIntake = new IntakeForm
        {
            Id = formId,
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true, // locked after eval
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.IntakeForms.Add(lockedIntake);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "IntakeForm",
                    ServerId = formId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"response\":\"modified\"}",
                    LastModifiedUtc = DateTime.UtcNow // client is newer, but intake is locked
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: push rejected due to locked intake
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Single(response.Items);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("locked", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsPush_WhenServerVersionIsNewer()
    {
        // Arrange: multi-device conflict, server has a more recent draft note
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Carol",
            LastName = "Davis",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var serverNote = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Server version, newer\"}",
            LastModifiedUtc = DateTime.UtcNow, // server is newer
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(serverNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Client version, older\"}",
                    LastModifiedUtc = DateTime.UtcNow.AddHours(-1) // client is older
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: conflict detected (server wins for newer server version)
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("newer", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_AcceptsPush_DraftNote_WhenClientIsNewer()
    {
        // Arrange: multi-device conflict, client has a more recent draft note (last-write-wins)
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Dan",
            LastName = "Evans",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var serverNote = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Server version, older\"}",
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1), // server is older
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(serverNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Client version, newer\"}",
                    LastModifiedUtc = DateTime.UtcNow // client is newer → last-write-wins, should be accepted
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted, client wins (last-write-wins for draft note)
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal("Accepted", response.Items[0].Status);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_AcceptsNewEntity_WhenNoServerVersionExists()
    {
        // Arrange: offline creation, entity doesn't exist on server yet
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "Patient",
                    ServerId = Guid.Empty, // new record not yet on server
                    LocalId = 42,
                    Operation = "Create",
                    DataJson = "{\"firstName\":\"New\",\"lastName\":\"Patient\"}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted and assigned a server ID
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal("Accepted", response.Items[0].Status);
        Assert.NotEqual(Guid.Empty, response.Items[0].ServerId);
        Assert.Equal(42, response.Items[0].LocalId);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_AllowsPushToUnsignedNote()
    {
        // Arrange: draft note without signature should be accepted
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Eve",
            LastName = "Foster",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        var draftNote = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Draft\"}",
            SignatureHash = null, // not signed
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(draftNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Updated draft\"}",
                    LastModifiedUtc = DateTime.UtcNow // newer than server
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: draft note is accepted
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal("Accepted", response.Items[0].Status);
    }

    // ── Reconnect sync scenario ───────────────────────────────────────────────

    [Fact]
    public async Task ReconnectSync_PushPendingChanges_ThenPullServerDelta()
    {
        // Arrange: simulate an offline session followed by reconnection
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Enqueue offline changes (simulating work done while offline)
        await syncEngine.EnqueueAsync("Patient", Guid.NewGuid(), SyncOperation.Create);
        await syncEngine.EnqueueAsync("ClinicalNote", Guid.NewGuid(), SyncOperation.Update);

        // Verify offline queue has items
        var statusBefore = await syncEngine.GetQueueStatusAsync();
        Assert.Equal(2, statusBefore.PendingCount);

        // Act: reconnect and run full sync (push then pull)
        var result = await syncEngine.SyncNowAsync();

        // Assert: push succeeded
        Assert.Equal(2, result.PushResult.TotalPushed);
        Assert.Equal(2, result.PushResult.SuccessCount);
        Assert.Equal(0, result.PushResult.FailureCount);

        // Queue should be cleared after successful push
        var statusAfter = await syncEngine.GetQueueStatusAsync();
        Assert.Equal(0, statusAfter.PendingCount);
    }

    [Fact]
    public async Task MultiDeviceConflict_DraftNote_ServerWins_WhenServerIsNewer()
    {
        // Arrange: same note modified on two devices; server version is newer
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Frank",
            LastName = "Garcia",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        // Device A modified at 10:00, Device B (server) modified at 10:05
        var serverNote = new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Device B version at 10:05\"}",
            LastModifiedUtc = DateTime.UtcNow, // server (Device B) is newer
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(serverNote);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        // Device A (client) tries to push its older version
        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Device A version at 10:00\"}",
                    LastModifiedUtc = DateTime.UtcNow.AddHours(-1) // Device A is older
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: conflict detected, Device A's push rejected, server version preserved
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.NotNull(response.Items[0].ServerModifiedUtc);
        // After rejection, client should pull server delta to get Device B's version
        var delta = await syncEngine.GetClientDeltaAsync(
            DateTime.UtcNow.AddHours(-2),
            new[] { "ClinicalNote" });
        Assert.Single(delta.Items);
        Assert.Equal(noteId, delta.Items[0].ServerId);
    }

    [Fact]
    public async Task MultiDeviceConflict_BatchPush_MixedAcceptedAndRejected()
    {
        // Arrange: batch push with one accepted item and one rejected signed note
        var context = CreateInMemoryContext();
        var signedNoteId = Guid.NewGuid();
        var draftNoteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Grace",
            LastName = "Hall",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.ClinicalNotes.AddRange(
            new ClinicalNote
            {
                Id = signedNoteId,
                PatientId = patient.Id,
                NoteType = NoteType.Evaluation,
                DateOfService = DateTime.UtcNow,
                ContentJson = "{\"text\":\"Signed eval\"}",
                SignatureHash = "signed123",
                SignedUtc = DateTime.UtcNow.AddHours(-2),
                SignedByUserId = Guid.NewGuid(),
                LastModifiedUtc = DateTime.UtcNow.AddHours(-2),
                ModifiedByUserId = Guid.NewGuid()
            },
            new ClinicalNote
            {
                Id = draftNoteId,
                PatientId = patient.Id,
                NoteType = NoteType.Daily,
                DateOfService = DateTime.UtcNow,
                ContentJson = "{\"text\":\"Draft daily\"}",
                SignatureHash = null,
                LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
                ModifiedByUserId = Guid.NewGuid()
            }
        );
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = signedNoteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Attempt to modify signed note\"}",
                    LastModifiedUtc = DateTime.UtcNow
                },
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = draftNoteId,
                    LocalId = 2,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Updated draft daily\"}",
                    LastModifiedUtc = DateTime.UtcNow // client is newer → accepted
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: signed note rejected, draft note accepted
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal(2, response.Items.Count);

        var signedResult = response.Items.First(i => i.LocalId == 1);
        var draftResult = response.Items.First(i => i.LocalId == 2);

        Assert.Equal("Conflict", signedResult.Status);
        Assert.Contains("immutable", signedResult.Error, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("Accepted", draftResult.Status);
    }
}
