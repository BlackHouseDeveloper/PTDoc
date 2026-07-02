using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Api.RequestParsing;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

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

        // Diagnosis code management
        group.MapGet("/{id:guid}/diagnoses", GetDiagnoses)
            .WithName("GetPatientDiagnoses")
            .WithSummary("Get diagnosis codes for a patient")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapPost("/{id:guid}/diagnoses", AddDiagnosis)
            .WithName("AddPatientDiagnosis")
            .WithSummary("Add an ICD-10 diagnosis code to a patient")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);

        group.MapDelete("/{id:guid}/diagnoses/{code}", RemoveDiagnosis)
            .WithName("RemovePatientDiagnosis")
            .WithSummary("Remove an ICD-10 diagnosis code from a patient")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);

        group.MapGet("/{id:guid}/documents", ListDocuments)
            .WithName("ListPatientDocuments")
            .WithSummary("List uploaded patient chart documents")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapPost("/{id:guid}/documents", UploadDocument)
            .WithName("UploadPatientDocument")
            .WithSummary("Upload a patient chart document")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);

        group.MapGet("/{id:guid}/documents/{documentId:guid}/content", GetDocumentContent)
            .WithName("GetPatientDocumentContent")
            .WithSummary("Download uploaded patient chart document content")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapGet("/{id:guid}/communications", ListCommunicationLogEntries)
            .WithName("ListPatientCommunicationLogEntries")
            .WithSummary("List patient chart communication log entries")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapPost("/{id:guid}/communications", CreateCommunicationLogEntry)
            .WithName("CreatePatientCommunicationLogEntry")
            .WithSummary("Add a patient chart communication log entry")
            .RequireAuthorization(AuthorizationPolicies.PatientWrite);
    }

    // GET /api/patients
    private static async Task<IResult> ListPatients(
        [FromQuery] string? query,
        [FromQuery] string? take,
        [FromServices] ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!ListQueryParameterParser.TryNormalizeTake(take, 100, 250, httpContext, out var normalizedTake, out var failure))
        {
            return failure!;
        }

        var normalizedQuery = query?.Trim();

        var patientQuery = db.Patients
            .AsNoTracking()
            .Where(p => !p.IsArchived);

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var likePattern = $"%{normalizedQuery}%";
            patientQuery = patientQuery.Where(p =>
                EF.Functions.Like(p.FirstName + " " + p.LastName, likePattern) ||
                (p.MedicalRecordNumber != null && EF.Functions.Like(p.MedicalRecordNumber, likePattern)) ||
                (p.Email != null && EF.Functions.Like(p.Email, likePattern)));
        }

        var patients = await patientQuery
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .Take(normalizedTake)
            .Select(p => new PatientListItemResponse
            {
                Id = p.Id,
                DisplayName = p.FirstName + " " + p.LastName,
                FirstName = p.FirstName,
                LastName = p.LastName,
                MedicalRecordNumber = p.MedicalRecordNumber,
                Email = p.Email,
                Phone = p.Phone,
                DateOfBirth = p.DateOfBirth,
                IsArchived = p.IsArchived
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
            ReferringPhysician = request.ReferringPhysician?.Trim(),
            PhysicianNpi = request.PhysicianNpi?.Trim(),
            DateOfOnset = request.DateOfOnset,
            AuthorizationNumber = request.AuthorizationNumber?.Trim(),
            EmergencyContactName = request.EmergencyContactName?.Trim(),
            EmergencyContactPhone = request.EmergencyContactPhone?.Trim(),
            ConsentSigned = request.ConsentSigned,
            ConsentSignedDate = request.ConsentSigned ? request.ConsentSignedDate ?? DateTime.UtcNow : null,
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

        if (request.ReferringPhysician is not null)
            patient.ReferringPhysician = request.ReferringPhysician.Trim();

        if (request.PhysicianNpi is not null)
            patient.PhysicianNpi = request.PhysicianNpi.Trim();

        if (request.DateOfOnset is not null)
            patient.DateOfOnset = request.DateOfOnset;

        if (request.AuthorizationNumber is not null)
            patient.AuthorizationNumber = request.AuthorizationNumber.Trim();

        if (request.EmergencyContactName is not null)
            patient.EmergencyContactName = request.EmergencyContactName.Trim();

        if (request.EmergencyContactPhone is not null)
            patient.EmergencyContactPhone = request.EmergencyContactPhone.Trim();

        if (request.ConsentSigned is not null)
        {
            patient.ConsentSigned = request.ConsentSigned.Value;
            if (request.ConsentSigned.Value)
            {
                // Consent signed — set the date if provided, otherwise default to now if not already set
                if (request.ConsentSignedDate is not null)
                    patient.ConsentSignedDate = request.ConsentSignedDate;
                else if (patient.ConsentSignedDate is null)
                    patient.ConsentSignedDate = DateTime.UtcNow;
            }
            else
            {
                // Consent revoked — clear the signature date
                patient.ConsentSignedDate = null;
            }
        }
        else if (request.ConsentSignedDate is not null && patient.ConsentSigned)
        {
            // Date update only allowed when consent is already signed
            patient.ConsentSignedDate = request.ConsentSignedDate;
        }

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
            .Where(n => n.PatientId == id && !n.IsAddendum)
            .OrderByDescending(n => n.DateOfService)
            .ToListAsync(cancellationToken);

        return Results.Ok(notes.Select(NoteToResponse));
    }

    // GET /api/patients/{id}/diagnoses
    private static async Task<IResult> GetDiagnoses(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        IReadOnlyList<PatientDiagnosisDto> diagnoses;
        try
        {
            diagnoses = DeserializeDiagnoses(patient.DiagnosisCodesJson);
        }
        catch (JsonException)
        {
            return Results.Problem("Stored diagnosis data is corrupted and cannot be read.", statusCode: 500);
        }

        return Results.Ok(diagnoses);
    }

    // POST /api/patients/{id}/diagnoses
    private static async Task<IResult> AddDiagnosis(
        Guid id,
        [FromBody] AddDiagnosisRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        if (string.IsNullOrWhiteSpace(request.IcdCode))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.IcdCode), ["IcdCode is required."] }
            });

        IReadOnlyList<PatientDiagnosisDto> diagnoses;
        try
        {
            diagnoses = DeserializeDiagnoses(patient.DiagnosisCodesJson);
        }
        catch (JsonException)
        {
            return Results.Problem("Stored diagnosis data is corrupted and cannot be read.", statusCode: 500);
        }

        // Avoid duplicates
        if (diagnoses.Any(d => string.Equals(d.IcdCode, request.IcdCode.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new { error = $"Diagnosis code {request.IcdCode} is already present." });

        var newList = diagnoses.ToList();

        // If this is primary, clear other primary flags
        if (request.IsPrimary)
        {
            for (int i = 0; i < newList.Count; i++)
            {
                newList[i] = new PatientDiagnosisDto
                {
                    IcdCode = newList[i].IcdCode,
                    Description = newList[i].Description,
                    IsPrimary = false
                };
            }
        }

        newList.Add(new PatientDiagnosisDto
        {
            IcdCode = request.IcdCode.Trim().ToUpperInvariant(),
            Description = request.Description?.Trim() ?? string.Empty,
            IsPrimary = request.IsPrimary
        });

        patient.DiagnosisCodesJson = JsonSerializer.Serialize(newList);
        patient.LastModifiedUtc = DateTime.UtcNow;
        patient.ModifiedByUserId = identityContext.GetCurrentUserId();
        patient.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(newList);
    }

    // DELETE /api/patients/{id}/diagnoses/{code}
    private static async Task<IResult> RemoveDiagnosis(
        Guid id,
        string code,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        IReadOnlyList<PatientDiagnosisDto> diagnoses;
        try
        {
            diagnoses = DeserializeDiagnoses(patient.DiagnosisCodesJson);
        }
        catch (JsonException)
        {
            return Results.Problem("Stored diagnosis data is corrupted and cannot be read.", statusCode: 500);
        }

        var updated = diagnoses.Where(d => !string.Equals(d.IcdCode, code, StringComparison.OrdinalIgnoreCase)).ToList();

        if (updated.Count == diagnoses.Count)
            return Results.NotFound(new { error = $"Diagnosis code {code} not found on patient." });

        patient.DiagnosisCodesJson = JsonSerializer.Serialize(updated);
        patient.LastModifiedUtc = DateTime.UtcNow;
        patient.ModifiedByUserId = identityContext.GetCurrentUserId();
        patient.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private const int MaxPatientDocumentBytes = 10 * 1024 * 1024;
    private const int MaxDocumentTypeLength = 80;
    private const int MaxDocumentFileNameLength = 255;
    private const int MaxDocumentContentTypeLength = 120;
    private const int MaxDocumentNotesLength = 1000;
    private const int MaxCommunicationChannelLength = 40;
    private const int MaxCommunicationDirectionLength = 40;
    private const int MaxCommunicationSummaryLength = 200;
    private const int MaxCommunicationDetailsLength = 2000;
    private const int MaxCommunicationContactNameLength = 120;

    private static async Task<IResult> ListDocuments(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(patient => patient.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var documents = await db.PatientDocuments
            .AsNoTracking()
            .Where(document => document.PatientId == id)
            .OrderByDescending(document => document.UploadedAtUtc)
            .Select(document => new PatientDocumentResponse
            {
                Id = document.Id,
                PatientId = document.PatientId,
                DocumentType = document.DocumentType,
                FileName = document.FileName,
                ContentType = document.ContentType,
                SizeBytes = document.SizeBytes,
                Notes = document.Notes,
                UploadedByUserId = document.UploadedByUserId,
                UploadedAtUtc = document.UploadedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(documents);
    }

    private static async Task<IResult> UploadDocument(
        Guid id,
        [FromBody] UploadPatientDocumentRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (patient is null)
        {
            return Results.NotFound();
        }

        var validationErrors = ValidateDocumentUpload(request, out var contentBytes);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var now = DateTime.UtcNow;
        var userId = identityContext.GetCurrentUserId();
        var document = new PatientDocument
        {
            PatientId = id,
            ClinicId = patient.ClinicId,
            DocumentType = request.DocumentType.Trim(),
            FileName = SanitizeFileName(request.FileName),
            ContentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType.Trim(),
            SizeBytes = contentBytes.LongLength,
            ContentHashSha256 = Convert.ToHexString(SHA256.HashData(contentBytes)).ToLowerInvariant(),
            ContentBytes = contentBytes,
            Notes = TrimOrNull(request.Notes),
            UploadedByUserId = userId,
            UploadedAtUtc = now
        };

        db.PatientDocuments.Add(document);
        patient.LastModifiedUtc = now;
        patient.ModifiedByUserId = userId;
        patient.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/patients/{id:D}/documents/{document.Id:D}", ToPatientDocumentResponse(document));
    }

    private static async Task<IResult> GetDocumentContent(
        Guid id,
        Guid documentId,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var document = await db.PatientDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.PatientId == id && item.Id == documentId, cancellationToken);

        if (document is null)
        {
            return Results.NotFound();
        }

        return Results.File(document.ContentBytes, document.ContentType, document.FileName);
    }

    private static async Task<IResult> ListCommunicationLogEntries(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(patient => patient.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var entries = await db.PatientCommunicationLogEntries
            .AsNoTracking()
            .Where(entry => entry.PatientId == id)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.CreatedAtUtc)
            .Select(entry => new PatientCommunicationLogEntryResponse
            {
                Id = entry.Id,
                PatientId = entry.PatientId,
                Channel = entry.Channel,
                Direction = entry.Direction,
                Summary = entry.Summary,
                Details = entry.Details,
                ContactName = entry.ContactName,
                OccurredAtUtc = entry.OccurredAtUtc,
                CreatedAtUtc = entry.CreatedAtUtc,
                CreatedByUserId = entry.CreatedByUserId
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(entries);
    }

    private static async Task<IResult> CreateCommunicationLogEntry(
        Guid id,
        [FromBody] CreatePatientCommunicationLogEntryRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (patient is null)
        {
            return Results.NotFound();
        }

        var validationErrors = ValidateCommunicationLogEntry(request);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var now = DateTime.UtcNow;
        var userId = identityContext.GetCurrentUserId();
        var entry = new PatientCommunicationLogEntry
        {
            PatientId = id,
            ClinicId = patient.ClinicId,
            Channel = request.Channel.Trim(),
            Direction = request.Direction.Trim(),
            Summary = request.Summary.Trim(),
            Details = TrimOrNull(request.Details),
            ContactName = TrimOrNull(request.ContactName),
            OccurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? now,
            CreatedAtUtc = now,
            CreatedByUserId = userId
        };

        db.PatientCommunicationLogEntries.Add(entry);
        patient.LastModifiedUtc = now;
        patient.ModifiedByUserId = userId;
        patient.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/patients/{id:D}/communications/{entry.Id:D}", ToPatientCommunicationLogEntryResponse(entry));
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    private static IReadOnlyList<PatientDiagnosisDto> DeserializeDiagnoses(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<PatientDiagnosisDto>();
        }

        // Allow JsonException to propagate — callers must handle and return an error response
        // rather than silently treating corrupted diagnosis data as an empty list.
        return JsonSerializer.Deserialize<List<PatientDiagnosisDto>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<PatientDiagnosisDto>();
    }

    private static Dictionary<string, string[]> ValidateDocumentUpload(
        UploadPatientDocumentRequest request,
        out byte[] contentBytes)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        contentBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(request.DocumentType))
        {
            errors[nameof(request.DocumentType)] = ["DocumentType is required."];
        }
        else
        {
            AddMaxLengthError(errors, nameof(request.DocumentType), request.DocumentType.Trim(), MaxDocumentTypeLength);
        }

        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            errors[nameof(request.FileName)] = ["FileName is required."];
        }
        else
        {
            var sanitizedFileName = SanitizeFileName(request.FileName);
            if (string.IsNullOrWhiteSpace(sanitizedFileName))
            {
                errors[nameof(request.FileName)] = ["FileName must include a file name."];
            }
            else
            {
                AddMaxLengthError(errors, nameof(request.FileName), sanitizedFileName, MaxDocumentFileNameLength);
            }
        }

        AddMaxLengthError(errors, nameof(request.ContentType), TrimOrNull(request.ContentType), MaxDocumentContentTypeLength);
        AddMaxLengthError(errors, nameof(request.Notes), TrimOrNull(request.Notes), MaxDocumentNotesLength);

        if (string.IsNullOrWhiteSpace(request.Base64Content))
        {
            errors[nameof(request.Base64Content)] = ["Base64Content is required."];
            return errors;
        }

        if (WouldDecodedBase64ExceedLimit(request.Base64Content, MaxPatientDocumentBytes))
        {
            errors[nameof(request.Base64Content)] = [$"Uploaded document cannot exceed {MaxPatientDocumentBytes / 1024 / 1024} MB."];
            return errors;
        }

        try
        {
            contentBytes = Convert.FromBase64String(request.Base64Content);
        }
        catch (FormatException)
        {
            errors[nameof(request.Base64Content)] = ["Base64Content must be valid base64."];
            return errors;
        }

        if (contentBytes.Length == 0)
        {
            errors[nameof(request.Base64Content)] = ["Uploaded document cannot be empty."];
        }
        else if (contentBytes.Length > MaxPatientDocumentBytes)
        {
            errors[nameof(request.Base64Content)] = [$"Uploaded document cannot exceed {MaxPatientDocumentBytes / 1024 / 1024} MB."];
        }

        return errors;
    }

    private static bool WouldDecodedBase64ExceedLimit(string base64Content, int maxDecodedBytes)
    {
        var encodedLength = 0;
        foreach (var character in base64Content)
        {
            if (!char.IsWhiteSpace(character))
            {
                encodedLength++;
            }
        }

        var maxEncodedLength = ((maxDecodedBytes + 2) / 3) * 4;
        if (encodedLength > maxEncodedLength)
        {
            return true;
        }

        if (encodedLength == 0 || encodedLength % 4 != 0)
        {
            return false;
        }

        var padding = 0;
        for (var i = base64Content.Length - 1; i >= 0 && padding < 2; i--)
        {
            var character = base64Content[i];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (character == '=')
            {
                padding++;
                continue;
            }

            break;
        }

        var maxPossibleDecodedLength = (encodedLength / 4 * 3) - padding;
        return maxPossibleDecodedLength > maxDecodedBytes;
    }

    private static Dictionary<string, string[]> ValidateCommunicationLogEntry(
        CreatePatientCommunicationLogEntryRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(request.Channel))
        {
            errors[nameof(request.Channel)] = ["Channel is required."];
        }
        else
        {
            AddMaxLengthError(errors, nameof(request.Channel), request.Channel.Trim(), MaxCommunicationChannelLength);
        }

        if (string.IsNullOrWhiteSpace(request.Direction))
        {
            errors[nameof(request.Direction)] = ["Direction is required."];
        }
        else
        {
            AddMaxLengthError(errors, nameof(request.Direction), request.Direction.Trim(), MaxCommunicationDirectionLength);
        }

        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            errors[nameof(request.Summary)] = ["Summary is required."];
        }
        else
        {
            AddMaxLengthError(errors, nameof(request.Summary), request.Summary.Trim(), MaxCommunicationSummaryLength);
        }

        AddMaxLengthError(errors, nameof(request.Details), TrimOrNull(request.Details), MaxCommunicationDetailsLength);
        AddMaxLengthError(errors, nameof(request.ContactName), TrimOrNull(request.ContactName), MaxCommunicationContactNameLength);

        return errors;
    }

    private static string SanitizeFileName(string? fileName)
    {
        var baseName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        var sanitized = new StringBuilder(baseName.Length);
        foreach (var character in baseName)
        {
            if (!char.IsControl(character))
            {
                sanitized.Append(character);
            }
        }

        return sanitized.ToString().Trim();
    }

    private static void AddMaxLengthError(
        Dictionary<string, string[]> errors,
        string fieldName,
        string? value,
        int maxLength)
    {
        if (value is not null && value.Length > maxLength)
        {
            errors[fieldName] = [$"{fieldName} cannot exceed {maxLength} characters."];
        }
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
        ReferringPhysician = p.ReferringPhysician,
        PhysicianNpi = p.PhysicianNpi,
        DateOfOnset = p.DateOfOnset,
        AuthorizationNumber = p.AuthorizationNumber,
        EmergencyContactName = p.EmergencyContactName,
        EmergencyContactPhone = p.EmergencyContactPhone,
        ConsentSigned = p.ConsentSigned,
        ConsentSignedDate = p.ConsentSignedDate,
        DiagnosisCodesJson = p.DiagnosisCodesJson,
        IsArchived = p.IsArchived,
        ClinicId = p.ClinicId,
        LastModifiedUtc = p.LastModifiedUtc
    };

    private static PatientDocumentResponse ToPatientDocumentResponse(PatientDocument document) => new()
    {
        Id = document.Id,
        PatientId = document.PatientId,
        DocumentType = document.DocumentType,
        FileName = document.FileName,
        ContentType = document.ContentType,
        SizeBytes = document.SizeBytes,
        Notes = document.Notes,
        UploadedByUserId = document.UploadedByUserId,
        UploadedAtUtc = document.UploadedAtUtc
    };

    private static PatientCommunicationLogEntryResponse ToPatientCommunicationLogEntryResponse(
        PatientCommunicationLogEntry entry) => new()
        {
            Id = entry.Id,
            PatientId = entry.PatientId,
            Channel = entry.Channel,
            Direction = entry.Direction,
            Summary = entry.Summary,
            Details = entry.Details,
            ContactName = entry.ContactName,
            OccurredAtUtc = entry.OccurredAtUtc,
            CreatedAtUtc = entry.CreatedAtUtc,
            CreatedByUserId = entry.CreatedByUserId
        };

    private static NoteResponse NoteToResponse(ClinicalNote n) => new()
    {
        Id = n.Id,
        PatientId = n.PatientId,
        AppointmentId = n.AppointmentId,
        ParentNoteId = n.ParentNoteId,
        IsAddendum = n.IsAddendum,
        NoteType = n.NoteType,
        IsReEvaluation = n.IsReEvaluation,
        NoteStatus = n.NoteStatus,
        ContentJson = NoteWriteService.NormalizeContentJson(
            n.NoteType,
            n.IsReEvaluation,
            n.DateOfService,
            n.ContentJson),
        DateOfService = n.DateOfService,
        SignatureHash = n.SignatureHash,
        SignedUtc = n.SignedUtc,
        SignedByUserId = n.SignedByUserId,
        CptCodesJson = n.CptCodesJson,
        TherapistNpi = n.TherapistNpi,
        TotalTreatmentMinutes = n.TotalTreatmentMinutes,
        ClinicId = n.ClinicId,
        CreatedUtc = n.CreatedUtc,
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
}

/// <summary>Request body for adding a diagnosis code to a patient.</summary>
public sealed record AddDiagnosisRequest(string IcdCode, string? Description, bool IsPrimary);
