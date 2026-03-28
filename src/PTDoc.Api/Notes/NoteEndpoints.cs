using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
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
/// Sprint UC-Gamma: PTA domain guard on create and update; carry-forward read endpoint;
///                  AI acceptance gate with section validation.
/// </summary>
public static class NoteEndpoints
{
    /// <summary>
    /// SOAP section names that are valid targets for AI-generated content acceptance.
    /// Sprint UC-Gamma: rejects any section name outside this canonical set.
    /// </summary>
    internal static readonly HashSet<string> ValidSoapSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "subjective", "objective", "assessment", "plan", "goals", "billing"
    };

    public static void MapNoteCrudEndpoints(this IEndpointRouteBuilder app)
    {
        var writeGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        writeGroup.MapPost("/", CreateNote)
            .WithName("CreateNote")
            .WithSummary("Create a new clinical note");

        writeGroup.MapPut("/{id:guid}", UpdateNote)
            .WithName("UpdateNote")
            .WithSummary("Update a draft clinical note");

        // Sprint UC-Gamma: AI output acceptance gate.
        // AI-generated content is NEVER persisted automatically — a clinician must
        // explicitly call this endpoint to write generated text into a draft note.
        writeGroup.MapPost("/{noteId:guid}/accept-ai-suggestion", AcceptAiSuggestion)
            .WithName("AcceptAiSuggestion")
            .WithSummary("Accept AI-generated content into a specific section of a draft note")
            .WithDescription(
                "Explicit clinician acceptance gate. AI output is not persisted until " +
                "a clinician (PT or PTA) calls this endpoint. Blocked on signed notes.");

        // Sprint UC-Gamma: carry-forward read endpoint.
        // Returns the most recent signed note eligible as a carry-forward source for the
        // given patient and target note type. NoteRead policy — accessible to clinical staff
        // and billing; NoteWrite not required since this is a read operation.
        var readGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteRead);

        readGroup.MapGet("/carry-forward", GetCarryForward)
            .WithName("GetCarryForward")
            .WithSummary("Get carry-forward data from the most recent signed note for a patient")
            .WithDescription(
                "Returns read-only content from the most recently signed note eligible as a " +
                "carry-forward source. Returns 404 when no eligible signed note exists.");
    }

    // POST /api/notes
    private static async Task<IResult> CreateNote(
        [FromBody] CreateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IRulesEngine rulesEngine,
        [FromServices] ISyncEngine syncEngine,
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

                // Server-side enforcement: override IsTimed for known timed CPT codes
                // so that UI serialization cannot bypass 8-minute rule validation.
                EnforceKnownTimedCptStatus(cptCodes);

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
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Create, cancellationToken);

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
        [FromServices] ISyncEngine syncEngine,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {id} not found." });

        // Sprint UC-Gamma: PTA domain guard on update.
        // PTAs may only author Daily notes. Editing an Evaluation, ProgressNote, or Discharge
        // note is outside the PTA scope of practice, even when the note was created by a PT.
        if (PtaIsBlockedFromNoteType(httpContext.User, note.NoteType))
        {
            return Results.Forbid();
        }

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

                // Server-side enforcement: override IsTimed for known timed CPT codes
                // so that UI serialization cannot bypass 8-minute rule validation.
                EnforceKnownTimedCptStatus(cptCodes);

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
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Update, cancellationToken);

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
        [FromServices] ISyncEngine syncEngine,
        HttpContext httpContext,
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

        // Sprint UC-Gamma: reject unknown SOAP section names to prevent drift
        // from the spec and to guard against unsanitized free-text payloads
        // being written to arbitrary keys in ContentJson.
        if (!ValidSoapSections.Contains(request.Section))
            return Results.UnprocessableEntity(new
            {
                errors = new Dictionary<string, string[]>
                {
                    { nameof(request.Section), [$"'{request.Section}' is not a valid SOAP section. Valid values: {string.Join(", ", ValidSoapSections.OrderBy(s => s))}."] }
                }
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

        // Sprint UC-Gamma: PTA domain guard mirrors UpdateNote.
        // PTAs cannot modify Eval/PN/Discharge notes even via AI suggestion acceptance.
        if (PtaIsBlockedFromNoteType(httpContext.User, note.NoteType))
        {
            return Results.Forbid();
        }

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
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Update, cancellationToken);

        // Audit: log the explicit clinician acceptance of AI-generated content.
        // NO PHI — only generation type and note identity.
        await auditService.LogAiGenerationAcceptedAsync(
            AuditEvent.AiGenerationAccepted(note.Id, request.GenerationType, userId),
            cancellationToken);

        return Results.Ok(new NoteOperationResponse
        {
            Note = ToResponse(note)
        });
    }

    // GET /api/notes/carry-forward?patientId={id}&noteType={type}
    private static async Task<IResult> GetCarryForward(
        [FromQuery] Guid patientId,
        [FromQuery] NoteType noteType,
        [FromServices] ICarryForwardService carryForwardService,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(patientId), ["patientId is required."] }
            });

        var data = await carryForwardService.GetCarryForwardDataAsync(patientId, noteType, cancellationToken);

        if (data is null)
            return Results.NotFound(new
            {
                error = "No eligible signed note found for carry-forward.",
                patientId,
                noteType = noteType.ToString()
            });

        return Results.Ok(new
        {
            sourceNoteId = data.SourceNoteId,
            sourceNoteType = data.SourceNoteType.ToString(),
            sourceNoteDateOfService = data.SourceNoteDateOfService,
            contentJson = data.ContentJson
        });
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
    /// Overrides the <see cref="CptCodeEntry.IsTimed"/> flag to <c>true</c> for any
    /// CPT code whose code value appears in the server-authoritative <see cref="KnownTimedCptCodes.Codes"/> set.
    /// This prevents the IsTimed flag from being stripped or defaulted to false by UI serialization,
    /// ensuring 8-minute rule enforcement cannot be bypassed at the client layer.
    /// Mutates the list in-place.
    /// </summary>
    internal static void EnforceKnownTimedCptStatus(List<CptCodeEntry> cptCodes)
    {
        foreach (var entry in cptCodes)
        {
            if (KnownTimedCptCodes.Codes.Contains(entry.Code))
                entry.IsTimed = true;
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
            // Treat malformed ContentJson as an empty document rather than propagating an
            // error. This intentionally recovers gracefully: the accepted AI text is still
            // written into the section, and the note's content can be corrected on the next
            // full save. Surfacing a 500 here would silently discard clinician acceptance work.
            content = null;
        }

        content ??= new Dictionary<string, object>();

        // Normalize all existing keys to lower-case to prevent duplicate entries that differ
        // only in casing (e.g., "Assessment" and "assessment" both present).
        var normalized = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var kvp in content)
            normalized[kvp.Key?.ToLowerInvariant() ?? string.Empty] = kvp.Value;

        // Always store the target section key in lower-case, overwriting any prior value.
        normalized[section.ToLowerInvariant()] = generatedText;
        return JsonSerializer.Serialize(normalized);
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
