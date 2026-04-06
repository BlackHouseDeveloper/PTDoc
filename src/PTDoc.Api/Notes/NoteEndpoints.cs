using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
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
    private static readonly JsonSerializerOptions WorkspaceContentSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions CptSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
        // Read-only endpoints — NoteRead policy (includes Billing role)
        var readGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteRead);

        readGroup.MapGet("/", ListNotes)
            .WithName("ListNotes")
            .WithSummary("List clinical notes with optional filtering");

        readGroup.MapGet("/{id:guid}", GetNoteById)
            .WithName("GetNoteById")
            .WithSummary("Get a clinical note with linked addendums");

        readGroup.MapPost("/batch-read", BatchReadNotes)
            .WithName("BatchReadNotes")
            .WithSummary("Get a bounded set of clinical note details");

        readGroup.MapPost("/export/preview-target", ResolveExportPreviewTarget)
            .WithName("ResolveExportPreviewTarget")
            .WithSummary("Resolve the note used for the current export preview");

        // Write endpoints — NoteWrite policy (PT and PTA only)
        var writeGroup = app.MapGroup("/api/v1/notes")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        writeGroup.MapPost("/", CreateNote)
            .WithName("CreateNote")
            .WithSummary("Create a new clinical note");

        writeGroup.MapPut("/{id:guid}", UpdateNote)
            .WithName("UpdateNote")
            .WithSummary("Update a draft clinical note");

        writeGroup.MapPost("/{noteId:guid}/override", ApplyOverride)
            .WithName("ApplyNoteOverride")
            .WithSummary("Apply a PT-attested compliance override to an existing note");

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
        readGroup.MapGet("/carry-forward", GetCarryForward)
            .WithName("GetCarryForward")
            .WithSummary("Get carry-forward data from the most recent signed note for a patient")
            .WithDescription(
                "Returns read-only content from the most recently signed note eligible as a " +
                "carry-forward source. Returns 404 when no eligible signed note exists.");
    }

    // GET /api/v1/notes
    private static async Task<IResult> ListNotes(
        [FromQuery] Guid? patientId,
        [FromQuery] string? noteType,
        [FromQuery] string? status,
        [FromQuery] int take,
        [FromQuery] string? categoryId,
        [FromQuery] string? itemId,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedTake = take <= 0 ? 100 : Math.Min(take, 500);

        var query = db.ClinicalNotes
            .AsNoTracking()
            .Where(n => !n.IsAddendum)
            .AsQueryable();

        if (patientId.HasValue)
            query = query.Where(n => n.PatientId == patientId.Value);

        if (!string.IsNullOrWhiteSpace(noteType) &&
            Enum.TryParse<NoteType>(noteType, ignoreCase: true, out var parsedType))
            query = query.Where(n => n.NoteType == parsedType);

        // Status filter: "signed" means fully finalized; "unsigned" means not fully finalized
        if (status?.Equals("signed", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(n => n.NoteStatus == NoteStatus.Signed);
        else if (status?.Equals("unsigned", StringComparison.OrdinalIgnoreCase) == true)
            query = query.Where(n => n.NoteStatus != NoteStatus.Signed);

        // Taxonomy filter: use ANY/EXISTS predicate for an efficient SQL plan.
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            query = query.Where(n =>
                db.NoteTaxonomySelections.Any(s =>
                    s.ClinicalNoteId == n.Id &&
                    s.CategoryId == categoryId &&
                    (string.IsNullOrWhiteSpace(itemId) || s.ItemId == itemId)));
        }

        var notes = await query
            .OrderByDescending(n => n.DateOfService)
            .Take(normalizedTake)
            .Select(n => new NoteListItemApiResponse
            {
                Id = n.Id,
                PatientId = n.PatientId,
                PatientName = n.Patient != null
                    ? n.Patient.FirstName + " " + n.Patient.LastName
                    : string.Empty,
                NoteType = n.NoteType.ToString(),
                IsSigned = n.NoteStatus == NoteStatus.Signed,
                NoteStatus = n.NoteStatus,
                DateOfService = n.DateOfService,
                LastModifiedUtc = n.LastModifiedUtc,
                CptCodesJson = n.CptCodesJson
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(notes);
    }

    private static async Task<IResult> GetNoteById(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {id} not found." });

        if (note.IsAddendum)
        {
            if (note.ParentNoteId is null)
                return Results.NotFound(new { error = $"Primary note for addendum {id} not found." });

            note = await db.ClinicalNotes
                .AsNoTracking()
                .Include(n => n.ObjectiveMetrics)
                .FirstOrDefaultAsync(
                    n => n.Id == note.ParentNoteId.Value && !n.IsAddendum,
                    cancellationToken);

            if (note is null)
                return Results.NotFound(new { error = $"Primary note for addendum {id} not found." });
        }

        var linkedAddendumNotes = await db.ClinicalNotes
            .AsNoTracking()
            .Where(n => n.ParentNoteId == note.Id && n.IsAddendum)
            .OrderBy(n => n.CreatedUtc)
            .ThenBy(n => n.LastModifiedUtc)
            .ToListAsync(cancellationToken);

        var legacyAddendumRows = await db.Addendums
            .AsNoTracking()
            .Where(a => a.ClinicalNoteId == note.Id)
            .OrderBy(a => a.CreatedUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(new NoteDetailResponse
        {
            Note = ToResponse(note),
            Addendums = linkedAddendumNotes
                .Select(MapLinkedAddendum)
                .Concat(legacyAddendumRows.Select(MapLegacyAddendum))
                .OrderBy(a => a.CreatedUtc)
                .ThenBy(a => a.LastModifiedUtc)
                .ToList()
        });
    }

    private static async Task<IResult> BatchReadNotes(
        [FromBody] BatchNoteReadRequest request,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var noteIds = (request.NoteIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(100)
            .ToList();

        if (noteIds.Count == 0)
        {
            return Results.Ok(Array.Empty<NoteDetailResponse>());
        }

        var notes = await db.ClinicalNotes
            .AsNoTracking()
            .Include(note => note.ObjectiveMetrics)
            .Where(note => noteIds.Contains(note.Id) && !note.IsAddendum)
            .ToListAsync(cancellationToken);

        var orderIndex = noteIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index);

        var results = notes
            .Select(note => new NoteDetailResponse
            {
                Note = ToResponse(note),
                Addendums = Array.Empty<NoteAddendumResponse>()
            })
            .OrderBy(result => orderIndex.TryGetValue(result.Note!.Id, out var idx) ? idx : int.MaxValue)
            .ToList();

        return Results.Ok(results);
    }

    private static async Task<IResult> ResolveExportPreviewTarget(
        [FromBody] ExportPreviewTargetRequest request,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var invalidFilter = (request.NoteTypeFilters ?? Array.Empty<string>())
            .FirstOrDefault(filter => !TryMapPreviewFilter(filter, out _));
        if (!string.IsNullOrWhiteSpace(invalidFilter))
        {
            return Results.Ok(new ExportPreviewTargetResponse
            {
                UnavailableReason = $"Preview cannot apply the selected note-type filter '{invalidFilter}'."
            });
        }

        var patientIds = (request.PatientIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var previewFilters = new List<PreviewNoteFilter>();
        foreach (var filter in request.NoteTypeFilters ?? Array.Empty<string>())
        {
            if (TryMapPreviewFilter(filter, out var mapped))
            {
                previewFilters.Add(mapped);
            }
        }

        var query = db.ClinicalNotes
            .AsNoTracking()
            .Include(note => note.Patient)
            .Where(note => !note.IsAddendum);

        if (patientIds.Count > 0)
        {
            query = query.Where(note => patientIds.Contains(note.PatientId));
        }

        if (request.DateRangeStart.HasValue)
        {
            var start = request.DateRangeStart.Value.Date;
            query = query.Where(note => note.DateOfService.Date >= start);
        }

        if (request.DateRangeEnd.HasValue)
        {
            var end = request.DateRangeEnd.Value.Date;
            query = query.Where(note => note.DateOfService.Date <= end);
        }

        if (previewFilters.Count > 0)
        {
            query = query.Where(note => previewFilters.Any(filter =>
                filter.NoteType == note.NoteType &&
                filter.IsReEvaluation == note.IsReEvaluation));
        }

        var matchingNotes = await query
            .OrderByDescending(note => note.DateOfService)
            .ThenByDescending(note => note.LastModifiedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        if (matchingNotes.Count == 0)
        {
            return Results.Ok(new ExportPreviewTargetResponse
            {
                UnavailableReason = "No SOAP note matches the current export filters."
            });
        }

        var previewNote = matchingNotes.FirstOrDefault(note => note.NoteStatus == NoteStatus.Signed)
            ?? matchingNotes[0];

        return Results.Ok(new ExportPreviewTargetResponse
        {
            NoteId = previewNote.Id,
            Title = BuildPreviewTitle(previewNote),
            Subtitle = $"{previewNote.DateOfService:MMM d, yyyy} · {BuildPreviewStatusLabel(previewNote.NoteStatus)}",
            NoteStatus = previewNote.NoteStatus,
            SelectionNotice = BuildPreviewSelectionNotice(patientIds.Count, previewFilters.Count),
            CanDownloadPdf = previewNote.NoteStatus == NoteStatus.Signed
        });
    }

    // POST /api/v1/notes
    private static async Task<IResult> CreateNote(
        [FromBody] CreateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] INoteWriteService noteWriteService,
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

        try
        {
            var result = await noteWriteService.CreateAsync(request, cancellationToken);

            if (!result.IsValid)
                return Results.UnprocessableEntity(result);

            if (result.Note is null)
                return Results.Problem(
                    title: "Note creation returned an invalid success response.",
                    detail: "The note write service reported success but did not provide a note.",
                    statusCode: StatusCodes.Status500InternalServerError);

            return Results.Created($"/api/v1/notes/{result.Note.Id}", result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(
                new NoteOperationResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return ToValidationProblem(ex, nameof(request.CptCodesJson), nameof(request.TotalMinutes));
        }
    }

    // PUT /api/notes/{id}
    private static async Task<IResult> UpdateNote(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IRulesEngine rulesEngine,
        [FromServices] IAuditService auditService,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] INoteWriteService noteWriteService,
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

        if (note.NoteStatus != NoteStatus.Draft)
        {
            return Results.Conflict(new
            {
                error = note.NoteStatus == NoteStatus.PendingCoSign
                    ? "Pending notes are read-only while awaiting PT co-signature."
                    : "Signed notes cannot be modified. Create an addendum instead."
            });
        }

        // Sprint S: Signature locking — enforce note immutability via the rules engine.
        // Signed notes cannot be modified; clinicians must create an addendum instead.
        var immutabilityResult = await rulesEngine.ValidateImmutabilityAsync(note.Id, cancellationToken);
        if (!immutabilityResult.IsValid)
        {
            await auditService.LogRuleEvaluationAsync(
                AuditEvent.EditBlockedSignedNote(note.Id, identityContext.TryGetCurrentUserId(), "NoteEndpoints.UpdateNote"),
                cancellationToken);
            return Results.Conflict(new { error = "Signed notes cannot be modified. Create addendum." });
        }

        try
        {
            var result = await noteWriteService.UpdateAsync(note, request, cancellationToken);
            return result.IsValid
                ? Results.Ok(result)
                : Results.UnprocessableEntity(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(
                new NoteOperationResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return ToValidationProblem(ex, nameof(request.CptCodesJson), nameof(request.TotalMinutes));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ApplyOverride(
        Guid noteId,
        [FromBody] OverrideSubmission request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] INoteSaveValidationService validationService,
        [FromServices] IOverrideService overrideService,
        [FromServices] IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!request.RuleType.HasValue)
        {
            errors[nameof(request.RuleType)] = ["RuleType is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors[nameof(request.Reason)] = ["Reason is required."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var note = await db.ClinicalNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Id == noteId, cancellationToken);

        if (note is null)
        {
            return Results.NotFound(new { error = $"Note {noteId} not found." });
        }

        var cptEntries = TryDeserializeCptEntries(note.CptCodesJson);
        if (cptEntries is null)
        {
            return Results.Conflict(new { error = "Stored CPT data is invalid and cannot be evaluated for override." });
        }

        var totalTimedMinutes = cptEntries
            .Where(entry => entry.IsTimed)
            .Sum(entry => entry.Minutes ?? 0);

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = note.PatientId,
            ExistingNoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = note.DateOfService,
            TotalTimedMinutes = totalTimedMinutes,
            CptEntries = cptEntries
        }, cancellationToken);

        var ruleType = request.RuleType!.Value;
        var matchingRequirement = validation.OverrideRequirements
            .FirstOrDefault(requirement => requirement.RuleType == ruleType);

        if (matchingRequirement is null)
        {
            if (ruleType == ComplianceRuleType.ProgressNoteRequired)
            {
                await auditService.LogRuleEvaluationAsync(
                    AuditEvent.HardStopTriggered(noteId, ruleType, identityContext.TryGetCurrentUserId()),
                    cancellationToken);
            }

            return Results.UnprocessableEntity(new
            {
                error = $"No active overridable compliance rule matched rule type '{ruleType}'.",
                overrideRequirements = validation.OverrideRequirements
            });
        }

        try
        {
            await overrideService.ApplyOverrideAsync(new OverrideRequest
            {
                NoteId = noteId,
                RuleType = ruleType,
                Reason = request.Reason,
                AttestedBy = identityContext.GetCurrentUserId(),
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            return Results.Ok(new { success = true });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Reason)] = [ex.Message]
            });
        }
        catch (InvalidOperationException ex)
        {
            if (ruleType == ComplianceRuleType.ProgressNoteRequired)
            {
                await auditService.LogRuleEvaluationAsync(
                    AuditEvent.HardStopTriggered(noteId, ruleType, identityContext.TryGetCurrentUserId()),
                    cancellationToken);
            }

            return Results.UnprocessableEntity(new { error = ex.Message });
        }
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

        if (note.NoteStatus != NoteStatus.Draft)
        {
            return Results.Conflict(new
            {
                error = note.NoteStatus == NoteStatus.PendingCoSign
                    ? "AI-generated content cannot be accepted while a note is pending PT co-signature."
                    : "AI-generated content cannot be accepted into a signed note. Create an addendum instead."
            });
        }

        // Sprint UC-Gamma AI guardrail: AI content CANNOT be written to a signed note.
        // A signed note is immutable; the clinician must create an addendum instead.
        var immutabilityResult = await rulesEngine.ValidateImmutabilityAsync(note.Id, cancellationToken);
        if (!immutabilityResult.IsValid)
        {
            await auditService.LogRuleEvaluationAsync(
                AuditEvent.EditBlockedSignedNote(note.Id, identityContext.TryGetCurrentUserId(), "NoteEndpoints.AcceptAiSuggestion"),
                cancellationToken);
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

    private static IResult ToValidationProblem(ArgumentException exception, string cptCodesField, string totalMinutesField)
    {
        if (string.Equals(exception.ParamName, nameof(CreateNoteRequest.CptCodesJson), StringComparison.Ordinal)
            || string.Equals(exception.ParamName, nameof(UpdateNoteRequest.CptCodesJson), StringComparison.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { cptCodesField, ["CptCodesJson is not valid JSON."] }
            });
        }

        if (string.Equals(exception.ParamName, nameof(CreateNoteRequest.TotalMinutes), StringComparison.Ordinal)
            || string.Equals(exception.ParamName, nameof(UpdateNoteRequest.TotalMinutes), StringComparison.Ordinal))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { totalMinutesField, ["TotalMinutes must be zero or greater."] }
            });
        }

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            { "request", [exception.Message] }
        });
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
        if (TryMergeIntoWorkspaceV2(contentJson, section, generatedText, out var workspaceJson))
        {
            return workspaceJson;
        }

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

    private static bool TryMergeIntoWorkspaceV2(
        string contentJson,
        string section,
        string generatedText,
        out string mergedJson)
    {
        mergedJson = string.Empty;

        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(contentJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (!TryReadSchemaVersion(document.RootElement, out var schemaVersion)
                || schemaVersion != WorkspaceSchemaVersions.EvalReevalProgressV2)
            {
                return false;
            }
        }

        NoteWorkspaceV2Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, WorkspaceContentSerializerOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (payload is null)
        {
            return false;
        }

        switch (section.ToLowerInvariant())
        {
            case "subjective":
                payload.Subjective.NarrativeContext.PatientHistorySummary = generatedText;
                break;

            case "objective":
                payload.Objective.ClinicalObservationNotes = generatedText;
                break;

            case "assessment":
                payload.Assessment.AssessmentNarrative = generatedText;
                break;

            case "plan":
                payload.Plan.ClinicalSummary = generatedText;
                if (string.IsNullOrWhiteSpace(payload.Plan.PlanOfCareNarrative))
                {
                    payload.Plan.PlanOfCareNarrative = generatedText;
                }
                break;

            case "goals":
                payload.Assessment.GoalSuggestions =
                    ParseGoalSuggestions(generatedText)
                        .Select(description => new WorkspaceGoalSuggestionV2
                        {
                            Description = description
                        })
                        .ToList();
                break;

            case "billing":
                payload.Plan.FollowUpInstructions = generatedText;
                break;

            default:
                return false;
        }

        mergedJson = JsonSerializer.Serialize(payload, WorkspaceContentSerializerOptions);
        return true;
    }

    private static bool TryReadSchemaVersion(JsonElement root, out int schemaVersion)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            schemaVersion = default;
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out schemaVersion))
            {
                return true;
            }

            break;
        }

        schemaVersion = default;
        return false;
    }

    private static IReadOnlyList<string> ParseGoalSuggestions(string generatedText)
    {
        return generatedText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Select(line => line.TrimStart('-', '*', '•', ' ', '\t'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CptCodeEntry>? TryDeserializeCptEntries(string? cptCodesJson)
    {
        if (string.IsNullOrWhiteSpace(cptCodesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson, CptSerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static NoteResponse ToResponse(ClinicalNote n) => new()
    {
        Id = n.Id,
        PatientId = n.PatientId,
        AppointmentId = n.AppointmentId,
        ParentNoteId = n.ParentNoteId,
        IsAddendum = n.IsAddendum,
        NoteType = n.NoteType,
        IsReEvaluation = n.IsReEvaluation,
        NoteStatus = n.NoteStatus,
        ContentJson = n.ContentJson,
        DateOfService = n.DateOfService,
        CreatedUtc = n.CreatedUtc,
        SignatureHash = n.SignatureHash,
        SignedUtc = n.SignedUtc,
        SignedByUserId = n.SignedByUserId,
        CptCodesJson = n.CptCodesJson,
        TherapistNpi = n.TherapistNpi,
        TotalTreatmentMinutes = n.TotalTreatmentMinutes,
        ClinicId = n.ClinicId,
        LastModifiedUtc = n.LastModifiedUtc,
        ObjectiveMetrics = n.ObjectiveMetrics.Select(m => new ObjectiveMetricResponse
        {
            Id = m.Id,
            NoteId = m.NoteId,
            BodyPart = m.BodyPart,
            MetricType = m.MetricType,
            Value = m.Value,
            Side = m.Side,
            Unit = m.Unit,
            IsWNL = m.IsWNL,
            LastModifiedUtc = m.LastModifiedUtc
        }).ToList()
    };

    private static NoteAddendumResponse MapLinkedAddendum(ClinicalNote note) => new()
    {
        Id = note.Id,
        ParentNoteId = note.ParentNoteId,
        IsLegacy = false,
        IsSigned = note.IsFinalized,
        CreatedUtc = note.CreatedUtc,
        LastModifiedUtc = note.LastModifiedUtc,
        Content = note.ContentJson,
        ContentFormat = "json",
        SignatureHash = note.SignatureHash,
        SignedUtc = note.SignedUtc,
        SignedByUserId = note.SignedByUserId,
        NoteType = note.NoteType
    };

    private static NoteAddendumResponse MapLegacyAddendum(Addendum addendum) => new()
    {
        Id = addendum.Id,
        ParentNoteId = addendum.ClinicalNoteId,
        IsLegacy = true,
        IsSigned = !string.IsNullOrWhiteSpace(addendum.SignatureHash),
        CreatedUtc = addendum.CreatedUtc,
        LastModifiedUtc = addendum.CreatedUtc,
        Content = addendum.Content,
        ContentFormat = "text",
        SignatureHash = addendum.SignatureHash,
        NoteType = null
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

    private static string BuildPreviewTitle(ClinicalNote note)
    {
        var noteTypeLabel = note.NoteType == NoteType.Evaluation && note.IsReEvaluation
            ? "Re-Evaluation"
            : note.NoteType.ToString();
        var patientName = note.Patient is null
            ? "Unknown patient"
            : $"{note.Patient.FirstName} {note.Patient.LastName}".Trim();
        return $"{noteTypeLabel} for {patientName}";
    }

    private static string BuildPreviewStatusLabel(NoteStatus noteStatus) => noteStatus switch
    {
        NoteStatus.Signed => "Signed",
        NoteStatus.PendingCoSign => "Pending co-sign",
        _ => "Draft"
    };

    private static string? BuildPreviewSelectionNotice(int selectedPatientCount, int selectedNoteTypeCount)
    {
        if (selectedPatientCount == 0 && selectedNoteTypeCount == 0)
        {
            return "Showing the most recent signed SOAP note in the current export range when available.";
        }

        if (selectedPatientCount == 0)
        {
            return "Showing the most recent note that matches the selected date and note-type filters, preferring signed notes.";
        }

        if (selectedNoteTypeCount == 0)
        {
            return "Showing the most recent note for the selected patient filters, preferring signed notes.";
        }

        return null;
    }

    private static bool TryMapPreviewFilter(string filter, out PreviewNoteFilter previewFilter)
    {
        switch (filter)
        {
            case "initial-eval":
                previewFilter = new PreviewNoteFilter(NoteType.Evaluation, IsReEvaluation: false);
                return true;
            case "re-eval":
                previewFilter = new PreviewNoteFilter(NoteType.Evaluation, IsReEvaluation: true);
                return true;
            case "progress-note":
                previewFilter = new PreviewNoteFilter(NoteType.ProgressNote, IsReEvaluation: false);
                return true;
            case "daily-note":
                previewFilter = new PreviewNoteFilter(NoteType.Daily, IsReEvaluation: false);
                return true;
            case "discharge-summary":
                previewFilter = new PreviewNoteFilter(NoteType.Discharge, IsReEvaluation: false);
                return true;
            default:
                previewFilter = default;
                return false;
        }
    }

    private readonly record struct PreviewNoteFilter(NoteType NoteType, bool IsReEvaluation);
}
