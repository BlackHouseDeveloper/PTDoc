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
            var auditEvent = new AuditEvent
            {
                EventType = "AiGeneration",
                UserId = userGuid,
                Metadata = new Dictionary<string, object>
                {
                    { "GenerationType", "Assessment" },
                    { "TemplateVersion", result.Metadata.TemplateVersion },
                    { "Model", result.Metadata.Model },
                    { "Success", result.Success },
                    { "TokenCount", result.Metadata.TokenCount ?? 0 },
                    { "Timestamp", DateTime.UtcNow }
                }
            };

            // Log as rule evaluation since it's the closest existing method
            // Alternatively, we could add a new LogAiGenerationAsync method
            await auditService.LogRuleEvaluationAsync(auditEvent, cancellationToken);
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
        var userId = httpContext.User.FindFirst("user_id")?.Value;
        if (userId != null && Guid.TryParse(userId, out var userGuid))
        {
            var auditEvent = new AuditEvent
            {
                EventType = "AiGeneration",
                UserId = userGuid,
                Metadata = new Dictionary<string, object>
                {
                    { "GenerationType", "Plan" },
                    { "TemplateVersion", result.Metadata.TemplateVersion },
                    { "Model", result.Metadata.Model },
                    { "Success", result.Success },
                    { "TokenCount", result.Metadata.TokenCount ?? 0 },
                    { "Timestamp", DateTime.UtcNow }
                }
            };

            // Log as rule evaluation since it's the closest existing method
            await auditService.LogRuleEvaluationAsync(auditEvent, cancellationToken);
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
}
