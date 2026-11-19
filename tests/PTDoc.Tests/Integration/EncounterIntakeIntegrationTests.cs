using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeIntegrationTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OtherDetails_InvalidLengthOrNoneConsistencyCannotCreateOrUpdate(bool update, bool none)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client);
        var issued = await IssueAsync(client, patientId, noteId);
        var structured = new IntakeStructuredDataDto
        {
            NoMedications = none,
            NoComorbidities = none,
            OtherMedications = new string('m', none ? 1 : 2001),
            OtherMedicalHistory = new string('h', none ? 1 : 2001)
        };
        var request = Update(issued.LastModifiedUtc);
        request.StructuredData = structured;
        using var response = update
            ? await client.PutAsJsonAsync($"/api/v1/intake/{issued.Id}", request)
            : await client.PostAsJsonAsync("/api/v1/intake/", new CreateIntakeRequest { PatientId = patientId, StructuredData = structured });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("structuredData.otherMedications", problem);
        Assert.Contains("structuredData.otherMedicalHistory", problem);
        var unchanged = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{issued.Id}"))!;
        Assert.Equal(issued.ResponseJson, unchanged.ResponseJson);
        Assert.False(unchanged.Locked);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await db.IntakeForms.Where(form => form.PatientId == patientId).ToListAsync());
    }

    [Theory]
    [InlineData(NoteType.Evaluation, NoteType.ProgressNote, false, false, false)]
    [InlineData(NoteType.Evaluation, NoteType.Daily, false, false, false)]
    [InlineData(NoteType.Evaluation, NoteType.Discharge, false, false, true)]
    [InlineData(NoteType.Evaluation, NoteType.Evaluation, true, false, false)]
    [InlineData(NoteType.Evaluation, NoteType.Evaluation, true, false, true)]
    [InlineData(NoteType.Daily, NoteType.Daily, false, true, false)]
    [InlineData(NoteType.Daily, NoteType.Daily, false, true, true)]
    public async Task IssuedIntake_LocksWorkspaceTypeButAllowsSameTypeEdits(
        NoteType originalType, NoteType requestedType, bool reEvaluation, bool dryNeedling, bool submitted)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client, originalType);
        var url = $"/api/v2/notes/workspace/{patientId}/{noteId}";
        var before = (await client.GetFromJsonAsync<NoteWorkspaceV2LoadResponse>(url))!;
        Assert.False(before.HasEncounterIntake);
        var intake = await IssueAsync(client, patientId, noteId);
        if (submitted)
        {
            using var answerSave = await client.PutAsJsonAsync($"/api/v1/intake/{intake.Id}", UpdateFor(intake, intake.LastModifiedUtc));
            Assert.Equal(HttpStatusCode.OK, answerSave.StatusCode);
            intake = (await answerSave.Content.ReadFromJsonAsync<IntakeResponse>())!;
            using var submit = await client.PostAsJsonAsync($"/api/v1/intake/{intake.Id}/submit",
                new SubmitIntakeRequest { ExpectedLastModifiedUtc = intake.LastModifiedUtc });
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
            intake = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{intake.Id}"))!;
        }
        var current = (await client.GetFromJsonAsync<NoteWorkspaceV2LoadResponse>(url))!;
        Assert.True(current.HasEncounterIntake);
        Assert.True(current.LastModifiedUtc > before.LastModifiedUtc);
        var request = new NoteWorkspaceV2SaveRequest
        {
            PatientId = patientId,
            NoteId = noteId,
            DateOfService = DateTime.UtcNow.Date,
            ExpectedLastModifiedUtc = before.LastModifiedUtc,
            NoteType = originalType,
            Payload = current.Payload
        };
        using var stale = await client.PostAsJsonAsync("/api/v2/notes/workspace/", request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        request.ExpectedLastModifiedUtc = current.LastModifiedUtc;
        request.NoteType = requestedType;
        request.IsReEvaluation = reEvaluation;
        request.Payload.DryNeedling = dryNeedling ? new() : null;
        using var rejected = await client.PostAsJsonAsync("/api/v2/notes/workspace/", request);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("note type is locked", await rejected.Content.ReadAsStringAsync());
        var unchanged = (await client.GetFromJsonAsync<NoteWorkspaceV2LoadResponse>(url))!;
        Assert.Equal(originalType, unchanged.NoteType);
        Assert.False(unchanged.IsReEvaluation);
        Assert.Null(unchanged.Payload.DryNeedling);
        Assert.Equal(current.LastModifiedUtc, unchanged.LastModifiedUtc);
        var retained = (await client.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{intake.Id}"))!;
        Assert.Equal(intake.ResponseJson, retained.ResponseJson);
        Assert.Equal(intake.Consents, retained.Consents);
        Assert.Equal(intake.QuestionnaireDefinitionId, retained.QuestionnaireDefinitionId);
        Assert.Equal(intake.Locked, retained.Locked);

        request.NoteType = originalType;
        request.IsReEvaluation = false;
        request.Payload.DryNeedling = null;
        request.Payload.Subjective.KnownCause = "Ordinary same-type edit";
        using var accepted = await client.PostAsJsonAsync("/api/v2/notes/workspace/", request);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var saved = (await accepted.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>())!.Workspace!;
        Assert.True(saved.HasEncounterIntake);
        Assert.Equal("Ordinary same-type edit", saved.Payload.Subjective.KnownCause);
    }

    [Fact]
    public async Task IssuedIntake_GenericNoteUpdateCannotSwitchDailyToDryNeedling()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client, NoteType.Daily);
        var intake = await IssueAsync(client, patientId, noteId);
        using var response = await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest
        {
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload { NoteType = NoteType.Daily, DryNeedling = new() })
        });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("note type is locked", await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unchanged = (await db.ClinicalNotes.FindAsync(noteId))!;
        Assert.Equal(NoteType.Daily, unchanged.NoteType);
        Assert.Equal("{}", unchanged.ContentJson);
        Assert.Equal(intake.ResponseJson, (await db.IntakeForms.FindAsync(intake.Id))!.ResponseJson);
    }

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
    public async Task PatientSelectedOutcomes_RejectUnrelatedMeasure_ResumeAndProjectServerScoreThroughOtpLink()
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        using var patient = factory.CreateUnauthenticatedClient();
        var (patientId, noteId, email) = await CreateFixtureAsync(staff, NoteType.ProgressNote);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var note = (await db.ClinicalNotes.FindAsync(noteId))!;
            note.ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            { NoteType = NoteType.ProgressNote, Objective = new() { PrimaryBodyPart = BodyPart.Shoulder } }, json);
            await db.SaveChangesAsync();
        }
        var issued = await IssueAsync(staff, patientId, noteId);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(issued.ResponseJson, json)!;
        var choices = draft.PatientOutcomes!;
        Assert.Equal(2, choices.Choices.Count);
        Assert.Empty(choices.Responses);
        var definition = choices.Choices.Single(q => q.Definition.InstrumentId == "quickdash").Definition;
        using var linkResponse = await staff.PostAsync($"/api/v1/intake/{issued.Id}/delivery/link", null);
        var link = (await linkResponse.Content.ReadFromJsonAsync<IntakeDeliveryBundleResponse>())!;
        var invite = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(link.InviteUrl).Query)["invite"].ToString();
        using var sent = await patient.PostAsJsonAsync("/api/v1/intake/access/send-otp", new SendIntakeOtpRequest
        { InviteToken = invite, Contact = email, Channel = OtpChannel.Email });
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        using var verified = await patient.PostAsJsonAsync("/api/v1/intake/access/verify-otp", new VerifyIntakeOtpRequest
        { InviteToken = invite, Contact = email, Channel = OtpChannel.Email, OtpCode = factory.LastIntakeOtpCode! });
        var access = (await verified.Content.ReadFromJsonAsync<IntakeInviteResult>())!;
        patient.DefaultRequestHeaders.Add(IntakeAccessHeaders.AccessToken, access.AccessToken);
        draft.VisitQuestionnaire!.CurrentPain = 0;
        choices.Responses = [new("lefs", definition.Version, [])];
        var update = Update(issued.LastModifiedUtc);
        update.ResponseJson = JsonSerializer.Serialize(draft, json);
        Assert.Equal(HttpStatusCode.Conflict, (await patient.PutAsJsonAsync($"/api/v1/intake/access/{issued.Id}", update)).StatusCode);
        choices.Responses = [new("quickdash", definition.Version,
            definition.Items.Take(9).Select(i => new PTDoc.Application.Outcomes.OutcomeAnswer(i.Id,
                PTDoc.Application.Outcomes.OutcomeAnswerState.Answered, "3")).ToImmutableArray())];
        update.ResponseJson = JsonSerializer.Serialize(draft, json);
        using var partialResponse = await patient.PutAsJsonAsync($"/api/v1/intake/access/{issued.Id}", update);
        Assert.Equal(HttpStatusCode.OK, partialResponse.StatusCode);
        var partial = (await partialResponse.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var resumed = JsonSerializer.Deserialize<IntakeResponseDraft>(partial.ResponseJson, json)!;
        Assert.Equal(PTDoc.Application.Outcomes.OutcomeCompletion.Incomplete, resumed.PatientOutcomes!.Responses.Single().Result!.Calculation.Completion);
        resumed.PatientOutcomes.Responses = [resumed.PatientOutcomes.Responses.Single() with
        {
            Answers = definition.Items.Take(10).Select(i => new PTDoc.Application.Outcomes.OutcomeAnswer(i.Id,
                PTDoc.Application.Outcomes.OutcomeAnswerState.Answered, "3")).ToImmutableArray()
        }];
        update.ExpectedLastModifiedUtc = partial.LastModifiedUtc;
        update.ResponseJson = JsonSerializer.Serialize(resumed, json);
        using var savedResponse = await patient.PutAsJsonAsync($"/api/v1/intake/access/{issued.Id}", update);
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var saved = (await savedResponse.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var submitted = await patient.PostAsJsonAsync($"/api/v1/intake/access/{issued.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = saved.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var seed = await staff.GetFromJsonAsync<NoteWorkspaceV2EvaluationSeedResponse>($"/api/v2/notes/workspace/{patientId}/{noteId}/intake-seed");
        Assert.Equal(issued.Id, seed!.Payload.IntakeOutcomes!.IntakeId);
        Assert.Equal(50m, seed.Payload.IntakeOutcomes.Responses.Single().Result!.Calculation.Scores.Single().Value);

        var source = (await staff.GetFromJsonAsync<IntakeResponse>($"/api/v1/intake/{issued.Id}"))!;
        Assert.Equal(source.SubmittedAt, seed.Payload.IntakeOutcomes.AnswerVersionUtc);
        var current = (await staff.GetFromJsonAsync<NoteWorkspaceV2LoadResponse>($"/api/v2/notes/workspace/{patientId}/{noteId}"))!;
        var save = new NoteWorkspaceV2SaveRequest
        {
            PatientId = patientId,
            NoteId = noteId,
            NoteType = NoteType.ProgressNote,
            DateOfService = DateTime.UtcNow.Date,
            ExpectedLastModifiedUtc = current.LastModifiedUtc,
            Payload = seed.Payload
        };
        using var copied = await staff.PostAsJsonAsync("/api/v2/notes/workspace/", save);
        Assert.Equal(HttpStatusCode.OK, copied.StatusCode);
        var workspace = (await copied.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>())!.Workspace!;
        using var reviewed = await staff.PostAsync($"/api/v1/intake/{issued.Id}/review", null);
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        var afterReview = (await reviewed.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(source.ResponseJson, afterReview.ResponseJson);
        Assert.NotEqual(source.LastModifiedUtc, afterReview.LastModifiedUtc);
        save.ExpectedLastModifiedUtc = workspace.LastModifiedUtc;
        save.Payload = workspace.Payload;
        using var savedAfterReview = await staff.PostAsJsonAsync("/api/v2/notes/workspace/", save);
        Assert.Equal(HttpStatusCode.OK, savedAfterReview.StatusCode);
        Assert.Equal(source.SubmittedAt, (await savedAfterReview.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>())!
            .Workspace!.Payload.IntakeOutcomes!.AnswerVersionUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StandaloneOutcomes_SeedNewEvaluationAndRetainEvidenceAfterReview(bool reEvaluation)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        using var patientResponse = await client.PostAsJsonAsync("/api/v1/patients", new CreatePatientRequest
        {
            FirstName = "Synthetic",
            LastName = "OutcomeSeed",
            DateOfBirth = new DateTime(1990, 1, 1)
        });
        Assert.Equal(HttpStatusCode.Created, patientResponse.StatusCode);
        var patientId = (await patientResponse.Content.ReadFromJsonAsync<PatientResponse>())!.Id;
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var structured = new IntakeStructuredDataDto
        {
            BodyPartSelections = [new() { BodyPartId = "shoulder", Lateralities = ["right"] }]
        };
        using var created = await client.PostAsJsonAsync("/api/v1/intake/", new CreateIntakeRequest
        {
            PatientId = patientId,
            StructuredData = structured,
            ResponseJson = JsonSerializer.Serialize(new IntakeResponseDraft { PainSeverityScore = 2, PainSeverityProvided = true }, json)
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var intake = (await created.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Null(intake.EncounterNoteId);
        Assert.Null(intake.QuestionnaireDefinitionId);
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(intake.ResponseJson, json)!;
        var definition = draft.PatientOutcomes!.Choices.Single(q => q.Definition.InstrumentId == "quickdash").Definition;
        draft.PatientOutcomes.Responses = [new("quickdash", definition.Version,
            definition.Items.Select(i => new OutcomeAnswer(i.Id, OutcomeAnswerState.Answered, "3")).ToImmutableArray())];
        var update = Update(intake.LastModifiedUtc);
        update.StructuredData = structured;
        update.ResponseJson = JsonSerializer.Serialize(draft, json);
        using var updated = await client.PutAsJsonAsync($"/api/v1/intake/{intake.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        intake = (await updated.Content.ReadFromJsonAsync<IntakeResponse>())!;

        var seedUrl = $"/api/v2/notes/workspace/{patientId}/evaluation-seed";
        var unlockedSeed = (await client.GetFromJsonAsync<NoteWorkspaceV2EvaluationSeedResponse>(seedUrl))!;
        Assert.False(unlockedSeed.FromLockedSubmittedIntake);
        Assert.Null(unlockedSeed.Payload.IntakeOutcomes);
        using var submitted = await client.PostAsJsonAsync($"/api/v1/intake/{intake.Id}/submit",
            new SubmitIntakeRequest { ExpectedLastModifiedUtc = intake.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var source = (await submitted.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var seed = (await client.GetFromJsonAsync<NoteWorkspaceV2EvaluationSeedResponse>(seedUrl))!;
        Assert.True(seed.FromLockedSubmittedIntake);
        Assert.Equal(source.Id, seed.Payload.IntakeOutcomes!.IntakeId);
        Assert.Equal(source.SubmittedAt, seed.Payload.IntakeOutcomes.AnswerVersionUtc);
        Assert.Equal(source.SubmittedAt, seed.Payload.SeedContext.SourceIntakeVersionUtc);
        Assert.Equal(IntakeQuestionnaireDefinitions.EvaluationFullV1, seed.Payload.SeedContext.SourceIntakeDefinitionId);
        Assert.Equal(50m, seed.Payload.IntakeOutcomes.Responses.Single().Result!.Calculation.Scores.Single().Value);

        var save = new NoteWorkspaceV2SaveRequest
        {
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            IsReEvaluation = reEvaluation,
            DateOfService = DateTime.UtcNow.Date,
            Payload = seed.Payload
        };
        using var saved = await client.PostAsJsonAsync("/api/v2/notes/workspace/", save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var workspace = (await saved.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>())!.Workspace!;
        var outcomeJson = JsonSerializer.Serialize(seed.Payload.IntakeOutcomes, json);
        Assert.Equal(outcomeJson, JsonSerializer.Serialize(workspace.Payload.IntakeOutcomes, json));
        Assert.Equal(reEvaluation, workspace.IsReEvaluation);
        // The selected standalone source cannot be reused for a second new evaluation.
        using var reused = await client.PostAsJsonAsync("/api/v2/notes/workspace/", save);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);

        using var reviewed = await client.PostAsync($"/api/v1/intake/{intake.Id}/review", null);
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        var afterReview = (await reviewed.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.Equal(source.SubmittedAt, afterReview.SubmittedAt);
        Assert.Equal(source.ResponseJson, afterReview.ResponseJson);
        Assert.Null(afterReview.EncounterNoteId);
        save.NoteId = workspace.NoteId;
        save.ExpectedLastModifiedUtc = workspace.LastModifiedUtc;
        save.Payload = workspace.Payload;
        using var resaved = await client.PostAsJsonAsync("/api/v2/notes/workspace/", save);
        Assert.Equal(HttpStatusCode.OK, resaved.StatusCode);
        workspace = (await resaved.Content.ReadFromJsonAsync<NoteWorkspaceV2SaveResponse>())!.Workspace!;
        using var genericUpdate = await client.PutAsJsonAsync($"/api/v1/notes/{workspace.NoteId}", new UpdateNoteRequest
        {
            ContentJson = JsonSerializer.Serialize(workspace.Payload, json)
        });
        Assert.Equal(HttpStatusCode.OK, genericUpdate.StatusCode);
        var reloaded = (await client.GetFromJsonAsync<NoteWorkspaceV2LoadResponse>($"/api/v2/notes/workspace/{patientId}/{workspace.NoteId}"))!;
        Assert.Equal(outcomeJson, JsonSerializer.Serialize(reloaded.Payload.IntakeOutcomes, json));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenericNoteWrites_RejectDuplicateOutcomePropertiesWithoutSaving(bool update)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, _) = await CreateFixtureAsync(client);
        var snapshot = JsonSerializer.Serialize(new WorkspaceIntakeOutcomeSnapshot(Guid.NewGuid(), DateTime.UtcNow, [], []));
        var content = $"{{\"intakeOutcomes\":null,\"\\u0069ntakeOutcomes\":{snapshot}}}";
        // This schema-less shape survives the normalizer, so the evidence guard must reject it.
        Assert.Equal(content, PTDoc.Infrastructure.Services.NoteWriteService.NormalizeContentJson(NoteType.Evaluation, false, DateTime.UtcNow, content));
        using var response = update
            ? await client.PutAsJsonAsync($"/api/v1/notes/{noteId}", new UpdateNoteRequest { ContentJson = content })
            : await client.PostAsJsonAsync("/api/v1/notes/", new CreateNoteRequest
            {
                PatientId = patientId,
                NoteType = NoteType.Evaluation,
                DateOfService = DateTime.UtcNow.Date,
                ContentJson = content
            });
        Assert.Equal(update ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("duplicate intakeOutcomes", await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var retained = Assert.Single(await db.ClinicalNotes.AsNoTracking().Where(n => n.PatientId == patientId).ToListAsync());
        Assert.Equal(noteId, retained.Id);
        Assert.Equal("{}", retained.ContentJson);
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
        await VerifyPatientLinkAsync(staff, patient, first.Id, email);
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
                DailyTreatment = new() { ResponseToTreatment = "Clinician observation" },
                ProgressQuestionnaire = new() { SetbackDetails = "Recorded clinician setback detail" }
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
        if (type == NoteType.ProgressNote)
        {
            Assert.Equal(37.5m, seed.Payload.ProgressQuestionnaire.PercentImproved);
            Assert.Equal("Recorded clinician setback detail", seed.Payload.ProgressQuestionnaire.SetbackDetails);
        }
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

    [Theory]
    [InlineData(NoteType.Evaluation, false)]
    [InlineData(NoteType.Daily, false)]
    [InlineData(NoteType.Evaluation, true)]
    public async Task RecordedPhiRevocation_AllowsNewContactWithoutSelectingRecipient(NoteType type, bool submitViaPatientLink)
    {
        using var client = factory.CreateClientWithRole(Roles.PT);
        using var patient = factory.CreateUnauthenticatedClient();
        var (patientId, noteId, email) = await CreateFixtureAsync(client, type);
        var issued = await IssueAsync(client, patientId, noteId);
        var path = $"/api/v1/intake/{issued.Id}";
        if (submitViaPatientLink)
        {
            await VerifyPatientLinkAsync(client, patient, issued.Id, email);
            issued = (await client.GetFromJsonAsync<IntakeResponse>(path))!;
        }
        var initial = UpdateFor(issued, issued.LastModifiedUtc);
        initial.ConsentPacket = new() { HipaaAcknowledged = true, TreatmentConsentAccepted = true, FinalAttestationAccepted = true, PhiReleaseAuthorized = true };
        using var saved = await client.PutAsJsonAsync(path, initial);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var revoked = await client.PostAsJsonAsync(path + "/consents/revoke", new { consentKeys = new[] { "phiReleaseAuthorized" }, writtenRevocationReceived = true });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var current = (await revoked.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var update = UpdateFor(current, current.LastModifiedUtc);
        using var stale = await client.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var packet = current.ConsentPacket!;
        var timestamp = packet.LastRevocationAtUtc;
        Assert.NotNull(timestamp);
        packet.AuthorizedContacts.Add(new() { Id = "contact-1", Name = "Synthetic", PhoneNumber = "555-0101", Relationship = "Friend" });
        update.ConsentPacket = packet;
        using var resumed = await client.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        var ready = (await resumed.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var submitRequest = new SubmitIntakeRequest { ExpectedLastModifiedUtc = ready.LastModifiedUtc };
        using var submitted = submitViaPatientLink
            ? await patient.PostAsJsonAsync($"/api/v1/intake/access/{issued.Id}/submit", submitRequest)
            : await client.PostAsJsonAsync(path + "/submit", submitRequest);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var locked = (await client.GetFromJsonAsync<IntakeResponse>(path))!;
        Assert.True(locked.Locked);
        Assert.True(locked.ConsentPacket!.PhiReleaseAuthorized);
        Assert.False(IntakeConsentJson.IsPhiReleaseConsentActive(locked.ConsentPacket));
        Assert.Equal(timestamp, locked.ConsentPacket.LastRevocationAtUtc);
        Assert.Null(locked.ConsentPacket.CreditCardAuthorizationAccepted);
        Assert.Null(locked.ConsentPacket.DryNeedlingConsentAccepted);
        var snapshot = JsonNode.Parse(locked.ResponseJson)!["consentPacket"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(ready.Consents), snapshot));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(locked.Consents), snapshot));
        Assert.Equal("contact-1", Assert.Single(locked.ConsentPacket.AuthorizedContacts).Id);
        Assert.Empty(locked.ConsentPacket.PhiAuthorizedContactIds);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, update)).StatusCode);
    }

    [Theory]
    [InlineData("communicationCallConsent")]
    [InlineData("communicationTextConsent")]
    public async Task RevokedCommunicationChannels_RequireDetailsOnlyForRemainingActiveChannel(string activePhoneChannel)
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        var (patientId, noteId, email) = await CreateFixtureAsync(staff);
        var issued = await IssueAsync(staff, patientId, noteId);
        var path = $"/api/v1/intake/{issued.Id}";
        var update = UpdateFor(issued, issued.LastModifiedUtc);
        update.ConsentPacket = new IntakeConsentPacket
        {
            HipaaAcknowledged = true, TreatmentConsentAccepted = true, FinalAttestationAccepted = true,
            CommunicationCallConsent = true, CommunicationTextConsent = true, CommunicationEmailConsent = true,
            CommunicationPhoneNumber = "555-0102", CommunicationEmail = email
        };
        using var initial = await staff.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        var revokedPhoneChannel = activePhoneChannel == "communicationCallConsent" ? "communicationTextConsent" : "communicationCallConsent";
        using var partial = await staff.PostAsJsonAsync(path + "/consents/revoke", new RevokeIntakeConsentRequest
        {
            ConsentKeys = ["communicationEmailConsent", revokedPhoneChannel], WrittenRevocationReceived = true
        });
        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        var current = (await partial.Content.ReadFromJsonAsync<IntakeResponse>())!;
        update = UpdateFor(current, current.LastModifiedUtc);
        update.ConsentPacket = current.ConsentPacket!;
        update.ConsentPacket.CommunicationPhoneNumber = null;
        update.ConsentPacket.CommunicationEmail = null;
        using var rejected = await staff.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("communicationPhoneNumber", Assert.Single(problem.GetProperty("errors").EnumerateObject()).Name);

        using var remaining = await staff.PostAsJsonAsync(path + "/consents/revoke", new RevokeIntakeConsentRequest
        {
            ConsentKeys = [activePhoneChannel], WrittenRevocationReceived = true
        });
        Assert.Equal(HttpStatusCode.OK, remaining.StatusCode);
        current = (await remaining.Content.ReadFromJsonAsync<IntakeResponse>())!;
        update.ExpectedLastModifiedUtc = current.LastModifiedUtc;
        update.ConsentPacket = current.ConsentPacket!;
        update.ConsentPacket.CommunicationPhoneNumber = null;
        update.ConsentPacket.CommunicationEmail = null;
        using var saved = await staff.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var ready = (await saved.Content.ReadFromJsonAsync<IntakeResponse>())!;
        using var submitted = await staff.PostAsJsonAsync(path + "/submit", new SubmitIntakeRequest { ExpectedLastModifiedUtc = ready.LastModifiedUtc });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var locked = (await submitted.Content.ReadFromJsonAsync<IntakeResponse>())!;
        Assert.True(locked.Locked);
        Assert.True(locked.ConsentPacket!.CommunicationCallConsent);
        Assert.True(locked.ConsentPacket.CommunicationTextConsent);
        Assert.True(locked.ConsentPacket.CommunicationEmailConsent);
        Assert.Null(locked.ConsentPacket.CommunicationPhoneNumber);
        Assert.Null(locked.ConsentPacket.CommunicationEmail);
    }

    [Theory]
    [InlineData(NoteType.Evaluation)]
    [InlineData(NoteType.Daily)]
    public async Task RevokedFinalAttestation_RejectsStaffAndPatientLinkSubmissions(NoteType type)
    {
        using var staff = factory.CreateClientWithRole(Roles.PT);
        using var patient = factory.CreateUnauthenticatedClient();
        var (patientId, noteId, email) = await CreateFixtureAsync(staff, type);
        var issued = await IssueAsync(staff, patientId, noteId);
        var path = $"/api/v1/intake/{issued.Id}";
        await VerifyPatientLinkAsync(staff, patient, issued.Id, email);
        var current = (await staff.GetFromJsonAsync<IntakeResponse>(path))!;
        var update = UpdateFor(current, current.LastModifiedUtc);
        update.ConsentPacket = new() { HipaaAcknowledged = true, TreatmentConsentAccepted = true, FinalAttestationAccepted = true };
        using var saved = await staff.PutAsJsonAsync(path, update);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var revoked = await staff.PostAsJsonAsync(path + "/consents/revoke", new { consentKeys = new[] { "finalAttestationAccepted" }, writtenRevocationReceived = true });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var recorded = (await revoked.Content.ReadFromJsonAsync<IntakeResponse>())!;
        var request = new SubmitIntakeRequest { ExpectedLastModifiedUtc = recorded.LastModifiedUtc };
        using var staffSubmit = await staff.PostAsJsonAsync(path + "/submit", request);
        using var patientSubmit = await patient.PostAsJsonAsync($"/api/v1/intake/access/{issued.Id}/submit", request);
        foreach (var response in new[] { staffSubmit, patientSubmit })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.GetProperty("errors").TryGetProperty("finalAttestationAccepted", out _));
        }
        var draft = (await staff.GetFromJsonAsync<IntakeResponse>(path))!;
        Assert.False(draft.Locked);
        Assert.Null(draft.SubmittedAt);
        Assert.Equal(recorded.LastModifiedUtc, draft.LastModifiedUtc);
        Assert.Equal(recorded.Consents, draft.Consents);
        Assert.Equal(recorded.ResponseJson, draft.ResponseJson);
    }

    private async Task VerifyPatientLinkAsync(HttpClient staff, HttpClient patient, Guid intakeId, string email)
    {
        using var linkResponse = await staff.PostAsync($"/api/v1/intake/{intakeId}/delivery/link", null);
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
