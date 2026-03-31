using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

        group.MapGet("/patient/{patientId:guid}/draft", GetDraftByPatient)
            .WithName("GetPatientDraftIntake")
            .WithSummary("Get most recent intake draft for a patient")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapPut("/{id:guid}", UpdateIntake)
            .WithName("UpdateIntake")
            .WithSummary("Update an existing intake draft")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapPost("/{id:guid}/submit", SubmitIntake)
            .WithName("SubmitIntake")
            .WithSummary("Submit and lock an intake response")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapPost("/{id:guid}/lock", LockIntake)
            .WithName("LockIntake")
            .WithSummary("Lock an intake response")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapPost("/{id:guid}/review", ReviewIntake)
            .WithName("ReviewIntake")
            .WithSummary("Record a clinician review event for a submitted intake response")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        group.MapPost("/{id:guid}/consents/revoke", RevokeIntakeConsents)
            .WithName("RevokeIntakeConsents")
            .WithSummary("Apply one or more written consent revocations to an intake response")
            .RequireAuthorization(AuthorizationPolicies.IntakeWrite);

        group.MapGet("/{id:guid}/consents/revocations", GetIntakeConsentRevocations)
            .WithName("GetIntakeConsentRevocations")
            .WithSummary("Get intake consent revocation state and recent revocation audit entries")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/consents/revocations/timeline", GetIntakeConsentRevocationTimeline)
            .WithName("GetIntakeConsentRevocationTimeline")
            .WithSummary("Get paged intake consent revocation audit timeline")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/communications/eligibility", GetIntakeCommunicationConsentEligibility)
            .WithName("GetIntakeCommunicationConsentEligibility")
            .WithSummary("Get current communication channel eligibility derived from intake consents")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/specialty-consents/eligibility", GetIntakeSpecialtyConsentEligibility)
            .WithName("GetIntakeSpecialtyConsentEligibility")
            .WithSummary("Get current specialty treatment eligibility derived from intake consents")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/phi-release/eligibility", GetIntakePhiReleaseEligibility)
            .WithName("GetIntakePhiReleaseEligibility")
            .WithSummary("Get PHI release authorization eligibility derived from intake consents")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/credit-card-authorization/eligibility", GetIntakeCreditCardAuthorizationEligibility)
            .WithName("GetIntakeCreditCardAuthorizationEligibility")
            .WithSummary("Get credit card authorization eligibility derived from intake consents")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/{id:guid}/consents/completeness", GetIntakeConsentCompleteness)
            .WithName("GetIntakeConsentCompleteness")
            .WithSummary("Evaluate whether all required consents are present and not revoked")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);
    }

    // POST /api/v1/intake
    private static async Task<IResult> CreateIntake(
        [FromBody] CreateIntakeRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IIntakeReferenceDataCatalogService intakeReferenceData,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        if (request.PatientId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.PatientId), ["PatientId is required."] }
            });

        if (!TryResolveStructuredData(
                request.StructuredData,
                request.PainMapData,
                existingStructuredDataJson: null,
                intakeReferenceData,
                out var painMapData,
                out var structuredDataJson,
                out var createStructuredDataProblem))
        {
            return createStructuredDataProblem!;
        }

        if (!TryNormalizeConsents(request.Consents, requireHipaaAcknowledgement: false, out var consents, out _, out var createValidationProblem))
            return createValidationProblem!;

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
            StructuredDataJson = structuredDataJson,
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
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Create, cancellationToken);

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

    // GET /api/v1/intake/patient/{patientId}/draft
    private static async Task<IResult> GetDraftByPatient(
        Guid patientId,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .Where(f => f.PatientId == patientId && !f.IsLocked)
            .OrderByDescending(f => f.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"No intake draft found for patient {patientId}." });

        return Results.Ok(ToResponse(intake));
    }

    // PUT /api/v1/intake/{id}
    private static async Task<IResult> UpdateIntake(
        Guid id,
        [FromBody] UpdateIntakeRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IIntakeReferenceDataCatalogService intakeReferenceData,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (intake.IsLocked)
            return Results.Conflict(new { error = "Intake is locked and cannot be modified." });

        if (!TryResolveStructuredData(
                request.StructuredData,
                request.PainMapData,
                intake.StructuredDataJson,
                intakeReferenceData,
                out var painMapData,
                out var structuredDataJson,
                out var updateStructuredDataProblem))
        {
            return updateStructuredDataProblem!;
        }

        intake.PainMapData = painMapData;

        if (!TryNormalizeConsents(request.Consents, requireHipaaAcknowledgement: false, out var normalizedConsents, out _, out var updateValidationProblem))
            return updateValidationProblem!;

        intake.Consents = normalizedConsents;
        intake.ResponseJson = string.IsNullOrWhiteSpace(request.ResponseJson) ? "{}" : request.ResponseJson;
        if (request.StructuredData is not null)
        {
            intake.StructuredDataJson = structuredDataJson;
        }

        var templateVersion = string.IsNullOrWhiteSpace(request.TemplateVersion) ? "1.0" : request.TemplateVersion;
        if (templateVersion.Length > 50)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.TemplateVersion), ["TemplateVersion must not exceed 50 characters."] }
            });

        intake.TemplateVersion = templateVersion;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = identityContext.GetCurrentUserId();
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);
        return Results.Ok(ToResponse(intake));
    }

    // POST /api/v1/intake/{id}/submit
    internal static async Task<IResult> SubmitIntake(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        [FromServices] IPatientContextAccessor patientContext,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        // When the caller is a Patient, verify they own this intake form.
        // Staff roles (PT, PTA, Admin, FrontDesk) may submit on behalf of any patient.
        if (string.Equals(identityContext.GetCurrentUserRole(), Roles.Patient, StringComparison.Ordinal))
        {
            var callerPatientId = patientContext.GetCurrentPatientId();
            if (callerPatientId is null || callerPatientId.Value != intake.PatientId)
                return Results.Forbid();
        }

        if (intake.IsLocked)
            return Results.Conflict(new { error = "Intake is already locked." });

        if (!TryNormalizeConsents(intake.Consents, requireHipaaAcknowledgement: true, out var normalizedConsents, out var consentPacket, out var submitValidationProblem))
            return submitValidationProblem!;

        var nowUtc = DateTime.UtcNow;
        intake.Consents = normalizedConsents;
        intake.SubmittedAt = intake.SubmittedAt ?? nowUtc;
        intake.IsLocked = true;
        intake.LastModifiedUtc = nowUtc;
        intake.ModifiedByUserId = identityContext.GetCurrentUserId();
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);

        await auditService.LogIntakeEventAsync(
            AuditEvent.IntakeSubmitted(
                intake.Id,
                intake.ModifiedByUserId,
                IntakeConsentJson.CreateAuditSummary(consentPacket)),
            cancellationToken);

        return Results.Ok(ToResponse(intake));
    }

    // POST /api/v1/intake/{id}/lock
    private static async Task<IResult> LockIntake(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (intake.IsLocked)
            return Results.Ok(ToResponse(intake));

        intake.IsLocked = true;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = identityContext.GetCurrentUserId();
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);

        await auditService.LogIntakeEventAsync(
            AuditEvent.IntakeLocked(intake.Id, intake.ModifiedByUserId), cancellationToken);

        return Results.Ok(ToResponse(intake));
    }

    // POST /api/v1/intake/{id}/review
    internal static async Task<IResult> ReviewIntake(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        var isProperlySubmitted = intake.IsLocked && intake.SubmittedAt is not null;
        if (!isProperlySubmitted)
            return Results.Conflict(new { error = "Intake must be submitted and locked before it can be reviewed." });

        var reviewerId = identityContext.GetCurrentUserId();

        await auditService.LogIntakeEventAsync(
            AuditEvent.IntakeReviewed(intake.Id, reviewerId), cancellationToken);

        return Results.Ok(ToResponse(intake));
    }

    // POST /api/v1/intake/{id}/consents/revoke
    internal static async Task<IResult> RevokeIntakeConsents(
        Guid id,
        [FromBody] RevokeIntakeConsentRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        if (request.ConsentKeys is null || request.ConsentKeys.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ConsentKeys), ["At least one consent key is required for revocation."] }
            });
        }

        if (!request.WrittenRevocationReceived)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.WrittenRevocationReceived), ["Written revocation confirmation is required before applying consent revocations."] }
            });
        }

        var intake = await db.IntakeForms
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var nowUtc = DateTime.UtcNow;
        var revocationValidation = IntakeConsentJson.ApplyWrittenRevocation(consentPacket, request.ConsentKeys, nowUtc);
        if (!revocationValidation.IsValid)
            return Results.ValidationProblem(revocationValidation.Errors);

        intake.Consents = IntakeConsentJson.Serialize(consentPacket);
        intake.LastModifiedUtc = nowUtc;
        intake.ModifiedByUserId = identityContext.GetCurrentUserId();
        intake.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("IntakeForm", intake.Id, SyncOperation.Update, cancellationToken);

        var normalizedKeys = IntakeConsentJson.NormalizeConsentKeys(request.ConsentKeys);
        await auditService.LogIntakeEventAsync(
            AuditEvent.IntakeConsentRevoked(
                intake.Id,
                intake.ModifiedByUserId,
                normalizedKeys,
                !string.IsNullOrWhiteSpace(request.WrittenRequestReference)),
            cancellationToken);

        return Results.Ok(ToResponse(intake));
    }

    // GET /api/v1/intake/{id}/consents/revocations
    internal static async Task<IResult> GetIntakeConsentRevocations(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var auditEntries = await db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.EventType == "IntakeConsentRevoked" &&
                a.EntityType == "IntakeForm" &&
                a.EntityId == id)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        var response = new IntakeConsentRevocationHistoryResponse
        {
            IntakeId = intake.Id,
            WrittenRevocationReceived = consentPacket.WrittenRevocationReceived == true,
            LastRevocationAtUtc = consentPacket.LastRevocationAtUtc,
            RevokedConsentKeys = consentPacket.RevokedConsentKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            AuditEntries = auditEntries.Select(MapRevocationAuditEntry).ToList()
        };

        return Results.Ok(response);
    }

    // GET /api/v1/intake/{id}/consents/revocations/timeline
    internal static async Task<IResult> GetIntakeConsentRevocationTimeline(
        Guid id,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        [FromQuery] string? correlationId,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        page = page <= 0 ? 1 : page;
        pageSize = pageSize <= 0 ? 25 : pageSize;
        if (pageSize > 200)
            pageSize = 200;

        var intakeExists = await db.IntakeForms
            .AsNoTracking()
            .AnyAsync(f => f.Id == id, cancellationToken);

        if (!intakeExists)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        var query = db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.EventType == "IntakeConsentRevoked" &&
                a.EntityType == "IntakeForm" &&
                a.EntityId == id);

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var normalizedCorrelationId = correlationId.Trim();
            query = query.Where(a => a.CorrelationId == normalizedCorrelationId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var response = new IntakeConsentRevocationTimelineResponse
        {
            IntakeId = id,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            Entries = entries.Select(MapRevocationAuditEntry).ToList()
        };

        return Results.Ok(response);
    }

    // GET /api/v1/intake/{id}/communications/eligibility
    internal static async Task<IResult> GetIntakeCommunicationConsentEligibility(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var callAllowed = IntakeConsentJson.IsCallConsentActive(consentPacket);
        var textAllowed = IntakeConsentJson.IsTextConsentActive(consentPacket);
        var emailAllowed = IntakeConsentJson.IsEmailConsentActive(consentPacket);

        var response = new IntakeCommunicationConsentEligibilityResponse
        {
            IntakeId = intake.Id,
            CallAllowed = callAllowed,
            TextAllowed = textAllowed,
            EmailAllowed = emailAllowed,
            AnyChannelAllowed = callAllowed || textAllowed || emailAllowed,
            CommunicationPhoneNumber = string.IsNullOrWhiteSpace(consentPacket.CommunicationPhoneNumber)
                ? null
                : consentPacket.CommunicationPhoneNumber,
            CommunicationEmail = string.IsNullOrWhiteSpace(consentPacket.CommunicationEmail)
                ? null
                : consentPacket.CommunicationEmail,
            LastRevocationAtUtc = consentPacket.LastRevocationAtUtc,
            RevokedConsentKeys = consentPacket.RevokedConsentKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return Results.Ok(response);
    }

    // GET /api/v1/intake/{id}/specialty-consents/eligibility
    internal static async Task<IResult> GetIntakeSpecialtyConsentEligibility(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var dryNeedlingAllowed = IntakeConsentJson.IsDryNeedlingConsentActive(consentPacket);
        var pelvicFloorAllowed = IntakeConsentJson.IsPelvicFloorConsentActive(consentPacket);

        var response = new IntakeSpecialtyConsentEligibilityResponse
        {
            IntakeId = intake.Id,
            DryNeedlingAllowed = dryNeedlingAllowed,
            PelvicFloorAllowed = pelvicFloorAllowed,
            AnySpecialtyAllowed = dryNeedlingAllowed || pelvicFloorAllowed,
            LastRevocationAtUtc = consentPacket.LastRevocationAtUtc,
            RevokedConsentKeys = consentPacket.RevokedConsentKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return Results.Ok(response);
    }

    // ─── Mapping helpers ──────────────────────────────────────────────────────

    internal static IntakeResponse ToResponse(IntakeForm f) => new()
    {
        Id = f.Id,
        PatientId = f.PatientId,
        PainMapData = f.PainMapData,
        Consents = f.Consents,
        ResponseJson = f.ResponseJson,
        StructuredData = TryParseStructuredData(f.StructuredDataJson),
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

    internal static bool TryNormalizeConsents(
        string? consentsJson,
        bool requireHipaaAcknowledgement,
        out string normalizedConsents,
        out IntakeConsentPacket consentPacket,
        out IResult? validationProblem)
    {
        if (!IntakeConsentJson.TryParse(consentsJson, out consentPacket, out var parseError))
        {
            normalizedConsents = "{}";
            validationProblem = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
            return false;
        }

        var validation = IntakeConsentJson.Validate(consentPacket, requireHipaaAcknowledgement);
        if (!validation.IsValid)
        {
            normalizedConsents = IntakeConsentJson.Serialize(consentPacket);
            validationProblem = Results.ValidationProblem(validation.Errors);
            return false;
        }

        normalizedConsents = IntakeConsentJson.Serialize(consentPacket);
        validationProblem = null;
        return true;
    }

    internal static IntakeStructuredDataDto? TryParseStructuredData(string? structuredDataJson)
    {
        if (!IntakeStructuredDataJson.TryParse(structuredDataJson, out var structuredData, out _))
        {
            return null;
        }

        var hasContent = (!string.IsNullOrWhiteSpace(structuredDataJson)
                && !string.Equals(structuredDataJson.Trim(), "{}", StringComparison.Ordinal))
            || structuredData.BodyPartSelections.Count > 0
            || structuredData.MedicationIds.Count > 0
            || structuredData.PainDescriptorIds.Count > 0;

        return hasContent ? structuredData : null;
    }

    internal static bool TryResolveStructuredData(
        IntakeStructuredDataDto? requestStructuredData,
        string? legacyPainMapData,
        string? existingStructuredDataJson,
        IIntakeReferenceDataCatalogService intakeReferenceData,
        out string painMapData,
        out string? structuredDataJson,
        out IResult? validationProblem)
    {
        validationProblem = null;

        if (requestStructuredData is not null)
        {
            if (!IntakeStructuredDataJson.TryNormalize(
                    requestStructuredData,
                    intakeReferenceData,
                    out var normalizationResult,
                    out var structuredDataValidation))
            {
                painMapData = "{}";
                structuredDataJson = existingStructuredDataJson;
                validationProblem = Results.ValidationProblem(structuredDataValidation.Errors);
                return false;
            }

            painMapData = normalizationResult.PainMapDataJson;
            structuredDataJson = normalizationResult.StructuredDataJson;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(existingStructuredDataJson) &&
            IntakeStructuredDataJson.TryParse(existingStructuredDataJson, out var existingStructuredData, out _))
        {
            painMapData = IntakeStructuredDataJson.BuildPainMapProjectionJson(existingStructuredData, intakeReferenceData);
            structuredDataJson = existingStructuredDataJson;
            return true;
        }

        painMapData = string.IsNullOrWhiteSpace(legacyPainMapData) ? "{}" : legacyPainMapData;
        structuredDataJson = existingStructuredDataJson;
        return true;
    }

    private static List<string> ReadConsentKeys(string metadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("ConsentKeys", out var keysElement) || keysElement.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return keysElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static bool TryReadHasWrittenReference(string metadataJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            return doc.RootElement.TryGetProperty("HasWrittenReference", out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IntakeConsentRevocationAuditEntryResponse MapRevocationAuditEntry(AuditLog entry)
    {
        return new IntakeConsentRevocationAuditEntryResponse
        {
            TimestampUtc = entry.TimestampUtc,
            UserId = entry.UserId,
            CorrelationId = entry.CorrelationId,
            HasWrittenReference = TryReadHasWrittenReference(entry.MetadataJson),
            ConsentKeys = ReadConsentKeys(entry.MetadataJson)
        };
    }

    internal static async Task<IResult> GetIntakePhiReleaseEligibility(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var phiReleaseAllowed = IntakeConsentJson.IsPhiReleaseConsentActive(consentPacket);

        var response = new IntakePhiReleaseEligibilityResponse
        {
            IntakeId = intake.Id,
            PhiReleaseAllowed = phiReleaseAllowed,
            LastRevocationAtUtc = consentPacket.LastRevocationAtUtc,
            RevokedConsentKeys = consentPacket.RevokedConsentKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetIntakeCreditCardAuthorizationEligibility(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var creditCardAuthorizationAllowed = IntakeConsentJson.IsCreditCardAuthorizationConsentActive(consentPacket);

        var response = new IntakeCreditCardAuthorizationEligibilityResponse
        {
            IntakeId = intake.Id,
            CreditCardAuthorizationAllowed = creditCardAuthorizationAllowed,
            LastRevocationAtUtc = consentPacket.LastRevocationAtUtc,
            RevokedConsentKeys = consentPacket.RevokedConsentKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetIntakeConsentCompleteness(
        Guid id,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var intake = await db.IntakeForms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (intake is null)
            return Results.NotFound(new { error = $"Intake {id} not found." });

        if (!IntakeConsentJson.TryParse(intake.Consents, out var consentPacket, out var parseError))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(IntakeForm.Consents), [parseError ?? "Consents data is not valid JSON."] }
            });
        }

        var (isComplete, missing, revoked) = IntakeConsentJson.EvaluateConsentCompleteness(consentPacket);

        // Build per-consent item list for the two required keys
        var requiredKeys = new[]
        {
            ("hipaaAcknowledged", consentPacket.HipaaAcknowledged == true),
            ("treatmentConsentAccepted", consentPacket.TreatmentConsentAccepted == true)
        };

        var items = requiredKeys.Select(entry => new IntakeConsentCompletenessItemResponse
        {
            ConsentKey = entry.Item1,
            Accepted = entry.Item2,
            Revoked = revoked.Contains(entry.Item1, StringComparer.OrdinalIgnoreCase)
        }).ToList();

        var response = new IntakeConsentCompletenessResponse
        {
            IntakeId = intake.Id,
            IsComplete = isComplete,
            MissingConsentKeys = missing,
            RevokedConsentKeys = revoked,
            Items = items
        };

        return Results.Ok(response);
    }
}
