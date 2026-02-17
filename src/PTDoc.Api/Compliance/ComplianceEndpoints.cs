using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;

namespace PTDoc.Api.Compliance;

/// <summary>
/// API endpoints for compliance and Medicare rules.
/// Backend-only - no UI dependencies.
/// </summary>
public static class ComplianceEndpoints
{
    public static void MapComplianceEndpoints(this WebApplication app)
    {
        var complianceGroup = app.MapGroup("/api/v1/compliance")
            .WithTags("Compliance")
            .RequireAuthorization();

        // Rule evaluation endpoints
        complianceGroup.MapPost("/evaluate/pn-frequency/{patientId:guid}",
            async (Guid patientId, IRulesEngine rulesEngine) =>
            {
                var result = await rulesEngine.ValidateProgressNoteFrequencyAsync(patientId);
                return Results.Ok(result);
            })
            .WithName("EvaluateProgressNoteFrequency");

        complianceGroup.MapPost("/evaluate/8-minute-rule",
            async ([FromBody] EightMinuteRuleRequest request, IRulesEngine rulesEngine) =>
            {
                var result = await rulesEngine.ValidateEightMinuteRuleAsync(
                    request.TotalMinutes,
                    request.CptCodes);
                return Results.Ok(result);
            })
            .WithName("EvaluateEightMinuteRule");

        complianceGroup.MapPost("/evaluate/signature-eligible/{noteId:guid}",
            async (Guid noteId, IRulesEngine rulesEngine) =>
            {
                var result = await rulesEngine.ValidateSignatureEligibilityAsync(noteId);
                return Results.Ok(result);
            })
            .WithName("EvaluateSignatureEligibility");

        complianceGroup.MapPost("/evaluate/immutability/{noteId:guid}",
            async (Guid noteId, IRulesEngine rulesEngine) =>
            {
                var result = await rulesEngine.ValidateImmutabilityAsync(noteId);
                return Results.Ok(result);
            })
            .WithName("EvaluateImmutability");
    }

    public static void MapNoteEndpoints(this WebApplication app)
    {
        var notesGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization();

        // Signature endpoint
        notesGroup.MapPost("/{noteId:guid}/sign",
            async (Guid noteId, ISignatureService signatureService, IIdentityContextAccessor identityContext) =>
            {
                var userId = identityContext.GetCurrentUserId();
                if (userId == Guid.Empty)
                {
                    return Results.Unauthorized();
                }

                var result = await signatureService.SignNoteAsync(noteId, userId);

                if (!result.Success)
                {
                    return Results.BadRequest(new { error = result.ErrorMessage });
                }

                return Results.Ok(new
                {
                    success = true,
                    signatureHash = result.SignatureHash,
                    signedUtc = result.SignedUtc
                });
            })
            .WithName("SignNote");

        // Addendum endpoint
        notesGroup.MapPost("/{noteId:guid}/addendum",
            async (Guid noteId, [FromBody] AddendumRequest request,
                   ISignatureService signatureService, IIdentityContextAccessor identityContext) =>
            {
                var userId = identityContext.GetCurrentUserId();
                if (userId == Guid.Empty)
                {
                    return Results.Unauthorized();
                }

                var result = await signatureService.CreateAddendumAsync(noteId, request.Content, userId);

                if (!result.Success)
                {
                    return Results.BadRequest(new { error = result.ErrorMessage });
                }

                return Results.Ok(new
                {
                    success = true,
                    addendumId = result.AddendumId
                });
            })
            .WithName("CreateAddendum");

        // Verify signature endpoint
        notesGroup.MapGet("/{noteId:guid}/verify-signature",
            async (Guid noteId, ISignatureService signatureService) =>
            {
                var isValid = await signatureService.VerifySignatureAsync(noteId);
                return Results.Ok(new { isValid });
            })
            .WithName("VerifySignature");
    }
}

public record EightMinuteRuleRequest(int TotalMinutes, List<CptCodeEntry> CptCodes);
public record AddendumRequest(string Content);
