using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Intake;

/// <summary>
/// Endpoints for patient intake responses.
/// Supports tenant scoping and locking after evaluation.
/// Sprint O: TDD §6.2 Intake APIs
/// </summary>
public static class IntakeEndpoints
{
    public static void MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/intake")
            .RequireAuthorization()
            .WithTags("Intake");

        group.MapPost("/", CreateIntake)
            .WithName("CreateIntake")
            .WithSummary("Create an intake response for a patient");

        group.MapGet("/{id:guid}", GetIntake)
            .WithName("GetIntake")
            .WithSummary("Get an intake response by ID");
    }

    // POST /api/intake
    private static async Task<IResult> CreateIntake(
        [FromBody] CreateIntakeRequest request,
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

        // Verify the patient exists and is visible to this tenant
        var patientExists = await db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PatientId, cancellationToken);

        if (!patientExists)
            return Results.NotFound(new { error = $"Patient {request.PatientId} not found." });

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();

        var intake = new IntakeForm
        {
            PatientId = request.PatientId,
            PainMapData = request.PainMapData,
            Consents = request.Consents,
            ResponseJson = request.ResponseJson,
            TemplateVersion = request.TemplateVersion,
            IsLocked = false,
            AccessToken = Guid.NewGuid().ToString("N"), // Hashed token placeholder
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.IntakeForms.Add(intake);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/intake/{intake.Id}", ToResponse(intake));
    }

    // GET /api/intake/{id}
    private static async Task<IResult> GetIntake(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        return Results.Ok(ToResponse(intake));
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static IntakeResponse ToResponse(IntakeForm f) => new()
    {
        Id = f.Id,
        PatientId = f.PatientId,
        PainMapData = f.PainMapData,
        Consents = f.Consents,
        ResponseJson = f.ResponseJson,
        Locked = f.IsLocked,
        TemplateVersion = f.TemplateVersion,
        SubmittedAt = f.SubmittedAt,
        ClinicId = f.ClinicId,
        LastModifiedUtc = f.LastModifiedUtc
    };
}
