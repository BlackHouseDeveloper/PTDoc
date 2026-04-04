using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using System.Text.Json;
using Xunit;

namespace PTDoc.Tests.Sync;

/// <summary>
/// Tests for Sprint UC-Epsilon: offline-first storage and clinical sync completion.
/// Validates:
///  - ProcessQueueItemAsync marks entity SyncState as Synced (no longer a no-op)
///  - ReceiveClientPushAsync applies entity changes to the server database
///  - Clinical entities (IntakeForm, ClinicalNote) can be pushed/pulled
///  - Signed notes remain immutable — client push is rejected and no DB change is made
///  - Locked intake forms cannot be updated via client push
///  - Draft entities are applied with last-write-wins
///  - Role-based data scoping: Patient role excluded from clinical entities
/// </summary>
[Xunit.Trait("Category", "OfflineSync")]
public class SyncEpsilonTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    // ── ProcessQueueItemAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task PushAsync_ProcessQueueItem_MarksClinicalNoteSyncState_AsSynced()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"subjective\":\"draft content\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // Enqueue the note for processing
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Create);

        // Act: run push which calls ProcessQueueItemAsync
        var result = await syncEngine.PushAsync();

        // Assert: queue item was processed successfully
        Assert.True(result.SuccessCount >= 1);

        // Assert: the clinical note's SyncState is now Synced
        var updatedNote = await context.ClinicalNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id);
        Assert.Equal(SyncState.Synced, updatedNote.SyncState);
    }

    [Fact]
    public async Task PushAsync_ProcessQueueItem_MarksIntakeFormSyncState_AsSynced()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            ResponseJson = "{\"q1\":\"answer\"}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.IntakeForms.Add(intake);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Create);

        // Act
        var result = await syncEngine.PushAsync();

        // Assert
        Assert.True(result.SuccessCount >= 1);
        var updatedIntake = await context.IntakeForms.AsNoTracking().FirstAsync(i => i.Id == intake.Id);
        Assert.Equal(SyncState.Synced, updatedIntake.SyncState);
    }

    // ── ReceiveClientPushAsync entity application ─────────────────────────────

    [Fact]
    public async Task ReceiveClientPushAsync_AppliesPatientPayload_ToServerDatabase()
    {
        // Arrange: server has no record for this patient
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var patientId = Guid.NewGuid();

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "Patient",
                    ServerId = patientId,
                    LocalId = 1,
                    Operation = "Create",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        firstName = "Offline",
                        lastName = "Patient",
                        dateOfBirth = new DateTime(1990, 6, 15),
                        email = "offline@example.com",
                        phone = "555-1234",
                        medicalRecordNumber = "MRN-001",
                        lastModifiedUtc = DateTime.UtcNow
                    }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal("Accepted", response.Items[0].Status);

        // Assert: patient was actually persisted to the server DB
        var savedPatient = await context.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId);
        Assert.NotNull(savedPatient);
        Assert.Equal("Offline", savedPatient.FirstName);
        Assert.Equal("Patient", savedPatient.LastName);
        Assert.Equal("MRN-001", savedPatient.MedicalRecordNumber);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_AppliesIntakeFormPayload_WhenUnlocked()
    {
        // Arrange: existing unlocked intake on server
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var now = DateTime.UtcNow;
        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = "token",
            ResponseJson = "{\"old\":\"data\"}",
            IsLocked = false,
            LastModifiedUtc = now.AddMinutes(-10),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.IntakeForms.Add(intake);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "IntakeForm",
                    ServerId = intake.Id,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        patientId = patient.Id,
                        responseJson = "{\"q1\":\"updated answer\"}",
                        painMapData = "{\"regions\":[]}",
                        consents = "{}",
                        templateVersion = "1.0",
                        lastModifiedUtc = now
                    }),
                    LastModifiedUtc = now
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted
        Assert.Equal(1, response.AcceptedCount);

        // Assert: response JSON was updated in the DB
        var updated = await context.IntakeForms.AsNoTracking().FirstAsync(i => i.Id == intake.Id);
        Assert.Equal("{\"q1\":\"updated answer\"}", updated.ResponseJson);
        Assert.Equal(SyncState.Synced, updated.SyncState);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsIntakeFormUpdate_WhenLocked()
    {
        // Arrange: locked intake on server
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = "token",
            ResponseJson = "{\"locked\":\"data\"}",
            IsLocked = true,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-5),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        context.IntakeForms.Add(intake);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "IntakeForm",
                    ServerId = intake.Id,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = JsonSerializer.Serialize(new { responseJson = "{\"attempt\":\"hack\"}" }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: rejected as conflict (locked)
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("locked", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);

        // Assert: response JSON was NOT changed in the DB
        var notUpdated = await context.IntakeForms.AsNoTracking().FirstAsync(i => i.Id == intake.Id);
        Assert.Equal("{\"locked\":\"data\"}", notUpdated.ResponseJson);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_AppliesClinicalNotePayload_WhenUnsigned()
    {
        // Arrange: existing unsigned draft note on server
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var now = DateTime.UtcNow;
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"subjective\":\"old\"}",
            DateOfService = now.Date,
            LastModifiedUtc = now.AddMinutes(-10),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = note.Id,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        patientId = patient.Id,
                        contentJson = "{\"subjective\":\"updated offline\"}",
                        cptCodesJson = "[]",
                        dateOfService = now.Date,
                        lastModifiedUtc = now
                    }),
                    LastModifiedUtc = now
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted
        Assert.Equal(1, response.AcceptedCount);

        // Assert: ContentJson updated in server DB
        var updated = await context.ClinicalNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id);
        Assert.Equal("{\"subjective\":\"updated offline\"}", updated.ContentJson);
        Assert.Equal(SyncState.Synced, updated.SyncState);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsClinicalNoteUpdate_WhenSigned_AndDoesNotModifyDB()
    {
        // Arrange: signed note on server
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var originalContent = "{\"subjective\":\"signed content\"}";
        var note = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = originalContent,
            DateOfService = DateTime.UtcNow.Date,
            SignatureHash = "sha256-fakehash",
            SignedUtc = DateTime.UtcNow.AddMinutes(-30),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-30),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Synced
        };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = note.Id,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = JsonSerializer.Serialize(new { contentJson = "{\"subjective\":\"tampered\"}" }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: rejected
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Equal("Signed notes cannot be modified. Create addendum.", response.Items[0].Error);

        // Assert: ContentJson was NOT changed — signed note remains immutable
        var notUpdated = await context.ClinicalNotes.AsNoTracking().FirstAsync(n => n.Id == note.Id);
        Assert.Equal(originalContent, notUpdated.ContentJson);
        Assert.Equal("sha256-fakehash", notUpdated.SignatureHash);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_ClientPush_NeverTrustsSignatureHash_FromPayload()
    {
        // Arrange: new note being pushed from client (no server record exists)
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var newNoteId = Guid.NewGuid();

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = newNoteId,
                    LocalId = 1,
                    Operation = "Create",
                    // Client tries to submit a pre-signed note — server must ignore SignatureHash
                    DataJson = JsonSerializer.Serialize(new
                    {
                        patientId = patient.Id,
                        contentJson = "{\"subjective\":\"content\"}",
                        cptCodesJson = "[]",
                        dateOfService = DateTime.UtcNow.Date,
                        signatureHash = "fake-client-signature",
                        signedUtc = DateTime.UtcNow,
                        lastModifiedUtc = DateTime.UtcNow
                    }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: accepted (push itself is allowed for new notes)
        Assert.Equal(1, response.AcceptedCount);

        // Assert: SignatureHash was NOT applied from the client payload
        var savedNote = await context.ClinicalNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == newNoteId);
        Assert.NotNull(savedNote);
        Assert.Null(savedNote.SignatureHash); // server must reject client-supplied signature
        Assert.Null(savedNote.SignedUtc);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_NewClinicalNote_Created_WhenServerIdIsEmpty()
    {
        // Arrange: client has an offline note with ServerId == Guid.Empty
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = Guid.Empty, // new record
                    LocalId = 42,
                    Operation = "Create",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        patientId = patient.Id,
                        contentJson = "{\"subjective\":\"offline draft\"}",
                        cptCodesJson = "[]",
                        dateOfService = DateTime.UtcNow.Date,
                        lastModifiedUtc = DateTime.UtcNow
                    }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: server assigned a new UUID
        Assert.Equal(1, response.AcceptedCount);
        var assignedId = response.Items[0].ServerId;
        Assert.NotEqual(Guid.Empty, assignedId);

        // Assert: note was created in the server DB with the assigned ID
        var savedNote = await context.ClinicalNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == assignedId);
        Assert.NotNull(savedNote);
        Assert.Contains("offline draft", savedNote.ContentJson);
    }

    // ── Data scoping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClientDeltaAsync_PatientRole_DoesNotReceiveClinicalNotes_OrAuditLogs()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        context.ClinicalNotes.Add(new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"subjective\":\"PHI content\"}",
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var since = DateTime.UtcNow.AddMinutes(-2);

        // Act: pull as Patient role
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.Patient });

        // Assert: no clinical entities returned
        Assert.Empty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
        Assert.Empty(result.Items.Where(i => i.EntityType == "AuditLog"));
        Assert.Empty(result.Items.Where(i => i.EntityType == "ObjectiveMetric"));
    }

    [Fact]
    public async Task GetClientDeltaAsync_ClinicalStaff_ReceivesAllEntityTypes()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var patient = CreateTestPatient(context);
        var now = DateTime.UtcNow;
        context.ClinicalNotes.Add(new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            ContentJson = "{\"subjective\":\"eval\"}",
            DateOfService = now,
            LastModifiedUtc = now.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        });
        context.IntakeForms.Add(new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = "token",
            LastModifiedUtc = now.AddMinutes(-1),
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var since = DateTime.UtcNow.AddMinutes(-2);

        // Act: pull as PT (clinical staff)
        var result = await syncEngine.GetClientDeltaAsync(since, null, userRoles: new[] { Roles.PT });

        // Assert: clinical staff receives clinical notes and intake forms
        Assert.NotEmpty(result.Items.Where(i => i.EntityType == "ClinicalNote"));
        Assert.NotEmpty(result.Items.Where(i => i.EntityType == "IntakeForm"));
    }

    // ── EnqueueAsync called by API endpoints ─────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ForClinicalNote_CreatesQueueItem_WithSyncOperationCreate()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var noteId = Guid.NewGuid();

        // Act
        await syncEngine.EnqueueAsync("ClinicalNote", noteId, SyncOperation.Create);

        // Assert: queue item created with correct state
        var queueItem = await context.SyncQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.EntityId == noteId && q.EntityType == "ClinicalNote");
        Assert.NotNull(queueItem);
        Assert.Equal(SyncQueueStatus.Pending, queueItem.Status);
        Assert.Equal(SyncOperation.Create, queueItem.Operation);
    }

    [Fact]
    public async Task EnqueueAsync_ForIntakeForm_CreatesQueueItem_WithSyncOperationUpdate()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var intakeId = Guid.NewGuid();

        // Act
        await syncEngine.EnqueueAsync("IntakeForm", intakeId, SyncOperation.Update);

        // Assert
        var queueItem = await context.SyncQueueItems
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.EntityId == intakeId && q.EntityType == "IntakeForm");
        Assert.NotNull(queueItem);
        Assert.Equal(SyncQueueStatus.Pending, queueItem.Status);
        Assert.Equal(SyncOperation.Update, queueItem.Operation);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Patient CreateTestPatient(ApplicationDbContext context)
    {
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1980, 1, 1),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid(),
            SyncState = SyncState.Pending
        };
        context.Patients.Add(patient);
        context.SaveChanges();
        return patient;
    }
}
