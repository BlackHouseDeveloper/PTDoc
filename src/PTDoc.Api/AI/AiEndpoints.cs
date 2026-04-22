using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Api.Diagnostics;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;

namespace PTDoc.Api.AI;

/// <summary>
/// AI generation endpoints for clinical documentation assistance.
/// Feature-flagged: requires EnableAiGeneration = true
/// STATELESS: Does NOT persist generated content
/// </summary>
public static class AiEndpoints
{
    private const string AiFeatureDisabledCode = "ai_feature_disabled";
    private const string AiRequestInvalidCode = "ai_request_invalid";
    private const string AiNoteIdRequiredCode = "ai_note_id_required";
    private const string AiNoteNotFoundCode = "ai_note_not_found";
    private const string AiSignedNoteCode = "ai_signed_note";
    private const string AiGenerationFailedCode = "ai_generation_failed";

    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ai")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
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
        [FromServices] PTDoc.Infrastructure.Data.ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var _ = BeginAiScope(httpContext, "Assessment", request.NoteId);

        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AI generation is currently disabled.",
                AiFeatureDisabledCode);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.ChiefComplaint))
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status400BadRequest,
                "ChiefComplaint is required",
                AiRequestInvalidCode);
        }

        var validationFailure = await ValidateDraftNoteAsync(request.NoteId, db, httpContext, cancellationToken);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        // Generate AI content
        var result = await aiService.GenerateAssessmentAsync(request, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        var userId = (httpContext.User.FindFirst(PTDocClaimTypes.InternalUserId) ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier))?.Value;
        if (userId != null && Guid.TryParse(userId, out var userGuid))
        {
            var auditEvent = AuditEvent.AiGenerationAttempt(
                noteId: request.NoteId,
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
            return EndpointError(
                httpContext,
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "AI generation failed. Please try again or contact support.",
                AiGenerationFailedCode);
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
        [FromServices] AiDiagnosticsFaultStore faultStore,
        [FromServices] IAuditService auditService,
        [FromServices] IConfiguration configuration,
        [FromServices] PTDoc.Infrastructure.Data.ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var _ = BeginAiScope(httpContext, "Plan", request.NoteId);

        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AI generation is currently disabled.",
                AiFeatureDisabledCode);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Diagnosis))
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Diagnosis is required",
                AiRequestInvalidCode);
        }

        var validationFailure = await ValidateDraftNoteAsync(request.NoteId, db, httpContext, cancellationToken);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var currentUserId = TryGetCurrentUserId(httpContext);
        if (currentUserId is Guid consumingUserId
            && faultStore.TryConsume(
                AiDiagnosticsFaultModes.PlanGenerationFailure,
                request.NoteId,
                consumingUserId,
                out var consumedFault))
        {
            httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("PTDoc.Api.Diagnostics")
                .LogWarning(
                    "Consumed AI diagnostics fault {Mode} for note {NoteId} targeting user {TargetUserId}. CorrelationId {CorrelationId}",
                    consumedFault!.Mode,
                    consumedFault.NoteId,
                    consumedFault.TargetUserId,
                    httpContext.TraceIdentifier);

            return EndpointError(
                httpContext,
                StatusCodes.Status500InternalServerError,
                "AI generation failed. Please try again or contact support.",
                AiGenerationFailedCode);
        }

        // Generate AI content
        var result = await aiService.GeneratePlanAsync(request, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        if (currentUserId is Guid userGuid)
        {
            var auditEvent = AuditEvent.AiGenerationAttempt(
                noteId: request.NoteId,
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
            return EndpointError(
                httpContext,
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "AI generation failed. Please try again or contact support.",
                AiGenerationFailedCode);
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
        [FromServices] PTDoc.Infrastructure.Data.ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var _ = BeginAiScope(httpContext, "Goals", request.NoteId);

        // Feature flag check
        var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
        if (!enableAi)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status403Forbidden,
                "AI generation is currently disabled.",
                AiFeatureDisabledCode);
        }

        // Validate request
        if (string.IsNullOrWhiteSpace(request.Diagnosis))
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Diagnosis is required",
                AiRequestInvalidCode);
        }

        if (string.IsNullOrWhiteSpace(request.FunctionalLimitations))
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status400BadRequest,
                "FunctionalLimitations is required",
                AiRequestInvalidCode);
        }

        var validationFailure = await ValidateDraftNoteAsync(request.NoteId, db, httpContext, cancellationToken);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        // Rebuild the request with the server-authoritative IsNoteSigned value.
        // This prevents the client from bypassing the signed-note guardrail by omitting the flag.
        var guardedRequest = request with { IsNoteSigned = false };

        // Generate AI content
        var result = await clinicalService.GenerateGoalNarrativesAsync(guardedRequest, cancellationToken);

        // Audit logging (NO PHI - only metadata)
        var userId = (httpContext.User.FindFirst(PTDocClaimTypes.InternalUserId) ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier))?.Value;
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
            return EndpointError(
                httpContext,
                StatusCodes.Status500InternalServerError,
                result.ErrorMessage ?? "AI generation failed. Please try again or contact support.",
                AiGenerationFailedCode);
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

    private static async Task<IResult?> ValidateDraftNoteAsync(
        Guid noteId,
        PTDoc.Infrastructure.Data.ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (noteId == Guid.Empty)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status400BadRequest,
                "A valid NoteId is required.",
                AiNoteIdRequiredCode);
        }

        var noteSigning = await db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.Id == noteId)
            .Select(n => new { n.Id, n.SignatureHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (noteSigning is null)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status404NotFound,
                $"Note {noteId} not found.",
                AiNoteNotFoundCode);
        }

        if (noteSigning.SignatureHash is not null)
        {
            return EndpointError(
                httpContext,
                StatusCodes.Status409Conflict,
                "AI generation is not permitted on signed notes.",
                AiSignedNoteCode);
        }

        return null;
    }

    private static IResult EndpointError(HttpContext httpContext, int statusCode, string error, string code)
    {
        return Results.Json(new
        {
            error,
            code,
            correlationId = httpContext.TraceIdentifier
        }, statusCode: statusCode);
    }

    private static IDisposable? BeginAiScope(HttpContext httpContext, string generationType, Guid noteId)
    {
        var loggerFactory = httpContext.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("PTDoc.Api.AI");

        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = httpContext.TraceIdentifier,
            ["GenerationType"] = generationType,
            ["NoteId"] = noteId
        });
    }

    private static Guid? TryGetCurrentUserId(HttpContext httpContext)
    {
        var userId = (httpContext.User.FindFirst(PTDocClaimTypes.InternalUserId)
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier))?.Value;

        return Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : null;
    }
}
