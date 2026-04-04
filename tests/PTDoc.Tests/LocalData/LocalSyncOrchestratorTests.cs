using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PTDoc.Application.LocalData;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.LocalData;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PTDoc.Tests.LocalData;

/// <summary>
/// Tests for <see cref="LocalSyncOrchestrator"/> covering Sprint H acceptance criteria:
///  - Pending local changes can be enqueued and pushed
///  - Failed push items remain retryable (SyncState.Pending preserved)
///  - Pulled server changes are applied into the local database
///  - Conflicts are detected and marked safely (no silent overwrite)
///  - Pull and push watermarks are updated
/// </summary>
public class LocalSyncOrchestratorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static LocalDbContext CreateInMemoryLocalContext()
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LocalDbContext(options);
    }

    private static HttpClient CreateMockHttpClient(
        HttpStatusCode statusCode,
        object? responseBody = null)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = responseBody is null
                    ? new StringContent("{}")
                    : JsonContent.Create(responseBody)
            });

        return new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("http://localhost:5170")
        };
    }

    private static LocalPatientSummary MakePendingPatient(string firstName = "Alice", string lastName = "Smith")
        => new()
        {
            ServerId = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };

    private static LocalPatientSummary MakeSyncedPatient(string firstName = "Alice", string lastName = "Smith")
        => new()
        {
            ServerId = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            SyncState = SyncState.Synced,
            LastModifiedUtc = DateTime.UtcNow,
            LastSyncedUtc = DateTime.UtcNow
        };

    private static LocalAppointmentSummary MakePendingAppointment(Guid patientServerId)
        => new()
        {
            ServerId = Guid.NewGuid(),
            PatientServerId = patientServerId,
            PatientFirstName = "Alice",
            PatientLastName = "Smith",
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };

    // ── GetPendingCountAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingCountAsync_ReturnsZero_WhenNoPendingEntities()
    {
        var ctx = CreateInMemoryLocalContext();
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK), NullLogger<LocalSyncOrchestrator>.Instance);

        var count = await orch.GetPendingCountAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCorrectCount_AcrossEntityTypes()
    {
        var ctx = CreateInMemoryLocalContext();
        ctx.PatientSummaries.Add(MakePendingPatient());
        ctx.PatientSummaries.Add(MakePendingPatient("Bob", "Jones"));
        var appt = MakePendingAppointment(Guid.NewGuid());
        ctx.AppointmentSummaries.Add(appt);
        // Add a synced patient that should NOT be counted
        ctx.PatientSummaries.Add(MakeSyncedPatient("C", "D"));
        await ctx.SaveChangesAsync();

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK), NullLogger<LocalSyncOrchestrator>.Instance);

        var count = await orch.GetPendingCountAsync();

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task GetPendingCountAsync_IncludesConflictEntities()
    {
        var ctx = CreateInMemoryLocalContext();
        ctx.PatientSummaries.Add(MakePendingPatient());
        ctx.PatientSummaries.Add(new LocalPatientSummary
        {
            ServerId = Guid.NewGuid(),
            FirstName = "Conflict",
            LastName = "Patient",
            SyncState = SyncState.Conflict, // conflict, also needs attention
            LastModifiedUtc = DateTime.UtcNow
        });
        ctx.PatientSummaries.Add(MakeSyncedPatient("Synced", "Patient")); // should NOT count
        await ctx.SaveChangesAsync();

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK), NullLogger<LocalSyncOrchestrator>.Instance);

        var count = await orch.GetPendingCountAsync();

        Assert.Equal(2, count); // 1 Pending + 1 Conflict
    }

    // ── PushPendingAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PushPendingAsync_ReturnsEmptyResult_WhenNoPendingItems()
    {
        var ctx = CreateInMemoryLocalContext();
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(0, result.PushedCount);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task PushPendingAsync_MarksEntitiesSynced_OnServerAccept()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        // Build server response that accepts all items
        var serverResponse = new ClientSyncPushResponse
        {
            AcceptedCount = 1,
            Items = new List<ClientSyncPushItemResult>
            {
                new() { EntityType = "Patient", LocalId = patient.LocalId, ServerId = patient.ServerId, Status = "Accepted", ServerModifiedUtc = DateTime.UtcNow }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(1, result.PushedCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);

        // Entity should now be Synced
        await ctx.Entry(patient).ReloadAsync();
        Assert.Equal(SyncState.Synced, patient.SyncState);
        Assert.NotNull(patient.LastSyncedUtc);
    }

    [Fact]
    public async Task PushPendingAsync_LeavesEntitiesPending_OnNetworkError()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5170") };
        var orch = new LocalSyncOrchestrator(ctx, httpClient, NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(1, result.PushedCount);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);
        Assert.NotEmpty(result.Errors);

        // Entity must remain Pending for retry
        await ctx.Entry(patient).ReloadAsync();
        Assert.Equal(SyncState.Pending, patient.SyncState);
    }

    [Fact]
    public async Task PushPendingAsync_LeavesEntitiesPending_OnServerError()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.InternalServerError), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(1, result.PushedCount);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);

        await ctx.Entry(patient).ReloadAsync();
        Assert.Equal(SyncState.Pending, patient.SyncState);
    }

    [Fact]
    public async Task PushPendingAsync_MarksConflict_WhenServerReportsConflict()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        var serverResponse = new ClientSyncPushResponse
        {
            ConflictCount = 1,
            Items = new List<ClientSyncPushItemResult>
            {
                new() { EntityType = "Patient", LocalId = patient.LocalId, ServerId = patient.ServerId, Status = "Conflict", Error = "Server version is newer" }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.ConflictCount);

        await ctx.Entry(patient).ReloadAsync();
        Assert.Equal(SyncState.Conflict, patient.SyncState);
    }

    [Fact]
    public async Task PushPendingAsync_UpdatesSyncMetadata_OnSuccess()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        var serverResponse = new ClientSyncPushResponse
        {
            AcceptedCount = 1,
            Items = new List<ClientSyncPushItemResult>
            {
                new() { EntityType = "Patient", LocalId = patient.LocalId, ServerId = patient.ServerId, Status = "Accepted", ServerModifiedUtc = DateTime.UtcNow }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        await orch.PushPendingAsync();

        var meta = await ctx.SyncMetadata.FirstOrDefaultAsync(m => m.EntityType == "Patient");
        Assert.NotNull(meta);
        Assert.NotNull(meta.LastPushedAt);
    }

    [Fact]
    public async Task PushPendingAsync_IncludesAddendumMetadata_ForClinicalNotes()
    {
        var ctx = CreateInMemoryLocalContext();
        var parentNoteId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow.AddHours(-2);
        var note = new LocalClinicalNoteDraft
        {
            ServerId = Guid.NewGuid(),
            PatientServerId = Guid.NewGuid(),
            NoteType = "Daily",
            DateOfService = DateTime.UtcNow.Date,
            CreatedUtc = createdUtc,
            ParentNoteId = parentNoteId,
            IsAddendum = true,
            ContentJson = "{\"text\":\"addendum\"}",
            CptCodesJson = "[]",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };
        ctx.ClinicalNoteDrafts.Add(note);
        await ctx.SaveChangesAsync();

        string? requestBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (request, _) =>
            {
                requestBody = await request.Content!.ReadAsStringAsync();
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(new ClientSyncPushResponse
                    {
                        AcceptedCount = 1,
                        Items =
                        [
                            new ClientSyncPushItemResult
                            {
                                EntityType = "ClinicalNote",
                                LocalId = note.LocalId,
                                ServerId = note.ServerId,
                                Status = "Accepted",
                                ServerModifiedUtc = DateTime.UtcNow
                            }
                        ]
                    })
                };
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5170") };
        var orch = new LocalSyncOrchestrator(ctx, httpClient, NullLogger<LocalSyncOrchestrator>.Instance);

        await orch.PushPendingAsync();

        Assert.False(string.IsNullOrWhiteSpace(requestBody));
        var pushRequest = JsonSerializer.Deserialize<ClientSyncPushRequest>(requestBody!, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        var pushedItem = Assert.Single(pushRequest!.Items);
        using var doc = JsonDocument.Parse(pushedItem.DataJson);
        var root = doc.RootElement;

        Assert.Equal(createdUtc.ToString("O"), root.GetProperty("createdUtc").GetString());
        Assert.Equal(parentNoteId.ToString(), root.GetProperty("parentNoteId").GetString());
        Assert.True(root.GetProperty("isAddendum").GetBoolean());
    }

    // ── PullChangesAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PullChangesAsync_ReturnsEmpty_WhenServerReturnsNoItems()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverResponse = new ClientSyncPullResponse { Items = new List<ClientSyncPullItem>(), SyncedAt = DateTime.UtcNow };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(0, result.PulledCount);
        Assert.Equal(0, result.AppliedCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task PullChangesAsync_PopulatesAddendumMetadata_WhenPullingClinicalNote()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var parentNoteId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow.AddDays(-1);
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items =
            [
                new ClientSyncPullItem
                {
                    EntityType = "ClinicalNote",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        PatientId = patientId,
                        NoteType = NoteType.Daily,
                        DateOfService = DateTime.UtcNow.Date,
                        CreatedUtc = createdUtc,
                        ParentNoteId = parentNoteId,
                        IsAddendum = true,
                        ContentJson = "{\"text\":\"server addendum\"}",
                        CptCodesJson = "[]",
                        LastModifiedUtc = DateTime.UtcNow
                    }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            ]
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.AppliedCount);
        var local = await ctx.ClinicalNoteDrafts.FirstOrDefaultAsync(n => n.ServerId == serverId);
        Assert.NotNull(local);
        Assert.Equal(parentNoteId, local!.ParentNoteId);
        Assert.True(local.IsAddendum);
        Assert.Equal(createdUtc, local.CreatedUtc);
    }

    [Fact]
    public async Task PullChangesAsync_InsertsNewPatient_WhenNotInLocalDb()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new { FirstName = "Jane", LastName = "Doe", LastModifiedUtc = DateTime.UtcNow }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.PulledCount);
        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(0, result.ConflictCount);

        var local = await ctx.PatientSummaries.FirstOrDefaultAsync(p => p.ServerId == serverId);
        Assert.NotNull(local);
        Assert.Equal("Jane", local.FirstName);
        Assert.Equal(SyncState.Synced, local.SyncState);
    }

    [Fact]
    public async Task PullChangesAsync_UpdatesExistingSyncedPatient()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var existingPatient = new LocalPatientSummary
        {
            ServerId = serverId,
            FirstName = "Old",
            LastName = "Name",
            SyncState = SyncState.Synced,
            LastModifiedUtc = DateTime.UtcNow.AddMinutes(-10),
            LastSyncedUtc = DateTime.UtcNow.AddMinutes(-10)
        };
        ctx.PatientSummaries.Add(existingPatient);
        await ctx.SaveChangesAsync();

        var newerModified = DateTime.UtcNow;
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new { FirstName = "New", LastName = "Name", LastModifiedUtc = newerModified }),
                    LastModifiedUtc = newerModified
                }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.AppliedCount);
        Assert.Equal(0, result.ConflictCount);

        await ctx.Entry(existingPatient).ReloadAsync();
        Assert.Equal("New", existingPatient.FirstName);
        Assert.Equal(SyncState.Synced, existingPatient.SyncState);
    }

    [Fact]
    public async Task PullChangesAsync_InsertsNewIntakeForm_WithStructuredDataJson()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var modifiedUtc = DateTime.UtcNow;
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "IntakeForm",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        PatientId = patientId,
                        ResponseJson = "{}",
                        StructuredDataJson = "{\"schemaVersion\":\"2026-03-30\",\"bodyPartSelections\":[{\"bodyPartId\":\"knee\",\"lateralities\":[\"left\"]}]}",
                        PainMapData = "{\"selectedRegions\":[\"knee-left\"]}",
                        Consents = "{}",
                        TemplateVersion = "1.0",
                        IsLocked = false,
                        SubmittedAt = (DateTime?)null
                    }),
                    LastModifiedUtc = modifiedUtc
                }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.PulledCount);
        Assert.Equal(1, result.AppliedCount);

        var local = await ctx.IntakeFormDrafts.FirstOrDefaultAsync(form => form.ServerId == serverId);
        Assert.NotNull(local);
        Assert.Equal(patientId, local!.PatientServerId);
        Assert.Contains("bodyPartSelections", local.StructuredDataJson);
        Assert.Equal(SyncState.Synced, local.SyncState);
    }

    [Fact]
    public async Task PullChangesAsync_MarksConflict_WhenLocalIsPendingAndServerIsOlderOrSameAge()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var localPatient = new LocalPatientSummary
        {
            ServerId = serverId,
            FirstName = "LocalEdited",
            LastName = "Patient",
            SyncState = SyncState.Pending, // locally modified
            LastModifiedUtc = now
        };
        ctx.PatientSummaries.Add(localPatient);
        await ctx.SaveChangesAsync();

        // Server sends the same or older timestamp → conflict (local wins or needs review)
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new { FirstName = "ServerEdited", LastName = "Patient", LastModifiedUtc = now }),
                    LastModifiedUtc = now // same timestamp → conflict
                }
            }
        };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.ConflictCount);
        Assert.Equal(0, result.AppliedCount);

        await ctx.Entry(localPatient).ReloadAsync();
        // Local data must be preserved — no silent overwrite
        Assert.Equal("LocalEdited", localPatient.FirstName);
        Assert.Equal(SyncState.Conflict, localPatient.SyncState);
    }

    [Fact]
    public async Task PullChangesAsync_UpdatesPullWatermark()
    {
        var ctx = CreateInMemoryLocalContext();
        var syncedAt = DateTime.UtcNow;
        var serverResponse = new ClientSyncPullResponse { Items = new List<ClientSyncPullItem>(), SyncedAt = syncedAt };
        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        await orch.PullChangesAsync();

        var meta = await ctx.SyncMetadata.FirstOrDefaultAsync(m => m.EntityType == "Patient");
        Assert.NotNull(meta);
        Assert.NotNull(meta.LastPulledAt);
    }

    [Fact]
    public async Task PullChangesAsync_ReturnsErrors_OnNetworkFailure()
    {
        var ctx = CreateInMemoryLocalContext();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("timeout"));

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5170") };
        var orch = new LocalSyncOrchestrator(ctx, httpClient, NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.PulledCount);
    }

    // ── SyncAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_ExecutesPushThenPull_AndReturnsSummary()
    {
        var ctx = CreateInMemoryLocalContext();
        var patient = MakePendingPatient();
        ctx.PatientSummaries.Add(patient);
        await ctx.SaveChangesAsync();

        // Server accepts push and returns one item on pull
        var pushResponse = new ClientSyncPushResponse
        {
            AcceptedCount = 1,
            Items = new List<ClientSyncPushItemResult>
            {
                new() { EntityType = "Patient", LocalId = patient.LocalId, ServerId = patient.ServerId, Status = "Accepted", ServerModifiedUtc = DateTime.UtcNow }
            }
        };
        var pullResponse = new ClientSyncPullResponse { Items = new List<ClientSyncPullItem>(), SyncedAt = DateTime.UtcNow };

        // First HTTP call is push (POST), second is pull (GET)
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                var body = callCount == 1
                    ? (object)pushResponse
                    : pullResponse;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = JsonContent.Create(body)
                };
            });

        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5170") };
        var orch = new LocalSyncOrchestrator(ctx, httpClient, NullLogger<LocalSyncOrchestrator>.Instance);

        var summary = await orch.SyncAsync();

        Assert.NotNull(summary);
        Assert.Equal(1, summary.Push.SuccessCount);
        Assert.Equal(0, summary.Pull.PulledCount);
        Assert.True(summary.Duration.TotalMilliseconds >= 0);
    }

    // ── ServerId = Guid.Empty (new record create path) ────────────────────────────

    [Fact]
    public async Task PushPendingAsync_UpdatesLocalServerId_WhenNewRecordAccepted()
    {
        var ctx = CreateInMemoryLocalContext();
        var newPatient = new LocalPatientSummary
        {
            ServerId = Guid.Empty, // never synced — no server ID yet
            FirstName = "New",
            LastName = "Record",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };
        ctx.PatientSummaries.Add(newPatient);
        await ctx.SaveChangesAsync();

        var assignedServerId = Guid.NewGuid();
        var serverResponse = new ClientSyncPushResponse
        {
            AcceptedCount = 1,
            Items = new List<ClientSyncPushItemResult>
            {
                new()
                {
                    EntityType = "Patient",
                    LocalId = newPatient.LocalId,
                    ServerId = assignedServerId, // server assigns a new ID
                    Status = "Accepted",
                    ServerModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(1, result.SuccessCount);

        await ctx.Entry(newPatient).ReloadAsync();
        Assert.Equal(SyncState.Synced, newPatient.SyncState);
        Assert.Equal(assignedServerId, newPatient.ServerId); // local ServerId updated from server response
    }

    // ── Watermark not advanced on apply errors ────────────────────────────────────

    [Fact]
    public async Task PullChangesAsync_DoesNotAdvanceWatermark_WhenItemApplyFails()
    {
        var ctx = CreateInMemoryLocalContext();

        // Seed existing metadata with a known watermark
        var existingWatermark = DateTime.UtcNow.AddHours(-1);
        ctx.SyncMetadata.Add(new LocalSyncMetadata { EntityType = "Patient", LastPulledAt = existingWatermark });
        ctx.SyncMetadata.Add(new LocalSyncMetadata { EntityType = "Appointment", LastPulledAt = existingWatermark });
        await ctx.SaveChangesAsync();

        // Server returns an item with malformed DataJson that will fail to apply
        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = Guid.NewGuid(),
                    Operation = "Upsert",
                    DataJson = "THIS IS NOT VALID JSON",  // will throw during JsonDocument.Parse
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.NotEmpty(result.Errors);

        // Watermark must NOT be advanced past the failed item
        var meta = await ctx.SyncMetadata.FirstOrDefaultAsync(m => m.EntityType == "Patient");
        Assert.NotNull(meta);
        Assert.Equal(existingWatermark, meta.LastPulledAt);
    }

    // ── Delete conflict protection ────────────────────────────────────────────────

    [Fact]
    public async Task PullChangesAsync_MarksConflict_WhenPendingPatientReceivesDelete()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var pendingPatient = new LocalPatientSummary
        {
            ServerId = serverId,
            FirstName = "Edited",
            LastName = "Patient",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };
        ctx.PatientSummaries.Add(pendingPatient);
        await ctx.SaveChangesAsync();

        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = serverId,
                    Operation = "Delete", // server deleted while client has Pending local edits
                    DataJson = "{}",
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PullChangesAsync();

        Assert.Equal(1, result.ConflictCount);

        // Local record must still exist — not silently deleted
        await ctx.Entry(pendingPatient).ReloadAsync();
        Assert.Equal(SyncState.Conflict, pendingPatient.SyncState);
        Assert.Equal("Edited", pendingPatient.FirstName);
    }

    // ── DateOfBirth pulled from server ───────────────────────────────────────────

    [Fact]
    public async Task PullChangesAsync_PopulatesDateOfBirth_WhenPullingNewPatient()
    {
        var ctx = CreateInMemoryLocalContext();
        var serverId = Guid.NewGuid();
        var dob = new DateTime(1990, 5, 15, 0, 0, 0, DateTimeKind.Utc);

        var serverResponse = new ClientSyncPullResponse
        {
            SyncedAt = DateTime.UtcNow,
            Items = new List<ClientSyncPullItem>
            {
                new()
                {
                    EntityType = "Patient",
                    ServerId = serverId,
                    Operation = "Upsert",
                    DataJson = JsonSerializer.Serialize(new
                    {
                        FirstName = "DOB",
                        LastName = "Test",
                        DateOfBirth = dob,
                        LastModifiedUtc = DateTime.UtcNow
                    }),
                    LastModifiedUtc = DateTime.UtcNow
                }
            }
        };

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        await orch.PullChangesAsync();

        var local = await ctx.PatientSummaries.FirstOrDefaultAsync(p => p.ServerId == serverId);
        Assert.NotNull(local);
        Assert.NotNull(local.DateOfBirth);
        Assert.Equal(dob.Year, local.DateOfBirth!.Value.Year);
        Assert.Equal(dob.Month, local.DateOfBirth!.Value.Month);
        Assert.Equal(dob.Day, local.DateOfBirth!.Value.Day);
    }

    // ── LocalId correlation via (EntityType, LocalId) ─────────────────────────────

    [Fact]
    public async Task PushPendingAsync_CorrectlyCorrelates_WhenPatientAndAppointmentHaveSameLocalId()
    {
        var ctx = CreateInMemoryLocalContext();

        // Use ServerId=Guid.Empty for the appointment so the server assigns a new one,
        // letting us verify the correct entity received the correct assigned ID.
        var patient = MakePendingPatient("Alice", "Smith");
        var appointment = new LocalAppointmentSummary
        {
            ServerId = Guid.Empty, // new record — server will assign ID
            PatientServerId = Guid.NewGuid(),
            PatientFirstName = "Alice",
            PatientLastName = "Smith",
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };
        ctx.PatientSummaries.Add(patient);
        ctx.AppointmentSummaries.Add(appointment);
        await ctx.SaveChangesAsync();

        var assignedAppointmentServerId = Guid.NewGuid();
        var patientServerId = patient.ServerId;

        var serverResponse = new ClientSyncPushResponse
        {
            AcceptedCount = 2,
            Items = new List<ClientSyncPushItemResult>
            {
                // Both items may share the same LocalId integer across tables — EntityType disambiguates
                new() { EntityType = "Patient", LocalId = patient.LocalId, ServerId = patientServerId, Status = "Accepted", ServerModifiedUtc = DateTime.UtcNow },
                new() { EntityType = "Appointment", LocalId = appointment.LocalId, ServerId = assignedAppointmentServerId, Status = "Accepted", ServerModifiedUtc = DateTime.UtcNow }
            }
        };

        var orch = new LocalSyncOrchestrator(ctx, CreateMockHttpClient(HttpStatusCode.OK, serverResponse), NullLogger<LocalSyncOrchestrator>.Instance);

        var result = await orch.PushPendingAsync();

        Assert.Equal(2, result.SuccessCount);

        await ctx.Entry(patient).ReloadAsync();
        await ctx.Entry(appointment).ReloadAsync();

        // Patient stays Synced with original ServerId
        Assert.Equal(SyncState.Synced, patient.SyncState);
        Assert.Equal(patientServerId, patient.ServerId);

        // Appointment is Synced with newly assigned ServerId (not the patient's ID)
        Assert.Equal(SyncState.Synced, appointment.SyncState);
        Assert.Equal(assignedAppointmentServerId, appointment.ServerId);
    }
}
