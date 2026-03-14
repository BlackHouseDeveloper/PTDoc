using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Notes;

/// <summary>
/// CRUD endpoints for clinical notes.
/// PUT is restricted to draft (unsigned) notes per Medicare immutability rules.
/// Sprint O: TDD §6.3 Clinical Notes APIs
/// </summary>
public static class NoteEndpoints
{
    public static void MapNoteCrudEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notes")
            .RequireAuthorization()
            .WithTags("Notes");

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

        // Validate appointment FK if provided
        if (request.AppointmentId.HasValue)
        {
            var appointmentExists = await db.Appointments
                .AsNoTracking()
                .AnyAsync(a => a.Id == request.AppointmentId.Value, cancellationToken);

            if (!appointmentExists)
                return Results.UnprocessableEntity(new { error = $"Appointment {request.AppointmentId} not found." });
        }

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();

        var note = new ClinicalNote
        {
            PatientId = request.PatientId,
            AppointmentId = request.AppointmentId,
            NoteType = request.NoteType,
            ContentJson = request.ContentJson,
            DateOfService = request.DateOfService,
            CptCodesJson = request.CptCodesJson,
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.ClinicalNotes.Add(note);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/notes/{note.Id}", ToResponse(note));
    }

    // PUT /api/notes/{id}
    private static async Task<IResult> UpdateNote(
        Guid id,
        [FromBody] UpdateNoteRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .Include(n => n.ObjectiveMetrics)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {id} not found." });

        // Signed notes are immutable per Medicare requirements (TDD §3 + §8.2)
        if (note.SignedUtc.HasValue)
            return Results.Conflict(new { error = "Signed notes cannot be modified. Use POST /api/notes/{id}/addendum to append." });

        if (request.ContentJson is not null)
            note.ContentJson = request.ContentJson;

        if (request.DateOfService is not null)
            note.DateOfService = request.DateOfService.Value;

        if (request.CptCodesJson is not null)
            note.CptCodesJson = request.CptCodesJson;

        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = identityContext.GetCurrentUserId();
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(note));
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
