using System;
using System.Net.Http;
using System.Net;
using PTDoc.Application.AI;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.AI;

[Trait("Category", "CoreCi")]
public sealed class HttpAiClinicalGenerationServiceTests
{
    [Fact]
    public async Task GeneratePlanOfCareAsync_MapsMetadataFromApiResponse()
    {
        HttpRequestMessage? capturedRequest = null;
        var expectedGeneratedAt = DateTime.Parse("2026-03-30T12:34:56Z", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return StubHttpMessageHandler.JsonResponse("""
            {
              "generatedText": "Plan narrative",
              "metadata": {
                "templateVersion": "v2",
                "model": "gpt-4.1",
                "generatedAt": "2026-03-30T12:34:56Z",
                "tokenCount": 123
              }
            }
            """);
        });

        var service = CreateService(handler);

        var result = await service.GeneratePlanOfCareAsync(new PlanOfCareGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar strain",
            AssessmentSummary = "Limited lumbar mobility",
            Goals = "Restore lifting tolerance",
            Precautions = "Avoid loaded flexion"
        });

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/v1/ai/plan", capturedRequest.RequestUri!.AbsolutePath);
        Assert.True(result.Success);
        Assert.Equal("Plan narrative", result.GeneratedText);
        Assert.NotNull(result.Metadata);
        Assert.Equal("v2", result.Metadata!.TemplateVersion);
        Assert.Equal("gpt-4.1", result.Metadata.Model);
        Assert.Equal(expectedGeneratedAt, result.Metadata.GeneratedAtUtc);
        Assert.Equal(123, result.Metadata.TokenCount);
    }

    [Fact]
    public async Task GenerateGoalNarrativesAsync_WithEmptyNoteId_DoesNotCallApi()
    {
        var wasCalled = false;
        var handler = new StubHttpMessageHandler(_ =>
        {
            wasCalled = true;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(handler);

        var result = await service.GenerateGoalNarrativesAsync(new GoalNarrativesGenerationRequest
        {
            NoteId = Guid.Empty,
            Diagnosis = "Rotator cuff tendinopathy",
            FunctionalLimitations = "Overhead reach, dressing"
        });

        Assert.False(result.Success);
        Assert.Contains("save the note", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(wasCalled);
    }

    [Fact]
    public async Task GenerateAssessmentAsync_WithEmptyNoteId_DoesNotCallApi()
    {
        var wasCalled = false;
        var handler = new StubHttpMessageHandler(_ =>
        {
            wasCalled = true;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(handler);

        var result = await service.GenerateAssessmentAsync(new AssessmentGenerationRequest
        {
            NoteId = Guid.Empty,
            ChiefComplaint = "Shoulder pain"
        });

        Assert.False(result.Success);
        Assert.Contains("save the note", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(wasCalled);
    }

    [Fact]
    public async Task GeneratePlanOfCareAsync_WithEmptyNoteId_DoesNotCallApi()
    {
        var wasCalled = false;
        var handler = new StubHttpMessageHandler(_ =>
        {
            wasCalled = true;
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var service = CreateService(handler);

        var result = await service.GeneratePlanOfCareAsync(new PlanOfCareGenerationRequest
        {
            NoteId = Guid.Empty,
            Diagnosis = "Lumbar strain"
        });

        Assert.False(result.Success);
        Assert.Contains("save the note", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(wasCalled);
    }

    [Fact]
    public async Task GenerateAssessmentAsync_WhenApiReturnsProblemDetails_UsesDetailMessage()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
            {
              "title": "AI Generation Failed",
              "detail": "Chief complaint is required."
            }
            """, System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.GenerateAssessmentAsync(new AssessmentGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            ChiefComplaint = "Shoulder pain"
        });

        Assert.False(result.Success);
        Assert.Equal("Chief complaint is required.", result.ErrorMessage);
    }

    [Fact]
    public async Task GeneratePlanOfCareAsync_WhenApiReturnsStructuredAiError_IncludesReferenceId()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""
            {
              "error": "AI generation is currently disabled.",
              "code": "ai_feature_disabled",
              "correlationId": "ai-ref-123"
            }
            """, System.Text.Encoding.UTF8, "application/json")
        });

        var service = CreateService(handler);

        var result = await service.GeneratePlanOfCareAsync(new PlanOfCareGenerationRequest
        {
            NoteId = Guid.NewGuid(),
            Diagnosis = "Lumbar strain"
        });

        Assert.False(result.Success);
        Assert.Equal("AI generation is currently disabled. Reference ID: ai-ref-123", result.ErrorMessage);
    }

    private static HttpAiClinicalGenerationService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new HttpAiClinicalGenerationService(client);
    }
}
