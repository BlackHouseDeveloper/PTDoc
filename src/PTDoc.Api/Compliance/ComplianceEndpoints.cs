using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Compliance;

/// <summary>
/// API endpoints for compliance and Medicare rules.
/// Backend-only - no UI dependencies.
/// Sprint P: RBAC enforcement per FSD §3.
/// </summary>
public static class ComplianceEndpoints
{
    public static void MapComplianceEndpoints(this WebApplication app)
    {
        var complianceGroup = app.MapGroup("/api/v1/compliance")
            .WithTags("Compliance")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

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

        // Sprint N: Clinical validation — documentation completeness + Medicare compliance.
        complianceGroup.MapGet("/validate/clinical/{noteId:guid}",
            async (Guid noteId, IClinicalRulesEngine clinicalRulesEngine) =>
            {
                var violations = await clinicalRulesEngine.RunClinicalValidationAsync(noteId);
                return Results.Ok(new
                {
                    noteId,
                    violations,
                    hasBlockingViolations = violations.Any(v => v.Blocking),
                    canSign = violations.All(v => !v.Blocking)
                });
            })
            .WithName("ValidateClinicalNote");
    }

    public static void MapNoteEndpoints(this WebApplication app)
    {
        var notesGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes");

        // Signature endpoint — requires licensed clinician (PT or PTA).
        // Domain guard: PTA may only sign Daily notes (not Eval, ProgressNote, or Discharge)
        // per FSD §3.3 and Medicare documentation rules.
        notesGroup.MapPost("/{noteId:guid}/sign",
            async (Guid noteId, ISignatureService signatureService,
                   IIdentityContextAccessor identityContext,
                   ApplicationDbContext db,
                   HttpContext httpContext) =>
            {
                var userId = identityContext.GetCurrentUserId();
                if (userId == Guid.Empty)
                {
                    return Results.Unauthorized();
                }

                // Domain guard: PTA cannot sign Evaluation, Progress Note, or Discharge notes.
                var isPta = httpContext.User.IsInRole(Roles.PTA);
                if (isPta)
                {
                    var noteType = await db.ClinicalNotes
                        .AsNoTracking()
                        .Where(n => n.Id == noteId)
                        .Select(n => (NoteType?)n.NoteType)
                        .FirstOrDefaultAsync(httpContext.RequestAborted);

                    if (noteType == null)
                        return Results.NotFound(new { error = $"Note {noteId} not found." });

                    if (noteType != NoteType.Daily)
                        return Results.Forbid();
                }

                var result = await signatureService.SignNoteAsync(noteId, userId, signerIsPta: isPta);

                if (!result.Success)
                {
                    // Sprint N: Surface blocking validation failures with a 422 Unprocessable Entity.
                    if (result.ValidationFailures is { Count: > 0 })
                    {
                        return Results.UnprocessableEntity(new
                        {
                            error = result.ErrorMessage,
                            validationFailures = result.ValidationFailures
                        });
                    }
                    return Results.BadRequest(new { error = result.ErrorMessage });
                }

                return Results.Ok(new
                {
                    success = true,
                    signatureHash = result.SignatureHash,
                    signedUtc = result.SignedUtc,
                    // Sprint UC4: Notify caller that PT co-sign is required for PTA-authored notes
                    requiresCoSign = result.RequiresCoSign
                });
            })
            .WithName("SignNote")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        // Co-sign endpoint — PT-only, countersigns a PTA-authored note per Medicare rules.
        notesGroup.MapPost("/{noteId:guid}/co-sign",
            async (Guid noteId, ISignatureService signatureService,
                   IIdentityContextAccessor identityContext) =>
            {
                var userId = identityContext.GetCurrentUserId();
                if (userId == Guid.Empty)
                {
                    return Results.Unauthorized();
                }

                var result = await signatureService.CoSignNoteAsync(noteId, userId);

                if (!result.Success)
                {
                    return result.Status switch
                    {
                        CoSignStatus.NotFound => Results.NotFound(new { error = result.ErrorMessage }),
                        CoSignStatus.AlreadyCoSigned or
                        CoSignStatus.DoesNotRequireCoSign or
                        CoSignStatus.NotSigned => Results.Conflict(new { error = result.ErrorMessage }),
                        _ => Results.BadRequest(new { error = result.ErrorMessage }),
                    };
                }

                return Results.Ok(new
                {
                    success = true,
                    coSignedUtc = result.CoSignedUtc
                });
            })
            .WithName("CoSignNote")
            .RequireAuthorization(AuthorizationPolicies.NoteCoSign);

        // Addendum endpoint — requires licensed clinician (PT or PTA).
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
            .WithName("CreateAddendum")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        // Verify signature — readable by all clinical staff (PT, PTA, Admin).
        notesGroup.MapGet("/{noteId:guid}/verify-signature",
            async (Guid noteId, ISignatureService signatureService) =>
            {
                var isValid = await signatureService.VerifySignatureAsync(noteId);
                return Results.Ok(new { isValid });
            })
            .WithName("VerifySignature")
            .RequireAuthorization(AuthorizationPolicies.NoteRead);
    }
}

public record EightMinuteRuleRequest(int TotalMinutes, List<CptCodeEntry> CptCodes);
public record AddendumRequest(string Content);
