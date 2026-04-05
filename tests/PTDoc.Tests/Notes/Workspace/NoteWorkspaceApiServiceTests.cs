using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "Workspace")]
public sealed class NoteWorkspaceApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SaveDraftAsync_ProgressNote_UsesV2WorkspaceEndpoint()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/notes/workspace/", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            var response = new NoteWorkspaceV2SaveResponse
            {
                Workspace = new NoteWorkspaceV2LoadResponse
                {
                    NoteId = noteId,
                    PatientId = patientId,
                    DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
                    NoteType = NoteType.ProgressNote,
                    IsSigned = false,
                    Payload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    }
                }
            };

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(response, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = patientId,
            WorkspaceNoteType = "Progress Note",
            DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Progress Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm
                {
                    AssessmentNarrative = "Clinician assessment"
                },
                Plan = new PlanVm
                {
                    TreatmentFrequency = "2x/week",
                    TreatmentDuration = "6 weeks"
                }
            }
        });

        Assert.True(result.Success);
        Assert.Equal(noteId, result.NoteId);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal(patientId, document.RootElement.GetProperty("patientId").GetGuid());
        Assert.Equal("Clinician assessment", document.RootElement.GetProperty("payload")
            .GetProperty("assessment")
            .GetProperty("assessmentNarrative")
            .GetString());
        var frequencyDays = document.RootElement.GetProperty("payload")
            .GetProperty("plan")
            .GetProperty("treatmentFrequencyDaysPerWeek")
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToArray();
        var durationWeeks = document.RootElement.GetProperty("payload")
            .GetProperty("plan")
            .GetProperty("treatmentDurationWeeks")
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToArray();

        Assert.Equal(new[] { 2 }, frequencyDays);
        Assert.Equal(new[] { 6 }, durationWeeks);
    }

    [Fact]
    public async Task SaveDraftAsync_ComplianceFailure_ReturnsStructuredValidationPayload()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new NoteWorkspaceV2SaveResponse
            {
                IsValid = false,
                Errors = ["Progress Note required"],
                Warnings = ["Minutes fall below standard 8-minute threshold"],
                RequiresOverride = true
            };

            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = Guid.NewGuid(),
            WorkspaceNoteType = "Progress Note",
            DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Progress Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm(),
                Plan = new PlanVm()
            }
        });

        Assert.False(result.Success);
        Assert.Equal("Progress Note required", result.ErrorMessage);
        Assert.Collection(result.Errors, error => Assert.Equal("Progress Note required", error));
        Assert.Collection(result.Warnings, warning => Assert.Equal("Minutes fall below standard 8-minute threshold", warning));
        Assert.True(result.RequiresOverride);
    }

    [Fact]
    public async Task AcceptAiSuggestionAsync_PostsAcceptancePayload()
    {
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/accept-ai-suggestion", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse("""{"success":true}""");
        });

        var service = CreateService(handler);

        var result = await service.AcceptAiSuggestionAsync(noteId, "assessment", "Accepted AI text", "Assessment");

        Assert.True(result.Success);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("assessment", document.RootElement.GetProperty("section").GetString());
        Assert.Equal("Accepted AI text", document.RootElement.GetProperty("generatedText").GetString());
        Assert.Equal("Assessment", document.RootElement.GetProperty("generationType").GetString());
    }

    [Fact]
    public async Task SubmitAsync_PostsConsentAndIntentPayload()
    {
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/sign", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse("""{"success":true,"requiresCoSign":false}""");
        });

        var service = CreateService(handler);

        var result = await service.SubmitAsync(noteId, consentAccepted: true, intentConfirmed: true);

        Assert.True(result.Success);
        Assert.False(result.RequiresCoSign);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.True(document.RootElement.GetProperty("consentAccepted").GetBoolean());
        Assert.True(document.RootElement.GetProperty("intentConfirmed").GetBoolean());
    }

    [Fact]
    public async Task ExportPdfAsync_ReturnsPdfBytesAndHeaders()
    {
        var noteId = Guid.NewGuid();
        var expectedBytes = Encoding.UTF8.GetBytes("pdf-bytes");

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/export/pdf", request.RequestUri!.AbsolutePath);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"signed-note.pdf\""
            };

            return response;
        });

        var service = CreateService(handler);

        var result = await service.ExportPdfAsync(noteId);

        Assert.True(result.Success);
        Assert.Equal("signed-note.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(expectedBytes, result.Content);
    }

    private static NoteWorkspaceApiService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new NoteWorkspaceApiService(client);
    }
}
