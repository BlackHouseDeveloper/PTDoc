using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeIntegrationTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    [Fact]
    public async Task StaffUpdates_RequireIssuedVersion_PreserveAssociation_AndRejectStaleSubmit()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, email) = await CreateFixtureAsync(client);
        var issued = await IssueAsync(client, patientId, noteId);
        Assert.Equal(noteId, issued.EncounterNoteId);
        Assert.Equal(IntakeQuestionnaireDefinitions.EvaluationFullV1, issued.QuestionnaireDefinitionId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/intake/patient/{patientId}/latest")).StatusCode);
        foreach (var version in new DateTime?[] { null, issued.LastModifiedUtc.AddSeconds(-1) })
        {
            using var rejected = await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(version));
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        var oldFlow = Update(issued.LastModifiedUtc);
        oldFlow.ResponseJson = """{"intakeFlowVersion":1}""";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", oldFlow)).StatusCode);
        using var saved = await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(issued.LastModifiedUtc));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var current = (await saved.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var json = JsonDocument.Parse(current.ResponseJson);
        Assert.Equal(noteId, json.RootElement.GetProperty("encounterNoteId").GetGuid());
        Assert.Equal(patientId, json.RootElement.GetProperty("patientId").GetGuid());
        Assert.Equal("retained", json.RootElement.GetProperty("futureExtension").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/intake/{issued.Id}/submit", new SubmitIntakeRequest { ExpectedLastModifiedUtc = issued.LastModifiedUtc })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/intake/{issued.Id}/submit", new SubmitIntakeRequest { ExpectedLastModifiedUtc = current.LastModifiedUtc })).StatusCode);
        var locked = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{issued.Id}"))!;
        Assert.True(locked.Locked);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(locked.LastModifiedUtc))).StatusCode);
    }

    [Fact]
    public async Task VerifiedPatientLink_RemainsPinnedAcrossLaterVisits_AndCannotWriteSibling()
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        using var patient = factory.CreateUnauthenticatedClient();
        var (patientId, noteId, email) = await CreateFixtureAsync(staff);
        var first = await IssueAsync(staff, patientId, noteId);
        using var linkResponse = await staff.PostAsync($"/api/v1/intake/{first.Id}/delivery/link", null);
        Assert.Equal(HttpStatusCode.OK, linkResponse.StatusCode);
        var link = (await linkResponse.Content.ReadFromJsonAsync<IntakeDeliveryBundleResponse>())!;
        var inviteToken = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link.InviteUrl).Query)["invite"].ToString();
        using var sent = await patient.PostAsJsonAsync("/api/v1/intake/access/send-otp", new SendIntakeOtpRequest { InviteToken = inviteToken, Contact = email, Channel = OtpChannel.Email });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var verified = await patient.PostAsJsonAsync("/api/v1/intake/access/verify-otp", new VerifyIntakeOtpRequest { InviteToken = inviteToken, Contact = email, Channel = OtpChannel.Email, OtpCode = factory.LastIntakeOtpCode! });
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var access = (await verified.Content.ReadFromJsonAsync<IntakeInviteResult>())!;
        Assert.True(access.IsValid);
        patient.DefaultRequestHeaders.Add(IntakeAccessHeaders.AccessToken, access.AccessToken);
        Guid laterNoteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var laterNote = new ClinicalNote { PatientId = patientId, NoteType = NoteType.Evaluation, IsReEvaluation = true, LastModifiedUtc = DateTime.UtcNow };
            db.ClinicalNotes.Add(laterNote);
            await db.SaveChangesAsync();
            laterNoteId = laterNote.Id;
        }
        var later = await IssueAsync(staff, patientId, laterNoteId);
        Assert.NotEqual(first.Id, later.Id);
        var linked = (await patient.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/access/patient/{patientId}/latest"))!;
        Assert.Equal(first.Id, linked.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.PutAsJsonAsync($"/api/v1/intake/access/{later.Id}", Update(later.LastModifiedUtc))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await patient.PutAsJsonAsync($"/api/v1/intake/access/{first.Id}", Update(null))).StatusCode);
        using var update = await patient.PutAsJsonAsync($"/api/v1/intake/access/{first.Id}", Update(first.LastModifiedUtc));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await patient.PostAsJsonAsync($"/api/v1/intake/access/{first.Id}/submit", new SubmitIntakeRequest { ExpectedLastModifiedUtc = first.LastModifiedUtc })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await patient.PostAsJsonAsync($"/api/v1/intake/access/{first.Id}/submit", new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc })).StatusCode);
        Assert.False((await staff.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{later.Id}"))!.Locked);
        Assert.Equal(first.Id, (await patient.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/access/patient/{patientId}/latest"))!.Id);
    }

    [Fact]
    public async Task ConcurrentVersionedWrites_OnlyOneWins_AndFinalizedNoteRejectsChanges()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client);
        var issued = await IssueAsync(client, patientId, noteId);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(issued.LastModifiedUtc))));
        Assert.Single(responses, result => result.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, result => result.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var note = await db.ClinicalNotes.FindAsync(noteId);
            note!.SignedUtc = DateTime.UtcNow;
            note.SignatureHash = "synthetic-signed-evidence";
            await db.SaveChangesAsync();
        }
        var current = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{issued.Id}"))!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(current.LastModifiedUtc))).StatusCode);
    }

    private static UpdateIntakeRequest Update(DateTime? version) => new()
    {
        ExpectedLastModifiedUtc = version,
        Consents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"termsOfServiceAccepted":true}""",
        ResponseJson = """{"intakeFlowVersion":2,"fullName":"Synthetic Encounter","medicalHistoryNotes":"Current visit answer","encounterNoteId":"00000000-0000-0000-0000-000000000999","patientId":"00000000-0000-0000-0000-000000000999","futureExtension":"retained"}"""
    };

    private static async Task<IntakeResponse> IssueAsync(HttpClient client, Guid patientId, Guid noteId)
    {
        using var response = await client.PostAsJsonAsync($"/api/v1/intake/drafts/{patientId}", new EnsureIntakeDraftRequest { EncounterNoteId = noteId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IntakeResponse>())!;
    }

    private async Task<(Guid Patient, Guid Note, string Email)> CreateFixtureAsync(HttpClient client)
    {
        var email = $"synthetic-{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest { FirstName = "Synthetic", LastName = "Encounter", DateOfBirth = new DateTime(1990, 1, 1), Email = email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patient = (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var note = new ClinicalNote { PatientId = patient.Id, NoteType = NoteType.Evaluation, LastModifiedUtc = DateTime.UtcNow };
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync();
        return (patient.Id, note.Id, email);
    }
}
