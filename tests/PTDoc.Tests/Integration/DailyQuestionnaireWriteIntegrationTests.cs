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
public sealed class DailyQuestionnaireWriteIntegrationTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false, "version")]
    [InlineData(true, "version")]
    [InlineData(false, "answer")]
    [InlineData(true, "answer")]
    [InlineData(false, "malformed")]
    [InlineData(true, "malformed")]
    [InlineData(true, "omit")]
    [InlineData(true, "legacy")]
    [InlineData(true, "null-section")]
    [InlineData(false, "missing-schema")]
    [InlineData(true, "missing-schema")]
    public async Task GenericWrites_RejectInvalidOrLostDailyQuestionnaire_WithoutChangingSavedNote(bool update, string change)
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
                NoteType = NoteType.Daily,
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
        incoming.DryNeedling = new(); // The variant must not exempt Daily validation.
        if (change == "version") incoming.DailyTreatment.Questionnaire!.Version = 99;
        if (change == "answer") incoming.DailyTreatment.Questionnaire!.HepAdherence = "invented";
        if (change == "omit") incoming.DailyTreatment.Questionnaire = null;
        var content = change switch
        {
            "legacy" => """{"subjective":"Older client edit"}""",
            "null-section" => """{"schemaVersion":2,"dailyTreatment":null}""",
            "malformed" => """{"schemaVersion":2,"dailyTreatment":{"questionnaire":"invalid"}}""",
            "missing-schema" => """{"subjective":"Legacy content","dailyTreatment":{"questionnaire":{"version":1,"hepAdherence":"Daily"}}}""",
            _ => JsonSerializer.Serialize(incoming, JsonOptions)
        };
        using var response = update
            ? await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest { ContentJson = content })
            : await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
            {
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = new DateTime(2026, 11, 6),
                ContentJson = content
            });

        Assert.Equal(update ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(change == "answer" ? "supported answer" : "Daily", await response.Content.ReadAsStringAsync());
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

    [Fact]
    public async Task GenericWrites_AllowValidDailyCreateEditAndMetadataOnlyUpdate()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var patientId = await CreatePatientAsync(client);
        var payload = CreatePayload();
        using var created = await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Daily,
            DateOfService = new DateTime(2026, 11, 6),
            ContentJson = JsonSerializer.Serialize(payload, JsonOptions)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var noteId = (await created.Content.ReadFromJsonAsync<NoteOperationResponse>())!.Note!.Id;
        payload.DailyTreatment.Questionnaire!.HepAdherence = "Rarely";
        using var edited = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest
        {
            ContentJson = JsonSerializer.Serialize(payload, JsonOptions)
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var metadataOnly = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest { DateOfService = new DateTime(2026, 11, 7) });
        Assert.Equal(HttpStatusCode.OK, metadataOnly.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var note = await db.ClinicalNotes.AsNoTracking().SingleAsync(note => note.Id == noteId);
        var saved = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, JsonOptions)!;
        Assert.Equal("Rarely", saved.DailyTreatment.Questionnaire!.HepAdherence);
        Assert.Equal("retain", saved.DailyTreatment.Questionnaire.ExtensionData!["futureAnswer"].GetString());
    }

    private static NoteWorkspaceV2Payload CreatePayload() => new()
    {
        NoteType = NoteType.Daily,
        DailyTreatment = new()
        {
            Questionnaire = new()
            {
                HepAdherence = "Daily",
                ExtensionData = new() { ["futureAnswer"] = JsonSerializer.SerializeToElement("retain") }
            }
        },
        Assessment = new() { DiagnosisCodes = [new() { Code = "M54.5", Description = "Low back pain" }] }
    };

    private static async Task<Guid> CreatePatientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Synthetic",
            LastName = "Daily",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = $"synthetic-{Guid.NewGuid():N}@example.test"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PatientResponse>())!.Id;
    }
}
