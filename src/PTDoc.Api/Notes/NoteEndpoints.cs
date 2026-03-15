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
    }

    // POST /api/notes
    private static async Task<IResult> CreateNote(
        [FromBody] CreateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IRulesEngine rulesEngine,
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

        // Sprint S: 8-minute rule validation — validate CPT units when total minutes are provided.
        RuleResult? eightMinWarning = null;
        if (request.TotalMinutes.HasValue && !string.IsNullOrWhiteSpace(request.CptCodesJson) && request.CptCodesJson != "[]")
        {
            var cptCodes = TryDeserializeCptCodes(request.CptCodesJson);
            if (cptCodes is not null && cptCodes.Any(c => c.IsTimed))
            {
                var eightMinResult = await rulesEngine.ValidateEightMinuteRuleAsync(
                    request.TotalMinutes.Value, cptCodes, cancellationToken);
                if (eightMinResult.Severity == RuleSeverity.Warning)
                    eightMinWarning = eightMinResult;
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

        // Include any 8-minute rule advisory warning alongside the created note
        if (eightMinWarning is not null)
        {
            return Results.Created($"/api/v1/notes/{note.Id}", new
            {
                note = ToResponse(note),
                complianceWarning = new
                {
                    ruleId = eightMinWarning.RuleId,
                    message = eightMinWarning.Message,
                    data = eightMinWarning.Data
                }
            });
        }

        return Results.Created($"/api/v1/notes/{note.Id}", ToResponse(note));
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

        // Sprint S: 8-minute rule validation on update — if CPT codes and total minutes provided,
        // validate the updated billing units and include any advisory warning in the response.
        if (request.TotalMinutes.HasValue && !string.IsNullOrWhiteSpace(note.CptCodesJson) && note.CptCodesJson != "[]")
        {
            var cptCodes = TryDeserializeCptCodes(note.CptCodesJson);
            if (cptCodes is not null && cptCodes.Any(c => c.IsTimed))
            {
                var eightMinResult = await rulesEngine.ValidateEightMinuteRuleAsync(
                    request.TotalMinutes.Value, cptCodes, cancellationToken);
                if (eightMinResult.Severity == RuleSeverity.Warning)
                {
                    return Results.Ok(new
                    {
                        note = ToResponse(note),
                        complianceWarning = new
                        {
                            ruleId = eightMinResult.RuleId,
                            message = eightMinResult.Message,
                            data = eightMinResult.Data
                        }
                    });
                }
            }
        }

        return Results.Ok(ToResponse(note));
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static List<CptCodeEntry>? TryDeserializeCptCodes(string cptCodesJson)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            // Malformed CptCodesJson: skip 8-minute rule validation rather than failing the request.
            // The endpoint accepts arbitrary JSON strings for CptCodesJson; if the content is
            // unparseable the compliance check is bypassed and the note is still saved.
            return null;
        }
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
}
