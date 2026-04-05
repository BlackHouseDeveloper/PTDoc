using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Sync;
using System.Text.Json;
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
[Xunit.Trait("Category", "OfflineSync")]
public class SyncClientProtocolTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static ISignatureService CreateSignatureService(ApplicationDbContext context, Mock<IAuditService>? auditMock = null)
    {
        var audit = auditMock?.Object ?? Mock.Of<IAuditService>();
        var identity = Mock.Of<IIdentityContextAccessor>();
        var clinicalRules = new Mock<IClinicalRulesEngine>();
        clinicalRules
            .Setup(engine => engine.RunClinicalValidationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RuleEvaluationResult>());

        return new SignatureService(context, audit, identity, clinicalRules.Object);
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
            StructuredDataJson = "{\"schemaVersion\":\"2026-03-30\",\"bodyPartSelections\":[{\"bodyPartId\":\"knee\",\"lateralities\":[\"left\"]}]}",
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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "IntakeForm" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("IntakeForm", result.Items[0].EntityType);
        Assert.Equal(form.Id, result.Items[0].ServerId);
        Assert.Contains("IsLocked", result.Items[0].DataJson);
        Assert.Contains("StructuredDataJson", result.Items[0].DataJson);
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
            CreatedUtc = DateTime.UtcNow.AddHours(-1),
            ParentNoteId = Guid.NewGuid(),
            IsAddendum = true,
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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "ClinicalNote" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("ClinicalNote", result.Items[0].EntityType);
        Assert.Equal(note.Id, result.Items[0].ServerId);
        Assert.Contains("NoteType", result.Items[0].DataJson);
        Assert.Contains("CreatedUtc", result.Items[0].DataJson);
        Assert.Contains("ParentNoteId", result.Items[0].DataJson);
        Assert.Contains("IsAddendum", result.Items[0].DataJson);
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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        // Act
        var result = await syncEngine.GetClientDeltaAsync(watermark, new[] { "AuditLog" });

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("AuditLog", result.Items[0].EntityType);
        Assert.Equal(recentLog.Id, result.Items[0].ServerId);
        Assert.Contains("EventType", result.Items[0].DataJson);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_Replays_DuplicateOperationId_WithoutDuplicateWrite()
    {
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));
        var operationId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    OperationId = operationId,
                    EntityType = "Patient",
                    ServerId = patientId,
                    LocalId = 7,
                    Operation = "Create",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        firstName = "Offline",
                        lastName = "Replay",
                        dateOfBirth = new DateTime(1991, 1, 1),
                        lastModifiedUtc = timestamp
                    }),
                    LastModifiedUtc = timestamp
                }
            ]
        };

        var first = await syncEngine.ReceiveClientPushAsync(request);
        var second = await syncEngine.ReceiveClientPushAsync(request);

        Assert.Equal(1, first.AcceptedCount);
        Assert.Equal(1, second.AcceptedCount);
        Assert.Equal(patientId, second.Items[0].ServerId);
        Assert.Equal(1, await context.Patients.CountAsync());
        Assert.Equal(1, await context.SyncQueueItems.CountAsync(q => q.Id == operationId));
    }

    [Fact]
    public async Task ReceiveClientPushAsync_Writes_SyncAuditEvents_WithoutPhi()
    {
        var context = CreateInMemoryContext();
        var auditService = new AuditService(context);
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance, auditService: auditService);
        var operationId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var timestamp = DateTime.UtcNow;

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    OperationId = operationId,
                    EntityType = "Patient",
                    ServerId = patientId,
                    LocalId = 3,
                    Operation = "Create",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        firstName = "Hidden",
                        lastName = "Phi",
                        dateOfBirth = new DateTime(1992, 2, 2),
                        lastModifiedUtc = timestamp
                    }),
                    LastModifiedUtc = timestamp
                }
            ]
        };

        await syncEngine.ReceiveClientPushAsync(request);

        var auditLogs = await context.AuditLogs
            .Where(a => a.EventType == "SYNC_START" || a.EventType == "SYNC_SUCCESS")
            .ToListAsync();

        Assert.Equal(2, auditLogs.Count);
        Assert.All(auditLogs, log => Assert.DoesNotContain("Hidden", log.MetadataJson, StringComparison.OrdinalIgnoreCase));
        Assert.All(auditLogs, log => Assert.DoesNotContain("Phi", log.MetadataJson, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetClientDeltaAsync_DefaultTypes_IncludesAllAllowedEntities()
    {
        // Arrange: seed one row for each Sprint R entity type
        var context = CreateInMemoryContext();
        var watermark = DateTime.UtcNow.AddHours(-1);

        var patient = new Patient
        {
            FirstName = "Default",
            LastName = "Test",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.Appointments.Add(new Appointment
        {
            PatientId = patient.Id,
            StartTimeUtc = DateTime.UtcNow,
            EndTimeUtc = DateTime.UtcNow.AddHours(1),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        });

        context.IntakeForms.Add(new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        });

        var clinicalNote = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.ClinicalNotes.Add(clinicalNote);

        // ObjectiveMetric.NoteId references clinicalNote.Id which is set in the initializer
        // (Guid.NewGuid()), so no intermediate save is needed.
        context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            NoteId = clinicalNote.Id,
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "90",
            IsWNL = false
        });

        context.AuditLogs.Add(new AuditLog
        {
            EventType = "PatientAccess",
            Severity = "Info",
            TimestampUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString()
        });

        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        // Act: pull with no entity type filter (uses default allowlist)
        var result = await syncEngine.GetClientDeltaAsync(sinceUtc: watermark, entityTypes: null);

        // Assert: all six Sprint R entity types are present
        Assert.NotNull(result);
        Assert.True(result.SyncedAt > DateTime.MinValue);

        var entityTypes = result.Items.Select(i => i.EntityType).Distinct().ToHashSet();
        Assert.Contains("Patient", entityTypes);
        Assert.Contains("Appointment", entityTypes);
        Assert.Contains("IntakeForm", entityTypes);
        Assert.Contains("ClinicalNote", entityTypes);
        Assert.Contains("ObjectiveMetric", entityTypes);
        Assert.Contains("AuditLog", entityTypes);
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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

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

        // Assert: original note is preserved and the conflict is redirected into an addendum
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Single(response.Items);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("addendum", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
        var conflict = Assert.IsType<ConflictResult>(response.Items[0].Conflict);
        Assert.Equal(ConflictType.SignedConflict, conflict.ConflictType);
        Assert.Equal(ConflictResolution.AddendumCreated, conflict.ResolutionType);
        Assert.NotNull(conflict.NewEntityId);

        var addendum = await context.Addendums.FindAsync(conflict.NewEntityId);
        Assert.NotNull(addendum);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_RejectsPush_WhenClinicalNoteIsPendingCoSign()
    {
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Piper",
            LastName = "Pending",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.PendingCoSign,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{\"text\":\"Pending co-sign note\"}",
            SignatureHash = null,
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));
        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 2,
                    Operation = "Update",
                    DataJson = "{\"text\":\"Attempted pending note edit\"}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var response = await syncEngine.ReceiveClientPushAsync(request);

        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("Pending", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_ReplaysSignedConflict_WithoutCreatingDuplicateAddendum()
    {
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();
        var operationId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Replay",
            LastName = "Signed",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"text\":\"Signed\"}",
            SignatureHash = "signed-hash",
            SignedUtc = DateTime.UtcNow.AddMinutes(-5),
            SignedByUserId = Guid.NewGuid(),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-5),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    OperationId = operationId,
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 4,
                    Operation = "Update",
                    DataJson = "{\"text\":\"offline conflict\"}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        var first = await syncEngine.ReceiveClientPushAsync(request);
        var second = await syncEngine.ReceiveClientPushAsync(request);

        Assert.Equal(1, await context.Addendums.CountAsync());
        Assert.Equal("Conflict", first.Items[0].Status);
        Assert.Equal("Conflict", second.Items[0].Status);
        var firstConflict = Assert.IsType<ConflictResult>(first.Items[0].Conflict);
        var secondConflict = Assert.IsType<ConflictResult>(second.Items[0].Conflict);
        Assert.Equal(firstConflict.NewEntityId, secondConflict.NewEntityId);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_ConflictAuditMetadata_DoesNotContainPayloadText()
    {
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();
        var auditService = new AuditService(context);
        var signatureService = CreateSignatureService(context);

        var patient = new Patient
        {
            FirstName = "Audit",
            LastName = "Safety",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
        context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            ContentJson = "{\"text\":\"Signed\"}",
            SignatureHash = "signed-hash",
            SignedUtc = DateTime.UtcNow.AddMinutes(-5),
            SignedByUserId = Guid.NewGuid(),
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-5),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            auditService: auditService,
            signatureService: signatureService);

        var request = new ClientSyncPushRequest
        {
            Items =
            [
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = noteId,
                    LocalId = 6,
                    Operation = "Update",
                    DataJson = "{\"text\":\"tampered payload content\"}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };

        await syncEngine.ReceiveClientPushAsync(request);

        var conflictLogs = await context.AuditLogs
            .Where(log => log.EventType == "CONFLICT_DETECTED" || log.EventType == "ADDENDUM_CREATED")
            .ToListAsync();

        Assert.NotEmpty(conflictLogs);
        Assert.DoesNotContain(conflictLogs, log => (log.MetadataJson ?? string.Empty).Contains("tampered payload content", StringComparison.OrdinalIgnoreCase));
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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

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
        var conflict = Assert.IsType<ConflictResult>(response.Items[0].Conflict);
        Assert.Equal(ConflictType.DraftConflict, conflict.ConflictType);
        Assert.Equal(ConflictResolution.ServerWins, conflict.ResolutionType);
        Assert.Single(await context.Set<SyncConflictArchive>().ToListAsync());
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
        var conflict = Assert.IsType<ConflictResult>(response.Items[0].Conflict);
        Assert.Equal(ConflictType.DraftConflict, conflict.ConflictType);
        Assert.Equal(ConflictResolution.LocalWins, conflict.ResolutionType);
        Assert.Single(await context.Set<SyncConflictArchive>().ToListAsync());
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
    public async Task ReceiveClientPushAsync_CreatesClinicalNote_WhenNoteTypeIsEnumNameString()
    {
        var context = CreateInMemoryContext();
        var clinicId = Guid.NewGuid();
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Nina",
            LastName = "Newnote",
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var dateOfService = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);
        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = Guid.Empty,
                    LocalId = 17,
                    Operation = "Create",
                    DataJson =
                        $$"""{"patientId":"{{patient.Id}}","noteType":"ProgressNote","dateOfService":"{{dateOfService:O}}","contentJson":"{}","cptCodesJson":"[]"}""",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var response = await syncEngine.ReceiveClientPushAsync(request);

        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);

        var storedNote = await context.ClinicalNotes.SingleAsync();
        Assert.Equal(NoteType.ProgressNote, storedNote.NoteType);
        Assert.Equal(NoteStatus.Draft, storedNote.NoteStatus);
        Assert.Equal(patient.Id, storedNote.PatientId);
        Assert.Equal(clinicId, storedNote.ClinicId);
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

        // Create entities in the DB so ProcessQueueItemAsync can find them
        var userId = Guid.NewGuid();
        var patient = new Patient { FirstName = "Reconnect", LastName = "User", DateOfBirth = new DateTime(1990, 1, 1), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        var note = new ClinicalNote { PatientId = patient.Id, NoteType = NoteType.Daily, DateOfService = DateTime.UtcNow, ContentJson = "{}", LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending };
        context.ClinicalNotes.Add(note);
        await context.SaveChangesAsync();

        // Enqueue offline changes (simulating work done while offline)
        await syncEngine.EnqueueAsync("Patient", patient.Id, SyncOperation.Create);
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Update);

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

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

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
        Assert.Contains("addendum", signedResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConflictResolution.AddendumCreated, signedResult.Conflict!.ResolutionType);

        Assert.Equal("Accepted", draftResult.Status);
    }

    // ── Case-insensitive entity type handling tests ───────────────────────────

    [Theory]
    [InlineData("clinicalnote")]
    [InlineData("CLINICALNOTE")]
    [InlineData("ClinicalNote")]
    public async Task ReceiveClientPushAsync_RejectsSigned_WhenEntityTypeCasingVaries(string entityTypeCasing)
    {
        // Arrange: signed note should be rejected regardless of client-supplied casing
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Casing",
            LastName = "Test",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            SignatureHash = "abc123",
            SignedUtc = DateTime.UtcNow.AddHours(-1),
            SignedByUserId = Guid.NewGuid(),
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(
            context,
            NullLogger<SyncEngine>.Instance,
            signatureService: CreateSignatureService(context));

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = entityTypeCasing,
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: signed note rejected regardless of casing
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("addendum", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("intakeform")]
    [InlineData("INTAKEFORM")]
    [InlineData("IntakeForm")]
    public async Task ReceiveClientPushAsync_RejectsLockedIntake_WhenEntityTypeCasingVaries(string entityTypeCasing)
    {
        // Arrange: locked intake should be rejected regardless of client-supplied casing
        var context = CreateInMemoryContext();
        var formId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Casing",
            LastName = "Test2",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.IntakeForms.Add(new IntakeForm
        {
            Id = formId,
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString(),
            IsLocked = true,
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = entityTypeCasing,
                    ServerId = formId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: locked intake rejected regardless of casing
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("locked", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("clinicalnote")]
    [InlineData("CLINICALNOTE")]
    public async Task ReceiveClientPushAsync_DetectsTimestampConflict_WhenEntityTypeCasingVaries(string entityTypeCasing)
    {
        // Arrange: server has a more recent draft note; client uses varied casing
        var context = CreateInMemoryContext();
        var noteId = Guid.NewGuid();

        var patient = new Patient
        {
            FirstName = "Casing",
            LastName = "Test3",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);

        context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = noteId,
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow, // server is newer
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = entityTypeCasing,
                    ServerId = noteId,
                    LocalId = 1,
                    Operation = "Update",
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow.AddHours(-1) // client is older
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: timestamp conflict detected regardless of casing
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.Contains("newer", response.Items[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Delete semantics tests ────────────────────────────────────────────────

    [Fact]
    public async Task ReceiveClientPushAsync_DeleteExistingPatient_IsAccepted()
    {
        // Arrange: existing patient on server, client pushes a delete
        var context = CreateInMemoryContext();

        var patient = new Patient
        {
            FirstName = "Jane",
            LastName = "Doe",
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "Patient",
                    ServerId = patient.Id,
                    LocalId = 1,
                    Operation = "Delete",
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: delete is accepted without conflict
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal("Accepted", response.Items[0].Status);
        Assert.Null(response.Items[0].Error);

        // Entity should be archived (soft-deleted)
        var archived = await context.Patients.FindAsync(patient.Id);
        Assert.NotNull(archived);
        Assert.True(archived.IsArchived);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_DeleteMissingPatient_IsIdempotentAccepted()
    {
        // Arrange: entity does not exist on server (already deleted or never synced)
        var context = CreateInMemoryContext();
        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);
        var missingId = Guid.NewGuid();

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "Patient",
                    ServerId = missingId,
                    LocalId = 7,
                    Operation = "Delete",
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: idempotent – treated as already applied, no conflict
        Assert.Equal(1, response.AcceptedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal("Accepted", response.Items[0].Status);
        Assert.Null(response.Items[0].Error);
    }

    [Fact]
    public async Task ReceiveClientPushAsync_UpdateArchivedPatient_IsDeletedConflict()
    {
        // Arrange: patient is archived/deleted on server; client tries to update it
        var context = CreateInMemoryContext();

        var patient = new Patient
        {
            FirstName = "Archived",
            LastName = "Patient",
            IsArchived = true,
            LastModifiedUtc = DateTime.UtcNow.AddHours(-1),
            ModifiedByUserId = Guid.NewGuid()
        };
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var syncEngine = new SyncEngine(context, NullLogger<SyncEngine>.Instance);

        var request = new ClientSyncPushRequest
        {
            Items = new List<ClientSyncPushItem>
            {
                new ClientSyncPushItem
                {
                    EntityType = "Patient",
                    ServerId = patient.Id,
                    LocalId = 3,
                    Operation = "Update",
                    DataJson = "{\"firstName\":\"Updated\",\"lastName\":\"Patient\"}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        // Act
        var response = await syncEngine.ReceiveClientPushAsync(request);

        // Assert: conflict because server-deleted beats client update
        Assert.Equal(0, response.AcceptedCount);
        Assert.Equal(1, response.ConflictCount);
        Assert.Equal("Conflict", response.Items[0].Status);
        Assert.NotNull(response.Items[0].Conflict);
    }
}
