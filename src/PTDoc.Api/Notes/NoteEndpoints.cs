using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Text.Json;

namespace PTDoc.Api.Notes;

/// <summary>
/// CRUD endpoints for clinical notes.
/// PUT is restricted to draft (unsigned) notes per Medicare immutability rules.
/// Sprint O: TDD §6.3 Clinical Notes APIs
/// Sprint P: RBAC enforcement — NoteWrite requires PT or PTA role.
/// Sprint S: Compliance rule integration — PN frequency hard stop, 8-minute rule validation,
///           audit logging for note edits.
/// </summary>
public static class NoteEndpoints
{
    public static void MapNoteCrudEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        group.MapPost("/", CreateNote)
            .WithName("CreateNote")
            .WithSummary("Create a new clinical note");

        group.MapPut("/{id:guid}", UpdateNote)
            .WithName("UpdateNote")
            .WithSummary("Update a draft clinical note");

        // Sprint UC-Gamma: AI output acceptance gate.
        // AI-generated content is NEVER persisted automatically — a clinician must
        // explicitly call this endpoint to write generated text into a draft note.
        group.MapPost("/{noteId:guid}/accept-ai-suggestion", AcceptAiSuggestion)
            .WithName("AcceptAiSuggestion")
            .WithSummary("Accept AI-generated content into a specific section of a draft note")
            .WithDescription(
                "Explicit clinician acceptance gate. AI output is not persisted until " +
                "a clinician (PT or PTA) calls this endpoint. Blocked on signed notes.");
    }

    // POST /api/notes
    private static async Task<IResult> CreateNote(
        [FromBody] CreateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IRulesEngine rulesEngine,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.PatientId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.PatientId), ["PatientId is required."] }
            });

        if (request.DateOfService == default)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.DateOfService), ["DateOfService is required."] }
            });

        // Sprint UC3: PTA domain guard — PTA clinicians may only create Daily notes.
        // Evaluation, ProgressNote, and Discharge notes require PT or higher authority.
        // Checked after basic field validation so invalid requests are rejected first.
        if (PtaIsBlockedFromNoteType(httpContext.User, request.NoteType))
        {
            return Results.Forbid();
        }

        // Verify the patient exists and is accessible in this tenant
        var patientExists = await db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PatientId, cancellationToken);

        if (!patientExists)
            return Results.NotFound(new { error = $"Patient {request.PatientId} not found." });

        // Validate appointment FK if provided: must exist and belong to the same patient
        if (request.AppointmentId.HasValue)
        {
            var appointment = await db.Appointments
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.AppointmentId.Value, cancellationToken);

            if (appointment is null)
                return Results.UnprocessableEntity(new { error = $"Appointment {request.AppointmentId} not found." });

            if (appointment.PatientId != request.PatientId)
                return Results.UnprocessableEntity(new { error = $"Appointment {request.AppointmentId} does not belong to patient {request.PatientId}." });
        }

        // Sprint S: Progress Note hard stop — block Daily note creation when PN is required.
        // Per Medicare guidelines, a Progress Note must be written every 10 visits or 30 days.
        if (request.NoteType == NoteType.Daily)
        {
            var pnFreqResult = await rulesEngine.ValidateProgressNoteFrequencyAsync(request.PatientId, cancellationToken);
            if (pnFreqResult.Severity == RuleSeverity.HardStop)
            {
                return Results.UnprocessableEntity(new
                {
                    error = pnFreqResult.Message,
                    ruleId = pnFreqResult.RuleId,
                    data = pnFreqResult.Data
                });
            }
        }

        // Sprint S: 8-minute rule validation — runs only when TotalMinutes is explicitly provided.
        // Negative TotalMinutes → 400; malformed CptCodesJson when TotalMinutes present → 400;
        // rules engine Error → 400; Warning → advisory in response (note still created).
        ComplianceWarning? complianceWarning = null;
        if (request.TotalMinutes.HasValue)
        {
            if (request.TotalMinutes.Value < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(request.TotalMinutes), ["TotalMinutes must be zero or greater."] }
                });

            if (!string.IsNullOrWhiteSpace(request.CptCodesJson) && request.CptCodesJson != "[]")
            {
                var cptCodes = TryDeserializeCptCodes(request.CptCodesJson);
                if (cptCodes is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { nameof(request.CptCodesJson), ["CptCodesJson is not valid JSON."] }
                    });

                if (cptCodes.Any(c => c.IsTimed))
                {
                    var eightMinResult = await rulesEngine.ValidateEightMinuteRuleAsync(
                        request.TotalMinutes.Value, cptCodes, cancellationToken);
                    if (!eightMinResult.IsValid)
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            { "8MinuteRule", [eightMinResult.Message] }
                        });
                    if (eightMinResult.Severity == RuleSeverity.Warning)
                        complianceWarning = ToComplianceWarning(eightMinResult);
                }
            }
        }

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();

        var note = new ClinicalNote
        {
            PatientId = request.PatientId,
            AppointmentId = request.AppointmentId,
            NoteType = request.NoteType,
            ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson,
            DateOfService = request.DateOfService,
            CptCodesJson = string.IsNullOrWhiteSpace(request.CptCodesJson) ? "[]" : request.CptCodesJson,
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/notes/{note.Id}", new NoteOperationResponse
        {
            Note = ToResponse(note),
            ComplianceWarning = complianceWarning
        });
    }

    // PUT /api/notes/{id}
    private static async Task<IResult> UpdateNote(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        [FromServices] IRulesEngine rulesEngine,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {id} not found." });

        // Sprint S: Signature locking — enforce note immutability via the rules engine.
        // Signed notes cannot be modified; clinicians must create an addendum instead.
        var immutabilityResult = await rulesEngine.ValidateImmutabilityAsync(note.Id, cancellationToken);
        if (!immutabilityResult.IsValid)
        {
            return Results.Conflict(new { error = immutabilityResult.Message });
        }

        // Sprint S: 8-minute rule validation on update — runs only when TotalMinutes is provided.
        // Uses the incoming CptCodesJson (if being updated) or the existing saved value.
        // Negative TotalMinutes → 400; malformed JSON when TotalMinutes present → 400;
        // rules engine Error → 400; Warning → advisory in response (update still saved).
        ComplianceWarning? complianceWarning = null;
        if (request.TotalMinutes.HasValue)
        {
            if (request.TotalMinutes.Value < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(request.TotalMinutes), ["TotalMinutes must be zero or greater."] }
                });

            var effectiveCptCodesJson = request.CptCodesJson ?? note.CptCodesJson;
            if (!string.IsNullOrWhiteSpace(effectiveCptCodesJson) && effectiveCptCodesJson != "[]")
            {
                var cptCodes = TryDeserializeCptCodes(effectiveCptCodesJson);
                if (cptCodes is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        { nameof(request.CptCodesJson), ["CptCodesJson is not valid JSON."] }
                    });

                if (cptCodes.Any(c => c.IsTimed))
                {
                    var eightMinResult = await rulesEngine.ValidateEightMinuteRuleAsync(
                        request.TotalMinutes.Value, cptCodes, cancellationToken);
                    if (!eightMinResult.IsValid)
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            { "8MinuteRule", [eightMinResult.Message] }
                        });
                    if (eightMinResult.Severity == RuleSeverity.Warning)
                        complianceWarning = ToComplianceWarning(eightMinResult);
                }
            }
        }

        if (request.ContentJson is not null)
            note.ContentJson = request.ContentJson;

        if (request.DateOfService is not null)
            note.DateOfService = request.DateOfService.Value;

        if (request.CptCodesJson is not null)
            note.CptCodesJson = request.CptCodesJson;

        var userId = identityContext.GetCurrentUserId();
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = userId;
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        // Sprint S: Audit logging — record every successful note edit.
        await auditService.LogNoteEditedAsync(AuditEvent.NoteEdited(note.Id, userId), cancellationToken);

        return Results.Ok(new NoteOperationResponse
        {
            Note = ToResponse(note),
            ComplianceWarning = complianceWarning
        });
    }

    // POST /api/notes/{noteId}/accept-ai-suggestion
    private static async Task<IResult> AcceptAiSuggestion(
        Guid noteId,
        [FromBody] AiSuggestionAcceptanceRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        [FromServices] IRulesEngine rulesEngine,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.GeneratedText))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.GeneratedText), ["GeneratedText is required."] }
            });

        if (string.IsNullOrWhiteSpace(request.Section))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.Section), ["Section is required."] }
            });

        if (string.IsNullOrWhiteSpace(request.GenerationType))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.GenerationType), ["GenerationType is required."] }
            });

        var note = await db.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {noteId} not found." });

        // Sprint UC-Gamma AI guardrail: AI content CANNOT be written to a signed note.
        // A signed note is immutable; the clinician must create an addendum instead.
        var immutabilityResult = await rulesEngine.ValidateImmutabilityAsync(note.Id, cancellationToken);
        if (!immutabilityResult.IsValid)
        {
            return Results.Conflict(new
            {
                error = "AI-generated content cannot be accepted into a signed note. Create an addendum instead.",
                ruleId = immutabilityResult.RuleId
            });
        }

        // Merge the accepted AI content into the target SOAP section of ContentJson.
        note.ContentJson = MergeAiContentIntoSection(note.ContentJson, request.Section, request.GeneratedText);

        var userId = identityContext.GetCurrentUserId();
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = userId;
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        // Audit: log the explicit clinician acceptance of AI-generated content.
        // NO PHI — only generation type and note identity.
        await auditService.LogAiGenerationAcceptedAsync(
            AuditEvent.AiGenerationAccepted(note.Id, request.GenerationType, userId),
            cancellationToken);

        return Results.Ok(new { note = ToResponse(note) });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a RuleResult to a ComplianceWarning DTO for inclusion in the response.
    /// Called only when the rule fired at Warning severity.
    /// </summary>
    private static ComplianceWarning ToComplianceWarning(RuleResult result) => new()
    {
        RuleId = result.RuleId,
        Message = result.Message,
        Data = result.Data
    };

    /// <summary>
    /// Attempts to deserialize a CPT codes JSON string.
    /// Returns null when the JSON is malformed — callers that have <c>TotalMinutes</c> set
    /// must treat a null return as a validation failure and reject the request.
    /// </summary>
    private static List<CptCodeEntry>? TryDeserializeCptCodes(string cptCodesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Merges AI-generated text into the specified section of the note's ContentJson.
    /// Creates the section key if it does not exist; replaces it if it does.
    /// The section key is always stored as lower-case for consistency.
    /// Sprint UC-Gamma: called only from <see cref="AcceptAiSuggestion"/>
    /// after the clinician explicitly accepts the generated content.
    /// </summary>
    internal static string MergeAiContentIntoSection(string contentJson, string section, string generatedText)
    {
        Dictionary<string, object>? content;
        try
        {
            content = JsonSerializer.Deserialize<Dictionary<string, object>>(
                contentJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            content = null;
        }

        content ??= new Dictionary<string, object>();
        content[section.ToLowerInvariant()] = generatedText;
        return JsonSerializer.Serialize(content);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static NoteResponse ToResponse(ClinicalNote n) => new()
    {
        Id = n.Id,
        PatientId = n.PatientId,
        AppointmentId = n.AppointmentId,
        NoteType = n.NoteType,
        ContentJson = n.ContentJson,
        DateOfService = n.DateOfService,
        SignatureHash = n.SignatureHash,
        SignedUtc = n.SignedUtc,
        SignedByUserId = n.SignedByUserId,
        CptCodesJson = n.CptCodesJson,
        ClinicId = n.ClinicId,
        LastModifiedUtc = n.LastModifiedUtc,
        ObjectiveMetrics = n.ObjectiveMetrics.Select(m => new ObjectiveMetricResponse
        {
            Id = m.Id,
            NoteId = m.NoteId,
            BodyPart = m.BodyPart,
            MetricType = m.MetricType,
            Value = m.Value,
            IsWNL = m.IsWNL
        }).ToList()
    };

    /// <summary>
    /// Sprint UC3: PTA domain guard — determines whether a PTA user is blocked from creating
    /// a given note type. PTAs may only create Daily notes; all other note types are blocked.
    /// Extracted as an internal helper so the guard logic can be tested directly without
    /// invoking the full endpoint stack.
    /// </summary>
    /// <param name="user">The authenticated ClaimsPrincipal.</param>
    /// <param name="noteType">The note type being created.</param>
    /// <returns>True if the request should be rejected with 403 Forbidden.</returns>
    internal static bool PtaIsBlockedFromNoteType(System.Security.Claims.ClaimsPrincipal user, NoteType noteType)
        => user.IsInRole(Roles.PTA) && noteType != NoteType.Daily;
}
