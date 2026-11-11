using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeIntegrationTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    [Fact]
    public async Task InvalidProgressCreation_ReturnsValidationInsteadOfServerError()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, _, _) = await CreateFixtureAsync(client);
        using var response = await client.PostAsJsonAsync("/api/v1/notes/", new CreateNoteRequest
        {
            PatientId = patientId,
            DateOfService = new DateTime(2026, 11, 4),
            NoteType = NoteType.ProgressNote,
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote,
                ProgressQuestionnaire = new() { PercentImproved = 101 }
            })
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("Percent improved", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EncounterRead_EnforcesPatientOwnership_AndPreservesStaffAccess()
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(staff);
        var issued = await IssueAsync(staff, patientId, noteId);
        var (otherPatientId, otherNoteId, _) = await CreateFixtureAsync(staff);
        await IssueAsync(staff, otherPatientId, otherNoteId);
        using var patient = factory.CreateClientWithRole(Roles.Patient);
        patient.DefaultRequestHeaders.Add("X-Test-Patient-Id", patientId.ToString());

        var own = await patient.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/patient/{patientId}/note/{noteId}");
        Assert.Equal(issued.Id, own!.Id);
        using var rejected = await patient.GetAsync($"/api/v1/intake/patient/{otherPatientId}/note/{otherNoteId}");
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var allowed = await staff.GetAsync($"/api/v1/intake/patient/{otherPatientId}/note/{otherNoteId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using var unmappedPatient = factory.CreateClientWithRole(Roles.Patient);
        using var unmapped = await unmappedPatient.GetAsync($"/api/v1/intake/patient/{patientId}/note/{noteId}");
        Assert.Equal(HttpStatusCode.Forbidden, unmapped.StatusCode);
    }

    [Theory]
    [InlineData(Roles.PT, HttpStatusCode.OK)]
    [InlineData(Roles.PTA, HttpStatusCode.OK)]
    [InlineData(Roles.Admin, HttpStatusCode.OK)]
    [InlineData(Roles.Owner, HttpStatusCode.OK)]
    [InlineData(Roles.Billing, HttpStatusCode.Forbidden)]
    [InlineData(Roles.FrontDesk, HttpStatusCode.Forbidden)]
    [InlineData(Roles.Patient, HttpStatusCode.Forbidden)]
    public async Task EncounterSeed_RequiresBothNoteAndIntakeRead(string role, HttpStatusCode expected)
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(staff);
        var issued = await IssueAsync(staff, patientId, noteId);
        using var update = await staff.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", Update(issued.LastModifiedUtc));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var saved = (await update.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var submit = await staff.PostAsJsonAsync($"/api/v1/intake/{issued.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        using var reader = factory.CreateClientWithRole(role);
        using var response = await reader.GetAsync($"/api/v2/notes/workspace/{patientId}/{noteId}/intake-seed");
        Assert.Equal(expected, response.StatusCode);
        if (role == Roles.Billing)
        {
            using var noteRead = await reader.GetAsync($"/api/v2/notes/workspace/{patientId}/{noteId}");
            Assert.Equal(HttpStatusCode.OK, noteRead.StatusCode);
        }
    }

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

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.Daily)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public async Task VerifiedPatientLink_RemainsPinnedAcrossLaterVisits_AndCannotWriteSibling(NoteType type)
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        using var patient = factory.CreateUnauthenticatedClient();
        var (patientId, noteId, email) = await CreateFixtureAsync(staff, type);
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
        using var update = await patient.PutAsJsonAsync($"/api/v1/intake/access/{first.Id}", UpdateFor(first, first.LastModifiedUtc));
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LegacyNoteCreation_LeavesEncounterDraftEditable_AndLocksOnlyPatientWideDraft(bool reevaluation, bool includeLegacyDraft)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client);
        Guid? legacyId = null;
        if (includeLegacyDraft)
        {
            using var legacyResponse = await client.PostAsJsonAsync($"/api/v1/intake/drafts/{patientId}", new EnsureIntakeDraftRequest());
            Assert.Equal(HttpStatusCode.Created, legacyResponse.StatusCode);
            legacyId = (await legacyResponse.Content.ReadFromJsonAsync<IntakeResponse>())!.Id;
        }
        var encounter = await IssueAsync(client, patientId, noteId);
        // Make the unfinished encounter form unambiguously the latest form.
        using var initialSave = await client.PutAsJsonAsync($"/api/v1/intake/{encounter.Id}", Update(encounter.LastModifiedUtc));
        Assert.Equal(HttpStatusCode.OK, initialSave.StatusCode);
        encounter = (await initialSave.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var createNote = await client.PostAsJsonAsync("/api/v1/notes", new CreateNoteRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            IsReEvaluation = reevaluation,
            DateOfService = DateTime.UtcNow,
            ContentJson = "{}",
            CptCodesJson = "[]"
        });
        Assert.Equal(HttpStatusCode.Created, createNote.StatusCode);
        var noteB = (await createNote.Content.ReadFromJsonAsync<NoteOperationResponse>())!.Note!;
        Assert.NotEqual(noteId, noteB.Id);
        var unchanged = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{encounter.Id}"))!;
        Assert.False(unchanged.Locked);
        Assert.Equal(encounter.LastModifiedUtc, unchanged.LastModifiedUtc);
        Assert.Equal(encounter.ResponseJson, unchanged.ResponseJson);
        if (legacyId.HasValue)
            Assert.True((await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{legacyId}"))!.Locked);
        using var save = await client.PutAsJsonAsync($"/api/v1/intake/{encounter.Id}", Update(unchanged.LastModifiedUtc));
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = (await save.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var submit = await client.PostAsJsonAsync($"/api/v1/intake/{encounter.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
    }

    [Theory]
    [InlineData(NoteType.Daily)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public async Task VisitQuestionnaire_SaveSubmitAndMatchingSeed_PreserveChartAndClinicianContent(NoteType type)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, email) = await CreateFixtureAsync(client);
        string originalNote;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var note = (await db.ClinicalNotes.FindAsync(noteId))!;
            note.NoteType = type;
            note.ContentJson = originalNote = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                NoteType = type,
                Plan = new() { HomeExerciseProgramNotes = "Keep clinician instructions", PrimaryDischargeReason = "Patient request" },
                DailyTreatment = new() { ResponseToTreatment = "Clinician observation" }
            });
            await db.SaveChangesAsync();
        }
        var issued = await IssueAsync(client, patientId, noteId);
        Assert.Equal(IntakeQuestionnaireDefinitions.ForNoteType(type), issued.QuestionnaireDefinitionId);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(issued.ResponseJson, options)!;
        Assert.NotNull(draft.VisitQuestionnaire);
        Assert.Null(draft.VisitQuestionnaire.CurrentPain);
        draft.VisitQuestionnaire.CurrentPain = 0;
        draft.VisitQuestionnaire.BestPain = 0;
        draft.VisitQuestionnaire.WorstPain = 4;
        if (type == NoteType.Daily) draft.VisitQuestionnaire.Daily!.AdditionalInformation = "Daily patient answer";
        if (type == NoteType.ProgressNote) draft.VisitQuestionnaire.Progress!.PercentImproved = 37.5m;
        if (type == NoteType.Discharge) draft.VisitQuestionnaire.Discharge!.GoalsMetStatus = "Patient report only";
        draft.FullName = "Untrusted Chart Change";
        draft.EmailAddress = "untrusted@example.test";
        var update = new UpdateIntakeRequest
        {
            ExpectedLastModifiedUtc = issued.LastModifiedUtc,
            ResponseJson = JsonSerializer.Serialize(draft, options),
            Consents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"termsOfServiceAccepted":true}"""
        };
        using var savedResponse = await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", update);
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var saved = (await savedResponse.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", update)).StatusCode);
        var reloaded = JsonSerializer.Deserialize<IntakeResponseDraft>(saved.ResponseJson, options)!;
        Assert.Equal(0, reloaded.VisitQuestionnaire!.CurrentPain);
        Assert.Equal(email, reloaded.EmailAddress);
        using var submit = await client.PostAsJsonAsync($"/api/v1/intake/{issued.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var seed = await client.GetFromJsonAsync<NoteWorkspaceV2EvaluationSeedResponse>($"/api/v2/notes/workspace/{patientId}/{noteId}/intake-seed");
        Assert.NotNull(seed);
        Assert.Equal(issued.Id, seed.SourceIntakeId);
        Assert.Equal(issued.QuestionnaireDefinitionId, seed.Payload.SeedContext.SourceIntakeDefinitionId);
        Assert.True(seed.Payload.Subjective.IsPainScoreDocumented);
        Assert.Equal(0, seed.Payload.Subjective.CurrentPainScore);
        Assert.Equal("Keep clinician instructions", seed.Payload.Plan.HomeExerciseProgramNotes);
        Assert.Equal("Patient request", seed.Payload.Plan.PrimaryDischargeReason);
        if (type == NoteType.Daily)
        {
            Assert.Equal("Daily patient answer", seed.Payload.DailyTreatment.Questionnaire!.AdditionalInformation);
            Assert.Equal("Clinician observation", seed.Payload.DailyTreatment.ResponseToTreatment);
        }
        if (type == NoteType.ProgressNote) Assert.Equal(37.5m, seed.Payload.ProgressQuestionnaire.PercentImproved);
        if (type == NoteType.Discharge) Assert.Equal("Patient report only", seed.Payload.Discharge.GoalsMetStatus);
        await using var checkScope = factory.Services.CreateAsyncScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(originalNote, (await checkDb.ClinicalNotes.AsNoTracking().SingleAsync(n => n.Id == noteId)).ContentJson);
        Assert.Equal(email, (await checkDb.Patients.AsNoTracking().SingleAsync(p => p.Id == patientId)).Email);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v2/notes/workspace/{patientId}/{Guid.NewGuid()}/intake-seed")).StatusCode);
    }

    [Fact]
    public async Task DailyVisit_RejectsMissingPainAndCrossTypeAnswers()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.ClinicalNotes.FindAsync(noteId))!.NoteType = NoteType.Daily;
            await db.SaveChangesAsync();
        }
        var issued = await IssueAsync(client, patientId, noteId);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(issued.ResponseJson, options)!;
        var update = new UpdateIntakeRequest
        {
            ExpectedLastModifiedUtc = issued.LastModifiedUtc,
            ResponseJson = JsonSerializer.Serialize(draft, options),
            Consents = """{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"termsOfServiceAccepted":true}"""
        };
        using var response = await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", update);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync($"/api/v1/intake/{issued.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc })).StatusCode);
        draft.VisitQuestionnaire!.Progress = new();
        update.ExpectedLastModifiedUtc = saved.LastModifiedUtc;
        update.ResponseJson = JsonSerializer.Serialize(draft, options);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", update)).StatusCode);
    }

    private static UpdateIntakeRequest UpdateFor(IntakeResponse intake, DateTime? version)
    {
        var request = Update(version);
        if (!IntakeQuestionnaireDefinitions.IsShort(intake.QuestionnaireDefinitionId)) return request;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, options)!;
        draft.VisitQuestionnaire!.CurrentPain = 0;
        request.ResponseJson = JsonSerializer.Serialize(draft, options);
        return request;
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
        var intake = (await response.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var consents = JsonSerializer.Deserialize<IntakeConsentPacket>(intake.Consents)!;
        Assert.NotEqual(true, consents.TreatmentConsentAccepted);
        Assert.NotEqual(true, consents.HipaaAcknowledged);
        Assert.NotEqual(true, consents.CommunicationCallConsent);
        Assert.NotEqual(true, consents.CommunicationTextConsent);
        Assert.NotEqual(true, consents.CommunicationEmailConsent);
        Assert.NotEqual(true, consents.DryNeedlingConsentAccepted);
        Assert.NotEqual(true, consents.PelvicFloorConsentAccepted);
        Assert.NotEqual(true, consents.PhiReleaseAuthorized);
        Assert.NotEqual(true, consents.CreditCardAuthorizationAccepted);
        return intake;
    }

    private async Task<(Guid Patient, Guid Note, string Email)> CreateFixtureAsync(HttpClient client, NoteType type = NoteType.Evaluation)
    {
        var email = $"synthetic-{Guid.NewGuid():N}@example.test";
        using var response = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest { FirstName = "Synthetic", LastName = "Encounter", DateOfBirth = new DateTime(1990, 1, 1), Email = email });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var patient = (await response.Content.ReadFromJsonAsync<PatientResponse>())!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var note = new ClinicalNote { PatientId = patient.Id, NoteType = type, LastModifiedUtc = DateTime.UtcNow };
        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync();
        return (patient.Id, note.Id, email);
    }
}
