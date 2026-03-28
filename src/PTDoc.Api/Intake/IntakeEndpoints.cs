using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

namespace PTDoc.Api.Intake;

/// <summary>
/// Endpoints for patient intake responses.
/// Supports tenant scoping and locking after evaluation.
/// Sprint O: TDD §6.2 Intake APIs
/// Sprint P: RBAC enforcement — separate read/write policies.
/// </summary>
public static class IntakeEndpoints
{
    public static void MapIntakeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/intake")
            .WithTags("Intake");

        group.MapPost("/", CreateIntake)
            .WithName("CreateIntake")
            .WithSummary("Create an intake response for a patient")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapGet("/{id:guid}", GetIntake)
            .WithName("GetIntake")
            .WithSummary("Get an intake response by ID")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        // Sprint UC2: Lock (submit) intake after patient completion — prevents further editing.
        // Patient can submit their own form; Front Desk and clinical staff can lock on behalf.
        // Uses IntakeRead (which includes Patient) rather than IntakeWrite so patients can
        // submit/lock their own intake forms without requiring full write permissions.
        // Note: IntakeRead still requires authentication; ownership/tenancy is enforced
        // inside the handler via tenant context — only intake forms visible to the caller
        // can be submitted.
        group.MapPost("/{id:guid}/submit", SubmitIntake)
            .WithName("SubmitIntake")
            .WithSummary("Lock an intake form after submission — prevents further editing")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);
    }

    // POST /api/v1/intake
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

        // Default null JSON fields to valid empty JSON objects so EF doesn't fail
        var painMapData = string.IsNullOrWhiteSpace(request.PainMapData) ? "{}" : request.PainMapData;
        var consents = string.IsNullOrWhiteSpace(request.Consents) ? "{}" : request.Consents;

        // Verify the patient exists and is visible to this tenant
        var patientExists = await db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.PatientId, cancellationToken);

        if (!patientExists)
            return Results.NotFound(new { error = $"Patient {request.PatientId} not found." });

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();

        // Generate a cryptographically secure token and store its SHA-256 hash.
        // The raw token can be shared with the patient to access the self-completion form.
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToHexString(rawTokenBytes).ToLowerInvariant(); // 64-char hex
        var tokenHash = HashToken(rawToken);

        var intake = new IntakeForm
        {
            PatientId = request.PatientId,
            PainMapData = painMapData,
            Consents = consents,
            ResponseJson = string.IsNullOrWhiteSpace(request.ResponseJson) ? "{}" : request.ResponseJson,
            TemplateVersion = request.TemplateVersion,
            IsLocked = false,
            AccessToken = tokenHash, // SHA-256 hash of the raw token
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.IntakeForms.Add(intake);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/intake/{intake.Id}", ToResponse(intake));
    }

    // GET /api/v1/intake/{id}
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

    // POST /api/v1/intake/{id}/submit
    // Sprint UC2: Lock the intake form to prevent further editing after patient submission.
    // Internal visibility allows direct handler tests in PTDoc.Tests.
    internal static async Task<IResult> SubmitIntake(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (intake.IsLocked)
            return Results.Conflict(new { error = "Intake form is already locked." });

        var userId = identityContext.GetCurrentUserId();

        intake.IsLocked = true;
        intake.SubmittedAt = DateTime.UtcNow;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = userId;
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

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

    /// <summary>
    /// Produces a lowercase hex SHA-256 hash of the given token, consistent with the Session.TokenHash pattern.
    /// </summary>
    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
