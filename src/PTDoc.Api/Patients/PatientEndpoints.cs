using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Patients;

/// <summary>
/// CRUD endpoints for patient records.
/// All endpoints are tenant-scoped (clinic_id from JWT claim).
/// Sprint O: TDD §6.1 Patient APIs
/// Sprint P: RBAC enforcement per FSD §3
/// </summary>
public static class PatientEndpoints
{
    public static void MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/patients")
            .WithTags("Patients");

        group.MapPost("/", CreatePatient)
            .WithName("CreatePatient")
            .WithSummary("Create a new patient")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);

        group.MapGet("/", ListPatients)
            .WithName("ListPatients")
            .WithSummary("List patients for clinician workflows")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapGet("/{id:guid}", GetPatient)
            .WithName("GetPatient")
            .WithSummary("Get a patient by ID")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapPut("/{id:guid}", UpdatePatient)
            .WithName("UpdatePatient")
            .WithSummary("Update an existing patient")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);

        group.MapGet("/{id:guid}/notes", GetPatientNotes)
            .WithName("GetPatientNotes")
            .WithSummary("Get clinical notes for a patient")
            .RequireAuthorization(AuthorizationPolicies.NoteRead);
    }

    // GET /api/patients
    private static async Task<IResult> ListPatients(
        [FromQuery] string? query,
        [FromQuery] int take,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedTake = take <= 0 ? 100 : Math.Min(take, 250);
        var normalizedQuery = query?.Trim();

        var patientQuery = db.Patients
            .AsNoTracking()
            .Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            patientQuery = patientQuery.Where(p =>
                (p.FirstName + " " + p.LastName).Contains(normalizedQuery) ||
                (p.MedicalRecordNumber != null && p.MedicalRecordNumber.Contains(normalizedQuery)) ||
                (p.Email != null && p.Email.Contains(normalizedQuery)));
        }

        var patients = await patientQuery
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Take(normalizedTake)
            .Select(p => new PatientListItemResponse
            {
                Id = p.Id,
                DisplayName = p.FirstName + " " + p.LastName,
                MedicalRecordNumber = p.MedicalRecordNumber
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(patients);
    }

    // POST /api/patients
    private static async Task<IResult> CreatePatient(
        [FromBody] CreatePatientRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        // Validate required fields explicitly.
        // Minimal API endpoints do not auto-validate DataAnnotations without an endpoint filter;
        // this ensures a proper 400 response with a consistent error shape.
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.FirstName), ["FirstName is required."] }
            });

        if (string.IsNullOrWhiteSpace(request.LastName))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.LastName), ["LastName is required."] }
            });

        if (request.DateOfBirth == default)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.DateOfBirth), ["DateOfBirth is required."] }
            });

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();

        var patient = new Patient
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            AddressLine1 = request.AddressLine1?.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            City = request.City?.Trim(),
            State = request.State?.Trim(),
            ZipCode = request.ZipCode?.Trim(),
            MedicalRecordNumber = request.MedicalRecordNumber?.Trim(),
            PayerInfoJson = request.PayerInfoJson ?? "{}",
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.Patients.Add(patient);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/patients/{patient.Id}", ToResponse(patient));
    }

    // GET /api/patients/{id}
    private static async Task<IResult> GetPatient(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        return Results.Ok(ToResponse(patient));
    }

    // PUT /api/patients/{id}
    private static async Task<IResult> UpdatePatient(
        Guid id,
        [FromBody] UpdatePatientRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        // Apply only provided (non-null) fields
        if (request.FirstName is not null)
        {
            var trimmedFirst = request.FirstName.Trim();
            if (trimmedFirst.Length == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(request.FirstName), ["FirstName cannot be empty or whitespace."] }
                });
            patient.FirstName = trimmedFirst;
        }

        if (request.LastName is not null)
        {
            var trimmedLast = request.LastName.Trim();
            if (trimmedLast.Length == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(request.LastName), ["LastName cannot be empty or whitespace."] }
                });
            patient.LastName = trimmedLast;
        }

        if (request.DateOfBirth is not null)
            patient.DateOfBirth = request.DateOfBirth.Value;

        if (request.Email is not null)
            patient.Email = request.Email.Trim();

        if (request.Phone is not null)
            patient.Phone = request.Phone.Trim();

        if (request.AddressLine1 is not null)
            patient.AddressLine1 = request.AddressLine1.Trim();

        if (request.AddressLine2 is not null)
            patient.AddressLine2 = request.AddressLine2.Trim();

        if (request.City is not null)
            patient.City = request.City.Trim();

        if (request.State is not null)
            patient.State = request.State.Trim();

        if (request.ZipCode is not null)
            patient.ZipCode = request.ZipCode.Trim();

        if (request.MedicalRecordNumber is not null)
            patient.MedicalRecordNumber = request.MedicalRecordNumber.Trim();

        if (request.PayerInfoJson is not null)
            patient.PayerInfoJson = request.PayerInfoJson;

        if (request.IsArchived is not null)
            patient.IsArchived = request.IsArchived.Value;

        patient.LastModifiedUtc = DateTime.UtcNow;
        patient.ModifiedByUserId = identityContext.GetCurrentUserId();
        patient.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(patient));
    }

    // GET /api/patients/{id}/notes
    private static async Task<IResult> GetPatientNotes(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var patientExists = await db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == id, cancellationToken);

        if (!patientExists)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        var notes = await db.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.ObjectiveMetrics)
            .Where(n => n.PatientId == id)
            .OrderByDescending(n => n.DateOfService)
            .ToListAsync(cancellationToken);

        return Results.Ok(notes.Select(NoteToResponse));
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static PatientResponse ToResponse(Patient p) => new()
    {
        Id = p.Id,
        FirstName = p.FirstName,
        LastName = p.LastName,
        DateOfBirth = p.DateOfBirth,
        Email = p.Email,
        Phone = p.Phone,
        AddressLine1 = p.AddressLine1,
        AddressLine2 = p.AddressLine2,
        City = p.City,
        State = p.State,
        ZipCode = p.ZipCode,
        MedicalRecordNumber = p.MedicalRecordNumber,
        PayerInfoJson = p.PayerInfoJson,
        IsArchived = p.IsArchived,
        ClinicId = p.ClinicId,
        LastModifiedUtc = p.LastModifiedUtc
    };

    private static NoteResponse NoteToResponse(ClinicalNote n) => new()
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
