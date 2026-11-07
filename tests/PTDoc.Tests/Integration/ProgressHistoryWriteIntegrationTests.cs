using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class ProgressHistoryWriteIntegrationTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, "version")]
    [InlineData(true, "version")]
    [InlineData(false, "partial")]
    [InlineData(true, "partial")]
    [InlineData(true, "omit")]
    [InlineData(true, "legacy")]
    [InlineData(false, "missing-schema")]
    [InlineData(true, "missing-schema")]
    public async Task GenericWrites_RejectInvalidOrLostProgressHistory_WithoutChangingSavedNote(bool update, string change)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(client);
        var original = CreatePayload();
        var originalJson = JsonSerializer.Serialize(original, JsonOptions);
        var modified = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        Guid? noteId = null;
        if (update)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var note = new ClinicalNote
            {
                PatientId = patientId,
                NoteType = NoteType.ProgressNote,
                ContentJson = originalJson,
                DateOfService = new DateTime(2026, 11, 6),
                LastModifiedUtc = modified
            };
            db.ClinicalNotes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
            modified = note.LastModifiedUtc;
        }

        var incoming = CreatePayload();
        if (change == "version") incoming.ProgressQuestionnaire.Activities = new() { Version = 99 };
        if (change == "partial") incoming.ProgressQuestionnaire.Activities = new() { Baseline = null! };
        if (change == "omit") incoming.ProgressQuestionnaire.PercentImproved = null;
        var content = change switch
        {
            "legacy" => "{\"subjective\":\"Older client edit\"}",
            "missing-schema" => "{\"subjective\":\"Legacy content\",\"progressQuestionnaire\":{\"percentImproved\":37.5}}",
            _ => JsonSerializer.Serialize(incoming, JsonOptions)
        };
        using var response = update
            ? await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest { ContentJson = content })
            : await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
            {
                PatientId = patientId,
                NoteType = NoteType.ProgressNote,
                DateOfService = new DateTime(2026, 11, 6),
                ContentJson = content
            });

        Assert.Equal(update ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notes = await verifyDb.ClinicalNotes.AsNoTracking().Where(note => note.PatientId == patientId).ToListAsync();
        if (update)
        {
            var saved = Assert.Single(notes);
            Assert.Equal(originalJson, saved.ContentJson);
            Assert.Equal(modified, saved.LastModifiedUtc);
        }
        else Assert.Empty(notes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericRead_AllowsHistoricalUnsupportedOrPartialProgress(bool partial)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(client);
        var payload = CreatePayload();
        payload.ProgressQuestionnaire.Activities = partial ? new() { Baseline = null! } : new() { Version = 99 };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        Guid noteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var note = new ClinicalNote { PatientId = patientId, NoteType = NoteType.ProgressNote, ContentJson = json };
            db.ClinicalNotes.Add(note);
            await db.SaveChangesAsync();
            noteId = note.Id;
        }
        using var response = await client.GetAsync($"/api/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<NoteDetailResponse>())!;
        Assert.NotNull(result.Note);
        var read = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(result.Note.ContentJson, JsonOptions)!;
        Assert.Equal(37.5m, read.ProgressQuestionnaire.PercentImproved);
        Assert.Equal(partial ? 1 : 99, read.ProgressQuestionnaire.Activities!.Version);
        if (partial) Assert.Null(read.ProgressQuestionnaire.Activities.Baseline);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var saved = await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ClinicalNotes.AsNoTracking()
            .SingleAsync(n => n.Id == noteId);
        Assert.Equal(json, saved.ContentJson);
    }

    private static NoteWorkspaceV2Payload CreatePayload() => new()
    {
        NoteType = NoteType.ProgressNote,
        ProgressQuestionnaire = new() { PercentImproved = 37.5m },
        Assessment = new() { DiagnosisCodes = [new() { Code = "M54.5", Description = "Low back pain" }] }
    };

    private static async Task<Guid> CreatePatientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Synthetic",
            LastName = "Progress",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = $"synthetic-{Guid.NewGuid():N}@example.test"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!.Id;
    }
}
