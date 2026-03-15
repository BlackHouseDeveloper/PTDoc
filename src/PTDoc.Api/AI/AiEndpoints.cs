using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;

namespace PTDoc.Api.AI;

/// <summary>
/// AI generation endpoints for clinical documentation assistance.
/// Feature-flagged: requires EnableAiGeneration = true
/// STATELESS: Does NOT persist generated content
/// </summary>
public static class AiEndpoints
{
    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ai")
            .RequireAuthorization()
            .WithTags("AI Generation");

        group.MapPost("/assessment", GenerateAssessment)
            .WithName("GenerateAssessment")
            .WithSummary("Generate AI assessment text")
            .WithDescription("Stateless AI generation - does NOT save to database");

        group.MapPost("/plan", GeneratePlan)
            .WithName("GeneratePlan")
            .WithSummary("Generate AI plan of care text")
            .WithDescription("Stateless AI generation - does NOT save to database");

        group.MapPost("/goals", GenerateGoals)
            .WithName("GenerateGoals")
            .WithSummary("Generate AI goal narratives")
            .WithDescription("Stateless AI generation - does NOT save to database");
    }

    private static async Task<IResult> GenerateAssessment(
        [FromBody] AiAssessmentRequest request,
        [FromServices] IAiService aiService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return Results.StatusCode(403);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.ChiefComplaint))
        {
            return Results.BadRequest(new { error = "ChiefComplaint is required" });
        }

        // Generate AI content
        var result = await aiService.GenerateAssessmentAsync(request, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && Guid.TryParse(userId, out var userGuid))
        {
            var auditEvent = AuditEvent.AiGenerationAttempt(
                noteId: null,
                generationType: "Assessment",
                model: result.Metadata.Model,
                userId: userGuid,
                success: result.Success,
                errorMessage: result.Success ? null : result.ErrorMessage);
            auditEvent.Metadata["TokenCount"] = result.Metadata.TokenCount ?? 0;
            await auditService.LogAiGenerationAttemptAsync(auditEvent, cancellationToken);
        }

        if (!result.Success)
        {
            return Results.Problem(
                detail: result.ErrorMessage,
                statusCode: 500,
                title: "AI Generation Failed");
        }

        return Results.Ok(new
        {
            generatedText = result.GeneratedText,
            metadata = new
            {
                templateVersion = result.Metadata.TemplateVersion,
                model = result.Metadata.Model,
                generatedAt = result.Metadata.GeneratedAtUtc,
                tokenCount = result.Metadata.TokenCount
            }
        });
    }

    private static async Task<IResult> GeneratePlan(
        [FromBody] AiPlanRequest request,
        [FromServices] IAiService aiService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return Results.StatusCode(403);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Diagnosis))
        {
            return Results.BadRequest(new { error = "Diagnosis is required" });
        }

        // Generate AI content
        var result = await aiService.GeneratePlanAsync(request, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && Guid.TryParse(userId, out var userGuid))
        {
            var auditEvent = AuditEvent.AiGenerationAttempt(
                noteId: null,
                generationType: "Plan",
                model: result.Metadata.Model,
                userId: userGuid,
                success: result.Success,
                errorMessage: result.Success ? null : result.ErrorMessage);
            auditEvent.Metadata["TokenCount"] = result.Metadata.TokenCount ?? 0;
            await auditService.LogAiGenerationAttemptAsync(auditEvent, cancellationToken);
        }

        if (!result.Success)
        {
            return Results.Problem(
                detail: result.ErrorMessage,
                statusCode: 500,
                title: "AI Generation Failed");
        }

        return Results.Ok(new
        {
            generatedText = result.GeneratedText,
            metadata = new
            {
                templateVersion = result.Metadata.TemplateVersion,
                model = result.Metadata.Model,
                generatedAt = result.Metadata.GeneratedAtUtc,
                tokenCount = result.Metadata.TokenCount
            }
        });
    }

    private static async Task<IResult> GenerateGoals(
        [FromBody] GoalNarrativesGenerationRequest request,
        [FromServices] IAiClinicalGenerationService clinicalService,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return Results.StatusCode(403);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Diagnosis))
        {
            return Results.BadRequest(new { error = "Diagnosis is required" });
        }

        if (string.IsNullOrWhiteSpace(request.FunctionalLimitations))
        {
            return Results.BadRequest(new { error = "FunctionalLimitations is required" });
        }

        if (request.NoteId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "A valid NoteId is required" });
        }

        // Generate AI content
        var result = await clinicalService.GenerateGoalNarrativesAsync(request, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && Guid.TryParse(userId, out var userGuid))
        {
            var model = result.Metadata?.Model ?? "unknown";
            var auditEvent = AuditEvent.AiGenerationAttempt(
                noteId: request.NoteId,
                generationType: "Goals",
                model: model,
                userId: userGuid,
                success: result.Success,
                errorMessage: result.Success ? null : result.ErrorMessage);
            await auditService.LogAiGenerationAttemptAsync(auditEvent, cancellationToken);
        }

        if (!result.Success)
        {
            return Results.Problem(
                detail: result.ErrorMessage,
                statusCode: 500,
                title: "AI Generation Failed");
        }

        return Results.Ok(new
        {
            generatedText = result.GeneratedText,
            confidence = result.Confidence,
            warnings = result.Warnings,
            metadata = result.Metadata == null ? null : new
            {
                templateVersion = result.Metadata.TemplateVersion,
                model = result.Metadata.Model,
                generatedAt = result.Metadata.GeneratedAtUtc,
                tokenCount = result.Metadata.TokenCount
            }
        });
    }
}
