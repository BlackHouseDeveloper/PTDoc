using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Appointments;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace PTDoc.Api.Appointments;

/// <summary>
/// Endpoints for appointment scheduling workflows.
/// </summary>
public static class AppointmentEndpoints
{
    private const string AppointmentOverbookingErrorCode = "APPOINTMENT_OVERBOOKING";
    private const int PaymentTransactionIdMaxLength = 120;
    private const int PaymentAuthorizationCodeMaxLength = 80;
    private const int PaymentGatewayErrorCodeMaxLength = 80;
    private const int PaymentGatewayErrorMessageMaxLength = 500;
    private static readonly ConcurrentDictionary<Guid, AppointmentPaymentLockState> AppointmentPaymentLocks = new();
    private static readonly CultureInfo CopayCurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] SchedulableClinicianRoles =
    [
        Roles.PT,
        Roles.PTA,
        Roles.Admin,
        Roles.Owner,
        Roles.PracticeManager,
        "Physical Therapist",
        "Physical Therapist Assistant",
        "Clinician",
        "Provider"
    ];

    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments")
            .WithTags("Appointments")
            .RequireAuthorization(AuthorizationPolicies.SchedulingAccess);

        group.MapGet("/", ListAppointments)
            .WithName("ListAppointments")
            .WithSummary("List appointments and clinicians for the scheduling workspace");

        group.MapGet("/by-patient/{patientId:guid}", ListAppointmentsByPatient)
            .WithName("ListAppointmentsByPatient")
            .WithSummary("List appointments for a single patient within a date window");

        group.MapGet("/clinicians", ListClinicians)
            .WithName("ListAppointmentClinicians")
            .WithSummary("List clinicians for scheduling and export filters");

        group.MapPost("/", CreateAppointment)
            .WithName("CreateAppointment")
            .WithSummary("Create a new appointment");

        group.MapPut("/{id:guid}", UpdateAppointment)
            .WithName("UpdateAppointment")
            .WithSummary("Update an existing appointment");

        group.MapPatch("/{id:guid}/appointment-type", UpdateAppointmentType)
            .WithName("UpdateAppointmentType")
            .WithSummary("Update only an appointment's scheduling type");

        group.MapPost("/{id:guid}/check-in", CheckInAppointment)
            .WithName("CheckInAppointment")
            .WithSummary("Mark an appointment as checked in");

        group.MapPost("/{id:guid}/check-in-payment", CheckInAppointmentWithPayment)
            .WithName("CheckInAppointmentWithPayment")
            .WithSummary("Process a required copay before marking an appointment as checked in");
    }

    private static async Task<IResult> ListAppointments(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var currentClinicId = tenantContext.GetCurrentClinicId();
        var paymentAvailable = IsPaymentConfigured(configuration);

        if (!TryNormalizeDateRange(startDate, endDate, out var normalizedStartDate, out var normalizedEndDate, out var validationProblem))
        {
            return validationProblem!;
        }

        var rangeStartUtc = DateTime.SpecifyKind(normalizedStartDate, DateTimeKind.Utc);
        var rangeEndExclusiveUtc = DateTime.SpecifyKind(normalizedEndDate.AddDays(1), DateTimeKind.Utc);

        var appointments = await BuildAppointmentRowsQuery(
                db.Appointments
                    .AsNoTracking()
                    .Where(appointment => appointment.StartTimeUtc >= rangeStartUtc
                        && appointment.StartTimeUtc < rangeEndExclusiveUtc),
                db)
            .OrderBy(row => row.StartTimeUtc)
            .ThenBy(row => row.PatientName)
            .ToListAsync(cancellationToken);
        await HydrateAppointmentNoteWorkflowAsync(db, appointments, cancellationToken);
        await HydrateAppointmentClinicalMetadataAsync(db, appointments, cancellationToken);

        var clinicians = await BuildCliniciansQuery(db, currentClinicId).ToListAsync(cancellationToken);

        return Results.Ok(new AppointmentsOverviewResponse
        {
            Appointments = appointments.Select(row => ToResponse(row, paymentAvailable)).ToList(),
            Clinicians = clinicians
        });
    }

    private static async Task<IResult> ListAppointmentsByPatient(
        Guid patientId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (patientId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(patientId), ["PatientId is required."] }
            });
        }

        if (!TryNormalizeDateRange(startDate, endDate, out var normalizedStartDate, out var normalizedEndDate, out var validationProblem))
        {
            return validationProblem!;
        }

        var rangeStartUtc = DateTime.SpecifyKind(normalizedStartDate, DateTimeKind.Utc);
        var rangeEndExclusiveUtc = DateTime.SpecifyKind(normalizedEndDate.AddDays(1), DateTimeKind.Utc);

        var appointments = await BuildAppointmentRowsQuery(
                db.Appointments
                    .AsNoTracking()
                    .Where(appointment => appointment.PatientId == patientId
                        && appointment.StartTimeUtc >= rangeStartUtc
                        && appointment.StartTimeUtc < rangeEndExclusiveUtc),
                db)
            .OrderBy(row => row.StartTimeUtc)
            .ThenBy(row => row.AppointmentType)
            .ToListAsync(cancellationToken);
        await HydrateAppointmentNoteWorkflowAsync(db, appointments, cancellationToken);
        await HydrateAppointmentClinicalMetadataAsync(db, appointments, cancellationToken);

        var paymentAvailable = IsPaymentConfigured(configuration);
        return Results.Ok(appointments.Select(row => ToResponse(row, paymentAvailable)).ToList());
    }

    private static async Task<IResult> ListClinicians(
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        CancellationToken cancellationToken)
    {
        var clinicians = await BuildCliniciansQuery(db, tenantContext.GetCurrentClinicId()).ToListAsync(cancellationToken);
        return Results.Ok(clinicians);
    }

    private static async Task<IResult> CreateAppointment(
        [FromBody] CreateAppointmentRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IClinicalVisitOrdinalAllocator visitOrdinalAllocator,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateWriteRequest(
            request.PatientId,
            request.ClinicianId,
            request.AppointmentType,
            request.AppointmentDate,
            request.AppointmentTime,
            request.DurationMinutes);

        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == request.PatientId && !p.IsArchived, cancellationToken);

        if (patient is null)
        {
            return Results.NotFound(new { error = $"Patient {request.PatientId} not found." });
        }

        var clinician = await GetClinicianAsync(db, request.ClinicianId, patient.ClinicId, cancellationToken);
        if (clinician is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ClinicianId), ["Clinician is invalid or not available for this clinic."] }
            });
        }

        if (!TryMapAppointmentType(request.AppointmentType, out var appointmentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.AppointmentType), ["Appointment type is not supported."] }
            });
        }

        var (startUtc, endUtc) = BuildUtcRange(request.AppointmentDate, request.AppointmentTime, request.DurationMinutes);
        var schedulingConflict = await GetSchedulingConflictAsync(
            db,
            clinician.Id,
            patient.ClinicId,
            startUtc,
            endUtc,
            excludeAppointmentId: null,
            cancellationToken);

        if (schedulingConflict is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.AppointmentTime), [BuildSchedulingConflictMessage(schedulingConflict)] }
            });
        }

        Appointment? appointment = null;
        const int maxOrdinalAllocationAttempts = 3;
        for (var attempt = 1; attempt <= maxOrdinalAllocationAttempts; attempt++)
        {
            appointment = new Appointment
            {
                PatientId = patient.Id,
                ClinicalId = clinician.Id,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                AppointmentType = appointmentType,
                Status = AppointmentStatus.Scheduled,
                Notes = NormalizeNotes(request.Notes),
                ClinicId = patient.ClinicId,
                LastModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = identityContext.GetCurrentUserId(),
                SyncState = SyncState.Pending
            };
            appointment.AssignClinicalVisitOrdinal(
                await visitOrdinalAllocator.GetNextAsync(patient.Id, cancellationToken));

            db.Appointments.Add(appointment);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (IsSchedulingConflictDbException(ex))
            {
                return BuildSchedulingConflictResult();
            }
            catch (DbUpdateException ex) when (IsClinicalVisitOrdinalConflictDbException(ex))
            {
                db.Entry(appointment).State = EntityState.Detached;
                if (attempt == maxOrdinalAllocationAttempts)
                {
                    return Results.Conflict(new { error = "Another appointment reserved this patient's next visit number. Try scheduling again." });
                }
            }
        }

        var response = await BuildAppointmentResponseAsync(db, appointment!.Id, IsPaymentConfigured(configuration), cancellationToken);
        return Results.Created($"/api/v1/appointments/{appointment.Id}", response);
    }

    private static async Task<IResult> UpdateAppointment(
        Guid id,
        [FromBody] UpdateAppointmentRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateWriteRequest(
            request.PatientId,
            request.ClinicianId,
            request.AppointmentType,
            request.AppointmentDate,
            request.AppointmentTime,
            request.DurationMinutes);

        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (appointment is null)
        {
            return Results.NotFound(new { error = $"Appointment {id} not found." });
        }

        var patient = await db.Patients
            .FirstOrDefaultAsync(p => p.Id == request.PatientId && !p.IsArchived, cancellationToken);

        if (patient is null)
        {
            return Results.NotFound(new { error = $"Patient {request.PatientId} not found." });
        }

        if (appointment.ClinicalVisitOrdinal.HasValue && appointment.PatientId != patient.Id)
        {
            return Results.Conflict(new
            {
                error = "A numbered clinical visit cannot be reassigned to another patient. Create a new appointment instead."
            });
        }

        var clinician = await GetClinicianAsync(db, request.ClinicianId, patient.ClinicId, cancellationToken);
        if (clinician is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ClinicianId), ["Clinician is invalid or not available for this clinic."] }
            });
        }

        if (!TryMapAppointmentType(request.AppointmentType, out var appointmentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.AppointmentType), ["Appointment type is not supported."] }
            });
        }

        var (startUtc, endUtc) = BuildUtcRange(request.AppointmentDate, request.AppointmentTime, request.DurationMinutes);
        var schedulingConflict = await GetSchedulingConflictAsync(
            db,
            clinician.Id,
            patient.ClinicId,
            startUtc,
            endUtc,
            excludeAppointmentId: id,
            cancellationToken);

        if (schedulingConflict is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.AppointmentTime), [BuildSchedulingConflictMessage(schedulingConflict)] }
            });
        }

        appointment.PatientId = patient.Id;
        appointment.ClinicalId = clinician.Id;
        appointment.StartTimeUtc = startUtc;
        appointment.EndTimeUtc = endUtc;
        appointment.AppointmentType = appointmentType;
        appointment.Notes = NormalizeNotes(request.Notes);
        appointment.ClinicId = patient.ClinicId;
        MarkAppointmentModified(appointment, identityContext.GetCurrentUserId());

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSchedulingConflictDbException(ex))
        {
            return BuildSchedulingConflictResult();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "The appointment was changed by another user. Refresh and try again." });
        }

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, IsPaymentConfigured(configuration), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> UpdateAppointmentType(
        Guid id,
        [FromBody] UpdateAppointmentTypeRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuditService auditService,
        CancellationToken cancellationToken)
    {
        if (!TryMapAppointmentType(request.AppointmentType, out var appointmentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.AppointmentType), ["Appointment type is not supported."] }
            });
        }

        if (request.ExpectedLastModifiedUtc == default)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.ExpectedLastModifiedUtc), ["The appointment version is required."] }
            });
        }

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (appointment is null)
        {
            return Results.NotFound(new { error = $"Appointment {id} not found." });
        }

        if (appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
        {
            return Results.Conflict(new { error = "The appointment type cannot be changed after the appointment reaches a terminal status." });
        }

        if (appointment.LastModifiedUtc != request.ExpectedLastModifiedUtc)
        {
            return Results.Conflict(new
            {
                error = "The appointment was changed by another user. Refresh and try again.",
                lastModifiedUtc = appointment.LastModifiedUtc
            });
        }

        if (appointment.AppointmentType == appointmentType)
        {
            var unchanged = await BuildAppointmentResponseAsync(db, appointment.Id, IsPaymentConfigured(configuration), cancellationToken);
            return Results.Ok(unchanged);
        }

        var previousAppointmentType = appointment.AppointmentType;
        var modifiedByUserId = identityContext.GetCurrentUserId();
        appointment.AppointmentType = appointmentType;
        MarkAppointmentModified(appointment, modifiedByUserId);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "The appointment was changed by another user. Refresh and try again." });
        }

        await auditService.LogAppointmentEventAsync(new AuditEvent
        {
            EventType = "AppointmentTypeChanged",
            UserId = modifiedByUserId,
            EntityType = nameof(Appointment),
            EntityId = appointment.Id,
            Metadata = new Dictionary<string, object>
            {
                ["PreviousAppointmentType"] = previousAppointmentType.ToString(),
                ["NewAppointmentType"] = appointmentType.ToString(),
                ["TimestampUtc"] = appointment.LastModifiedUtc
            }
        }, cancellationToken);

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, IsPaymentConfigured(configuration), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CheckInAppointment(
        Guid id,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (appointment is null)
        {
            return Results.NotFound(new { error = $"Appointment {id} not found." });
        }

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
        {
            return Results.UnprocessableEntity(new { error = "Cancelled or no-show appointments cannot be checked in." });
        }

        if (await IsCopayPaymentRequiredAsync(db, appointment.PatientId, appointment.Id, cancellationToken))
        {
            var error = IsPaymentConfigured(configuration)
                ? "Copay payment is required before check-in."
                : "Copay collection is not configured for this appointment.";
            return Results.UnprocessableEntity(new { error });
        }

        await MarkAppointmentCheckedInAsync(db, appointment, identityContext.GetCurrentUserId(), cancellationToken);

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, IsPaymentConfigured(configuration), cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CheckInAppointmentWithPayment(
        Guid id,
        [FromBody] AppointmentCheckInPaymentRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IConfiguration configuration,
        [FromServices] IPaymentService paymentService,
        [FromServices] IAuditService auditService,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OpaqueDataDescriptor) || string.IsNullOrWhiteSpace(request.OpaqueDataToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.OpaqueDataDescriptor), ["Payment authorization is required."] },
                { nameof(request.OpaqueDataToken), ["Payment authorization is required."] }
            });
        }

        if (!IsPaymentConfigured(configuration))
        {
            return Results.BadRequest(new { error = "Payment processing is not configured." });
        }

        await using var paymentLock = await AcquireAppointmentPaymentLockAsync(id, cancellationToken);

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);

        if (appointment is null)
        {
            return Results.NotFound(new { error = $"Appointment {id} not found." });
        }

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
        {
            return Results.UnprocessableEntity(new { error = "Cancelled or no-show appointments cannot be checked in." });
        }

        var patient = await db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.Id == appointment.PatientId && !existing.IsArchived, cancellationToken);

        if (patient is null)
        {
            return Results.NotFound(new { error = $"Patient {appointment.PatientId} not found." });
        }

        var existingTransactionId = await GetSuccessfulPaymentTransactionIdAsync(db, appointment.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existingTransactionId))
        {
            if (request.CheckInAfterPayment)
            {
                await MarkAppointmentCheckedInAsync(db, appointment, identityContext.GetCurrentUserId(), cancellationToken);
            }

            var paidAppointment = await BuildAppointmentResponseAsync(db, appointment.Id, paymentAvailable: true, cancellationToken);
            paidAppointment.SuccessfulPaymentTransactionId = existingTransactionId;
            return Results.Ok(new AppointmentCheckInPaymentResponse
            {
                Appointment = paidAppointment,
                Payment = new PaymentResult
                {
                    Success = true,
                    TransactionId = existingTransactionId,
                    ProcessedAt = DateTime.UtcNow,
                    Amount = paidAppointment.CopayAmount
                }
            });
        }

        if (await HasPendingPaymentTransactionAsync(db, appointment.Id, cancellationToken))
        {
            return await BuildPaymentInProgressResponseAsync(db, appointment.Id, cancellationToken);
        }

        var copayAmount = await GetCopayAmountAsync(db, patient.Id, patient.PayerInfoJson, cancellationToken);
        if (copayAmount is null || copayAmount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { "copayAmount", ["No copay is due for this appointment."] }
            });
        }

        var transaction = new AppointmentPaymentTransaction
        {
            AppointmentId = appointment.Id,
            PatientId = patient.Id,
            Amount = copayAmount.Value,
            Status = AppointmentPaymentStatus.Pending,
            Processor = "AuthorizeNet",
            InvoiceNumber = BuildCopayInvoiceNumber(appointment.Id),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.AppointmentPaymentTransactions.Add(transaction);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPaymentConcurrencyDbException(ex))
        {
            db.Entry(transaction).State = EntityState.Detached;
            return await BuildConcurrentPaymentResponseAsync(db, appointment.Id, request.CheckInAfterPayment, identityContext.GetCurrentUserId(), cancellationToken);
        }

        PaymentResult paymentResult;
        try
        {
            paymentResult = await paymentService.ProcessPaymentAsync(new PaymentRequest
            {
                AppointmentId = appointment.Id,
                PatientId = patient.Id,
                Amount = copayAmount.Value,
                OpaqueDataDescriptor = request.OpaqueDataDescriptor.Trim(),
                OpaqueDataToken = request.OpaqueDataToken.Trim(),
                InvoiceNumber = transaction.InvoiceNumber,
                Description = $"PTDoc copay for appointment {appointment.Id:D}"
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            paymentResult = BuildPaymentGatewayFailure(copayAmount.Value, "GATEWAY_REQUEST_CANCELED");
        }
        catch (Exception)
        {
            paymentResult = BuildPaymentGatewayFailure(copayAmount.Value, "GATEWAY_REQUEST_FAILED");
        }

        var normalizedTransactionId = NormalizePaymentField(paymentResult.TransactionId, PaymentTransactionIdMaxLength);
        var normalizedAuthorizationCode = NormalizePaymentField(paymentResult.AuthorizationCode, PaymentAuthorizationCodeMaxLength);
        if (paymentResult.Success && string.IsNullOrWhiteSpace(normalizedTransactionId))
        {
            paymentResult = BuildPaymentGatewayFailure(
                copayAmount.Value,
                "GATEWAY_RESPONSE_INVALID",
                "Payment gateway returned an invalid response");
            paymentResult.AuthorizationCode = normalizedAuthorizationCode;
        }

        transaction.Status = paymentResult.Success ? AppointmentPaymentStatus.Succeeded : AppointmentPaymentStatus.Failed;
        transaction.TransactionId = paymentResult.Success
            ? normalizedTransactionId
            : NormalizePaymentField(paymentResult.TransactionId, PaymentTransactionIdMaxLength);
        transaction.AuthorizationCode = normalizedAuthorizationCode;
        transaction.GatewayErrorCode = NormalizePaymentField(paymentResult.ErrorCode, PaymentGatewayErrorCodeMaxLength);
        transaction.GatewayErrorMessage = NormalizePaymentField(paymentResult.ErrorMessage, PaymentGatewayErrorMessageMaxLength);
        transaction.ProcessedAtUtc = paymentResult.ProcessedAt == default ? DateTime.UtcNow : paymentResult.ProcessedAt;
        paymentResult.TransactionId = transaction.TransactionId;
        paymentResult.AuthorizationCode = transaction.AuthorizationCode;
        paymentResult.ErrorCode = transaction.GatewayErrorCode;
        paymentResult.ErrorMessage = transaction.GatewayErrorMessage;

        if (paymentResult.Success && request.CheckInAfterPayment)
        {
            await MarkAppointmentCheckedInAsync(db, appointment, identityContext.GetCurrentUserId(), cancellationToken, saveChanges: false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPaymentConcurrencyDbException(ex))
        {
            db.Entry(transaction).State = EntityState.Detached;
            return await BuildConcurrentPaymentResponseAsync(db, appointment.Id, request.CheckInAfterPayment, identityContext.GetCurrentUserId(), cancellationToken);
        }

        await auditService.LogRuleEvaluationAsync(new PTDoc.Application.Compliance.AuditEvent
        {
            EventType = "AppointmentCopayPayment",
            Metadata = new Dictionary<string, object>
            {
                ["Success"] = paymentResult.Success,
                ["AppointmentId"] = appointment.Id.ToString("D"),
                ["PatientId"] = patient.Id.ToString("D"),
                ["TransactionId"] = paymentResult.TransactionId ?? string.Empty,
                ["Amount"] = copayAmount.Value
            }
        });

        var updatedAppointment = await BuildAppointmentResponseAsync(db, appointment.Id, paymentAvailable: true, cancellationToken);
        return Results.Ok(new AppointmentCheckInPaymentResponse
        {
            Appointment = updatedAppointment,
            Payment = paymentResult
        });
    }

    private static async Task<IResult> BuildConcurrentPaymentResponseAsync(
        ApplicationDbContext db,
        Guid appointmentId,
        bool checkInAfterPayment,
        Guid modifiedByUserId,
        CancellationToken cancellationToken)
    {
        var existingTransactionId = await GetSuccessfulPaymentTransactionIdAsync(db, appointmentId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(existingTransactionId))
        {
            if (checkInAfterPayment)
            {
                var appointment = await db.Appointments
                    .FirstAsync(existing => existing.Id == appointmentId, cancellationToken);
                await MarkAppointmentCheckedInAsync(db, appointment, modifiedByUserId, cancellationToken);
            }

            var paidAppointment = await BuildAppointmentResponseAsync(db, appointmentId, paymentAvailable: true, cancellationToken);
            paidAppointment.SuccessfulPaymentTransactionId = existingTransactionId;
            return Results.Ok(new AppointmentCheckInPaymentResponse
            {
                Appointment = paidAppointment,
                Payment = new PaymentResult
                {
                    Success = true,
                    TransactionId = existingTransactionId,
                    ProcessedAt = DateTime.UtcNow,
                    Amount = paidAppointment.CopayAmount
                }
            });
        }

        return await BuildPaymentInProgressResponseAsync(db, appointmentId, cancellationToken);
    }

    private static string? TruncatePaymentField(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string? NormalizePaymentField(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TruncatePaymentField(value.Trim(), maxLength);
    }

    private static PaymentResult BuildPaymentGatewayFailure(
        decimal amount,
        string errorCode,
        string errorMessage = "Payment gateway request failed") =>
        new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            ProcessedAt = DateTime.UtcNow,
            Amount = amount
        };

    private static async Task<IResult> BuildPaymentInProgressResponseAsync(
        ApplicationDbContext db,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        AppointmentListItemResponse? currentAppointment = null;
        try
        {
            currentAppointment = await BuildAppointmentResponseAsync(db, appointmentId, paymentAvailable: true, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep concurrent duplicate payment attempts user-safe even if the snapshot read races payment persistence.
        }

        return Results.Ok(new AppointmentCheckInPaymentResponse
        {
            Appointment = currentAppointment,
            Payment = new PaymentResult
            {
                Success = false,
                ErrorCode = "PAYMENT_IN_PROGRESS",
                ErrorMessage = "A copay payment is already being processed for this appointment.",
                ProcessedAt = DateTime.UtcNow,
                Amount = currentAppointment?.CopayAmount
            }
        });
    }

    private static async Task<bool> HasPendingPaymentTransactionAsync(
        ApplicationDbContext db,
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        await db.AppointmentPaymentTransactions
            .AsNoTracking()
            .AnyAsync(payment => payment.AppointmentId == appointmentId
                && payment.Status == AppointmentPaymentStatus.Pending, cancellationToken);

    private static async Task<string?> GetSuccessfulPaymentTransactionIdAsync(
        ApplicationDbContext db,
        Guid appointmentId,
        CancellationToken cancellationToken) =>
        await db.AppointmentPaymentTransactions
            .AsNoTracking()
            .Where(payment => payment.AppointmentId == appointmentId
                && payment.Status == AppointmentPaymentStatus.Succeeded
                && payment.TransactionId != null
                && payment.TransactionId.Trim() != string.Empty)
            .OrderByDescending(payment => payment.ProcessedAtUtc ?? payment.CreatedAtUtc)
            .Select(payment => payment.TransactionId)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<bool> IsCopayPaymentRequiredAsync(
        ApplicationDbContext db,
        Guid patientId,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var successfulTransactionId = await GetSuccessfulPaymentTransactionIdAsync(db, appointmentId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(successfulTransactionId))
        {
            return false;
        }

        var payerInfoJson = await db.Patients
            .AsNoTracking()
            .Where(patient => patient.Id == patientId && !patient.IsArchived)
            .Select(patient => patient.PayerInfoJson)
            .FirstOrDefaultAsync(cancellationToken);

        return await GetCopayAmountAsync(db, patientId, payerInfoJson, cancellationToken) is > 0;
    }

    private static async Task<decimal?> GetCopayAmountAsync(
        ApplicationDbContext db,
        Guid patientId,
        string? legacyPayerInfoJson,
        CancellationToken cancellationToken)
    {
        var normalized = await db.PatientInsurancePolicies.AsNoTracking()
            .Where(policy => policy.PatientId == patientId && !policy.IsArchived && policy.Status == InsurancePolicyStatus.Active)
            .OrderBy(policy => policy.CoveragePriority)
            .Select(policy => policy.CopayAmount)
            .FirstOrDefaultAsync(cancellationToken);
        return normalized ?? TryParseCopayAmount(legacyPayerInfoJson);
    }

    private static async Task MarkAppointmentCheckedInAsync(
        ApplicationDbContext db,
        Appointment appointment,
        Guid modifiedByUserId,
        CancellationToken cancellationToken,
        bool saveChanges = true)
    {
        var changed = false;
        if (appointment.Status != AppointmentStatus.CheckedIn
            && appointment.Status != AppointmentStatus.InProgress
            && appointment.Status != AppointmentStatus.Completed)
        {
            appointment.Status = AppointmentStatus.CheckedIn;
            changed = true;
        }

        if (changed)
        {
            MarkAppointmentModified(appointment, modifiedByUserId);
            if (saveChanges)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static void MarkAppointmentModified(Appointment appointment, Guid modifiedByUserId)
    {
        appointment.LastModifiedUtc = DateTime.UtcNow;
        appointment.ModifiedByUserId = modifiedByUserId;
        appointment.SyncState = SyncState.Pending;
    }

    private static string BuildCopayInvoiceNumber(Guid appointmentId) =>
        $"CP-{appointmentId:N}"[..20];

    private static bool IsPaymentConfigured(IConfiguration configuration) =>
        configuration.GetValue<bool>("Integrations:Payments:Enabled")
        && !string.IsNullOrWhiteSpace(configuration["Integrations:Payments:ApiLoginId"])
        && !string.IsNullOrWhiteSpace(configuration["Integrations:Payments:TransactionKey"])
        && !string.IsNullOrWhiteSpace(configuration["Integrations:Payments:ClientKey"]);

    private static decimal? TryParseCopayAmount(string? payerInfoJson)
    {
        if (string.IsNullOrWhiteSpace(payerInfoJson) || payerInfoJson == "{}")
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payerInfoJson);
            if (!document.RootElement.TryGetProperty("copayAmount", out var copayElement))
            {
                return null;
            }

            return copayElement.ValueKind switch
            {
                JsonValueKind.Number when copayElement.TryGetDecimal(out var amount) => amount,
                JsonValueKind.String when decimal.TryParse(
                    copayElement.GetString()?.Trim(),
                    NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                    CopayCurrencyCulture,
                    out var amount) => amount,
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IQueryable<AppointmentQueryRow> BuildAppointmentRowsQuery(
        IQueryable<Appointment> appointmentQuery,
        ApplicationDbContext db)
    {
        return
            from appointment in appointmentQuery
            join patient in db.Patients.AsNoTracking() on appointment.PatientId equals patient.Id
            join clinician in db.Users.AsNoTracking() on appointment.ClinicalId equals clinician.Id into clinicianJoin
            from clinician in clinicianJoin.DefaultIfEmpty()
            where !patient.IsArchived
            select new AppointmentQueryRow
            {
                Id = appointment.Id,
                PatientRecordId = patient.Id,
                PatientName = patient.FirstName + " " + patient.LastName,
                MedicalRecordNumber = patient.MedicalRecordNumber,
                ClinicianId = clinician != null ? clinician.Id : null,
                ClinicianFirstName = clinician != null ? clinician.FirstName : null,
                ClinicianLastName = clinician != null ? clinician.LastName : null,
                StartTimeUtc = appointment.StartTimeUtc,
                EndTimeUtc = appointment.EndTimeUtc,
                AppointmentType = appointment.AppointmentType,
                ClinicalVisitOrdinal = appointment.ClinicalVisitOrdinal,
                AppointmentStatus = appointment.Status,
                Notes = appointment.Notes,
                LastModifiedUtc = appointment.LastModifiedUtc,
                PayerInfoJson = patient.PayerInfoJson,
                NormalizedCopayAmount = db.PatientInsurancePolicies.AsNoTracking()
                    .Where(policy => policy.PatientId == patient.Id && !policy.IsArchived && policy.Status == InsurancePolicyStatus.Active)
                    .OrderBy(policy => policy.CoveragePriority)
                    .Select(policy => policy.CopayAmount)
                    .FirstOrDefault(),
                VisitCount = db.Appointments
                    .AsNoTracking()
                    .Count(visit => visit.PatientId == patient.Id
                        && visit.StartTimeUtc <= appointment.StartTimeUtc
                        && (visit.Status == AppointmentStatus.CheckedIn
                            || visit.Status == AppointmentStatus.InProgress
                            || visit.Status == AppointmentStatus.Completed)),
                SuccessfulPaymentTransactionId = db.AppointmentPaymentTransactions
                    .AsNoTracking()
                    .Where(payment => payment.AppointmentId == appointment.Id
                        && payment.Status == AppointmentPaymentStatus.Succeeded
                        && payment.TransactionId != null
                        && payment.TransactionId.Trim() != string.Empty)
                    .OrderByDescending(payment => payment.ProcessedAtUtc ?? payment.CreatedAtUtc)
                    .Select(payment => payment.TransactionId)
                    .FirstOrDefault(),
                IntakeSubmittedAt = db.IntakeForms
                    .AsNoTracking()
                    .Where(intake => intake.PatientId == patient.Id)
                    .Max(intake => (DateTime?)intake.SubmittedAt),
                HasIntake = db.IntakeForms
                    .AsNoTracking()
                    .Any(intake => intake.PatientId == patient.Id)
            };
    }

    private static async Task<AppointmentListItemResponse> BuildAppointmentResponseAsync(
        ApplicationDbContext db,
        Guid appointmentId,
        bool paymentAvailable,
        CancellationToken cancellationToken)
    {
        var rows = await BuildAppointmentRowsQuery(
                db.Appointments
                    .AsNoTracking()
                    .Where(appointment => appointment.Id == appointmentId),
                db)
            .ToListAsync(cancellationToken);
        await HydrateAppointmentNoteWorkflowAsync(db, rows, cancellationToken);
        await HydrateAppointmentClinicalMetadataAsync(db, rows, cancellationToken);

        var row = rows.FirstOrDefault();

        if (row is null)
        {
            throw new InvalidOperationException($"Appointment {appointmentId} was saved but could not be reloaded.");
        }

        return ToResponse(row, paymentAvailable);
    }

    private static async Task HydrateAppointmentNoteWorkflowAsync(
        ApplicationDbContext db,
        IReadOnlyList<AppointmentQueryRow> appointments,
        CancellationToken cancellationToken)
    {
        if (appointments.Count == 0)
        {
            return;
        }

        var appointmentIds = appointments
            .Select(appointment => appointment.Id)
            .ToArray();

        const int appointmentIdBatchSize = 500;
        var noteRows = new List<AppointmentNoteWorkflowRow>();
        for (var offset = 0; offset < appointmentIds.Length; offset += appointmentIdBatchSize)
        {
            var batchIds = appointmentIds
                .Skip(offset)
                .Take(appointmentIdBatchSize)
                .ToArray();

            var batchRows = await db.ClinicalNotes
                .AsNoTracking()
                .Where(note => note.AppointmentId != null
                    && batchIds.Contains(note.AppointmentId.Value)
                    && !note.IsAddendum)
                .Select(note => new AppointmentNoteWorkflowRow
                {
                    AppointmentId = note.AppointmentId!.Value,
                    NoteId = note.Id,
                    IsCompleted = note.NoteStatus == NoteStatus.Signed
                        || note.SignatureHash != null
                        || note.SignedUtc != null,
                    LastModifiedUtc = note.LastModifiedUtc,
                    CreatedUtc = note.CreatedUtc
                })
                .ToListAsync(cancellationToken);

            noteRows.AddRange(batchRows);
        }

        var noteSummaries = noteRows
            .GroupBy(note => note.AppointmentId)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    HasCompletedNote = group.Any(note => note.IsCompleted),
                    VisitNoteId = group
                        .OrderByDescending(note => note.IsCompleted)
                        .ThenByDescending(note => note.LastModifiedUtc)
                        .ThenByDescending(note => note.CreatedUtc)
                        .Select(note => (Guid?)note.NoteId)
                        .FirstOrDefault()
                });

        foreach (var appointment in appointments)
        {
            if (!noteSummaries.TryGetValue(appointment.Id, out var noteSummary))
            {
                continue;
            }

            appointment.HasStartedNote = true;
            appointment.HasCompletedNote = noteSummary.HasCompletedNote;
            appointment.VisitNoteId = noteSummary.VisitNoteId;
        }
    }

    private static async Task HydrateAppointmentClinicalMetadataAsync(
        ApplicationDbContext db,
        IReadOnlyList<AppointmentQueryRow> appointments,
        CancellationToken cancellationToken)
    {
        if (appointments.Count == 0)
        {
            return;
        }

        var patientIds = appointments
            .Select(appointment => appointment.PatientRecordId)
            .Distinct()
            .ToArray();

        const int patientIdBatchSize = 500;
        var planOfCareRows = new List<AppointmentPlanOfCareRow>();
        for (var offset = 0; offset < patientIds.Length; offset += patientIdBatchSize)
        {
            var batchIds = patientIds
                .Skip(offset)
                .Take(patientIdBatchSize)
                .ToArray();

            var batchRows = await db.ClinicalNotes
                .AsNoTracking()
                .Where(note => batchIds.Contains(note.PatientId)
                    && !note.IsAddendum
                    && (note.NoteType == NoteType.Evaluation || note.NoteType == NoteType.ProgressNote))
                .Select(note => new AppointmentPlanOfCareRow
                {
                    PatientId = note.PatientId,
                    DateOfService = note.DateOfService,
                    LastModifiedUtc = note.LastModifiedUtc,
                    ContentJson = note.ContentJson
                })
                .ToListAsync(cancellationToken);

            planOfCareRows.AddRange(batchRows);
        }

        foreach (var planOfCareRow in planOfCareRows)
        {
            planOfCareRow.ProgressNoteDueDates = ReadProgressNoteDueDates(planOfCareRow.ContentJson);
        }

        var planRowsByPatient = planOfCareRows
            .GroupBy(row => row.PatientId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(row => row.DateOfService)
                    .ThenByDescending(row => row.LastModifiedUtc)
                    .ToList());

        foreach (var appointment in appointments)
        {
            if (!planRowsByPatient.TryGetValue(appointment.PatientRecordId, out var patientPlanRows))
            {
                continue;
            }

            var appointmentDate = appointment.StartTimeUtc.Date;
            foreach (var planRow in patientPlanRows.Where(row => row.DateOfService.Date <= appointmentDate))
            {
                var progressNoteDueDates = planRow.ProgressNoteDueDates;
                if (progressNoteDueDates.Count == 0)
                {
                    continue;
                }

                appointment.ProgressNoteDueDate = progressNoteDueDates
                    .FirstOrDefault(date => date.Date >= appointmentDate);

                if (!appointment.ProgressNoteDueDate.HasValue
                    || appointment.ProgressNoteDueDate.Value == default)
                {
                    appointment.ProgressNoteDueDate = progressNoteDueDates[^1];
                }

                break;
            }
        }
    }

    private static IReadOnlyList<DateTime> ReadProgressNoteDueDates(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return Array.Empty<DateTime>();
        }

        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!TryGetJsonProperty(document.RootElement, "plan", out var plan)
                || !TryGetJsonProperty(plan, "computedPlanOfCare", out var computedPlanOfCare)
                || !TryGetJsonProperty(computedPlanOfCare, "progressNoteDueDates", out var dueDatesElement)
                || dueDatesElement.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DateTime>();
            }

            return dueDatesElement
                .EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String
                    && element.TryGetDateTime(out var dueDate)
                        ? (DateTime?)dueDate.Date
                        : null)
                .Where(date => date.HasValue)
                .Select(date => date!.Value)
                .Distinct()
                .OrderBy(date => date)
                .ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<DateTime>();
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }

        property = default;
        return false;
    }

    private static Dictionary<string, string[]> ValidateWriteRequest(
        Guid patientId,
        Guid clinicianId,
        string appointmentType,
        DateTime appointmentDate,
        TimeSpan appointmentTime,
        int durationMinutes)
    {
        var errors = new Dictionary<string, string[]>();

        if (patientId == Guid.Empty)
        {
            errors[nameof(CreateAppointmentRequest.PatientId)] = ["PatientId is required."];
        }

        if (clinicianId == Guid.Empty)
        {
            errors[nameof(CreateAppointmentRequest.ClinicianId)] = ["ClinicianId is required."];
        }

        if (string.IsNullOrWhiteSpace(appointmentType))
        {
            errors[nameof(CreateAppointmentRequest.AppointmentType)] = ["AppointmentType is required."];
        }

        if (appointmentDate == default)
        {
            errors[nameof(CreateAppointmentRequest.AppointmentDate)] = ["AppointmentDate is required."];
        }

        if (appointmentTime < TimeSpan.Zero || appointmentTime >= TimeSpan.FromDays(1))
        {
            errors[nameof(CreateAppointmentRequest.AppointmentTime)] = ["AppointmentTime must be a valid time of day."];
        }

        if (durationMinutes <= 0 || durationMinutes > 480)
        {
            errors[nameof(CreateAppointmentRequest.DurationMinutes)] = ["DurationMinutes must be between 1 and 480."];
        }

        return errors;
    }

    private static bool TryNormalizeDateRange(
        DateTime startDate,
        DateTime endDate,
        out DateTime normalizedStartDate,
        out DateTime normalizedEndDate,
        out IResult? validationProblem)
    {
        normalizedStartDate = startDate == default ? DateTime.Today : startDate.Date;
        normalizedEndDate = endDate == default ? normalizedStartDate : endDate.Date;

        if (normalizedEndDate < normalizedStartDate)
        {
            validationProblem = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(endDate), ["End date must be greater than or equal to start date."] }
            });
            return false;
        }

        validationProblem = null;
        return true;
    }

    private static IQueryable<AppointmentClinicianResponse> BuildCliniciansQuery(
        ApplicationDbContext db,
        Guid? clinicId)
    {
        return db.Users
            .AsNoTracking()
            .Where(user => user.IsActive
                && (clinicId == null || user.ClinicId == clinicId)
                && SchedulableClinicianRoles.Contains(user.Role))
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new AppointmentClinicianResponse
            {
                Id = user.Id,
                DisplayName = user.FirstName + " " + user.LastName
            });
    }

    private static async Task<User?> GetClinicianAsync(
        ApplicationDbContext db,
        Guid clinicianId,
        Guid? clinicId,
        CancellationToken cancellationToken)
    {
        return await db.Users
            .AsNoTracking()
            .Where(user => user.Id == clinicianId
                && user.IsActive
                && user.ClinicId == clinicId
                && SchedulableClinicianRoles.Contains(user.Role))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryMapAppointmentType(string appointmentType, out AppointmentType result) =>
        AppointmentTypeCatalog.TryParse(appointmentType, out result);

    private static (DateTime StartUtc, DateTime EndUtc) BuildUtcRange(
        DateTime appointmentDate,
        TimeSpan appointmentTime,
        int durationMinutes)
    {
        var localStart = DateTime.SpecifyKind(appointmentDate.Date.Add(appointmentTime), DateTimeKind.Local);
        var startUtc = localStart.ToUniversalTime();
        return (startUtc, startUtc.AddMinutes(durationMinutes));
    }

    private static async Task<AppointmentConflictRow?> GetSchedulingConflictAsync(
        ApplicationDbContext db,
        Guid clinicianId,
        Guid? clinicId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeAppointmentId,
        CancellationToken cancellationToken)
    {
        return await db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.ClinicalId == clinicianId
                && appointment.ClinicId == clinicId
                && appointment.Status != AppointmentStatus.Cancelled
                && appointment.Status != AppointmentStatus.NoShow
                && (!excludeAppointmentId.HasValue || appointment.Id != excludeAppointmentId.Value)
                && appointment.StartTimeUtc < endUtc
                && startUtc < appointment.EndTimeUtc)
            .OrderBy(appointment => appointment.StartTimeUtc)
            .Select(appointment => new AppointmentConflictRow
            {
                StartTimeUtc = appointment.StartTimeUtc,
                EndTimeUtc = appointment.EndTimeUtc
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string BuildSchedulingConflictMessage(AppointmentConflictRow conflict)
    {
        var localStart = DateTime.SpecifyKind(conflict.StartTimeUtc, DateTimeKind.Utc).ToLocalTime();
        var localEnd = DateTime.SpecifyKind(conflict.EndTimeUtc, DateTimeKind.Utc).ToLocalTime();
        return $"This clinician is already booked from {localStart:h:mm tt} to {localEnd:h:mm tt} on {localStart:MMM d, yyyy}.";
    }

    private static IResult BuildSchedulingConflictResult() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            { nameof(CreateAppointmentRequest.AppointmentTime), ["This clinician is already booked for the selected time."] }
        });

    private static bool IsSchedulingConflictDbException(DbUpdateException exception) =>
        exception.GetBaseException().Message.Contains(AppointmentOverbookingErrorCode, StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentConcurrencyDbException(DbUpdateException exception)
    {
        return exception.GetBaseException() switch
        {
            Microsoft.Data.Sqlite.SqliteException sqlite => sqlite.SqliteErrorCode == 19,
            Microsoft.Data.SqlClient.SqlException sql => sql.Number is 2627 or 2601,
            Npgsql.PostgresException pg => pg.SqlState == "23505",
            _ => false
        };
    }

    private static bool IsClinicalVisitOrdinalConflictDbException(DbUpdateException exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Contains("ClinicalVisitOrdinal", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UX_Appointments_PatientId_ClinicalVisitOrdinal", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AppointmentPaymentLockLease> AcquireAppointmentPaymentLockAsync(
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        AppointmentPaymentLockState state;
        while (true)
        {
            state = AppointmentPaymentLocks.GetOrAdd(appointmentId, _ => new AppointmentPaymentLockState());
            if (state.TryAddReference())
            {
                break;
            }

            AppointmentPaymentLocks.TryRemove(appointmentId, out _);
        }

        try
        {
            await state.Semaphore.WaitAsync(cancellationToken);
            return new AppointmentPaymentLockLease(appointmentId, state);
        }
        catch
        {
            ReleaseAppointmentPaymentLockReference(appointmentId, state);
            throw;
        }
    }

    private static void ReleaseAppointmentPaymentLockReference(Guid appointmentId, AppointmentPaymentLockState state)
    {
        if (state.ReleaseReferenceAndMarkRemoved())
        {
            AppointmentPaymentLocks.TryRemove(appointmentId, out _);
        }
    }

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    private static AppointmentListItemResponse ToResponse(AppointmentQueryRow row, bool paymentAvailable)
    {
        var visitWorkflowStatus = MapVisitWorkflowStatus(row.AppointmentStatus, row.HasStartedNote, row.HasCompletedNote);
        var copayAmount = row.NormalizedCopayAmount ?? TryParseCopayAmount(row.PayerInfoJson);
        var hasSuccessfulPayment = !string.IsNullOrWhiteSpace(row.SuccessfulPaymentTransactionId);
        var canRecordCopay = copayAmount > 0 && !hasSuccessfulPayment && paymentAvailable;
        var (copayStatusLabel, unavailableReason) = BuildCopayStatus(copayAmount, hasSuccessfulPayment, paymentAvailable);

        return new AppointmentListItemResponse
        {
            Id = row.Id,
            PatientRecordId = row.PatientRecordId,
            PatientName = row.PatientName,
            MedicalRecordNumber = row.MedicalRecordNumber,
            ClinicianId = row.ClinicianId,
            ClinicianName = BuildClinicianName(row.ClinicianFirstName, row.ClinicianLastName),
            StartTimeUtc = DateTime.SpecifyKind(row.StartTimeUtc, DateTimeKind.Utc),
            EndTimeUtc = DateTime.SpecifyKind(row.EndTimeUtc, DateTimeKind.Utc),
            AppointmentType = MapAppointmentType(row.AppointmentType),
            AppointmentStatus = MapAppointmentStatus(row.AppointmentStatus),
            VisitWorkflowStatus = visitWorkflowStatus,
            VisitNoteId = ResolveVisitNoteId(row, visitWorkflowStatus),
            IntakeStatus = MapIntakeStatus(row.HasIntake, row.IntakeSubmittedAt),
            Notes = row.Notes?.Trim() ?? string.Empty,
            LastModifiedUtc = DateTime.SpecifyKind(row.LastModifiedUtc, DateTimeKind.Utc),
            VisitCount = row.VisitCount,
            VisitNumber = ResolveVisitNumber(row.AppointmentStatus, row.ClinicalVisitOrdinal, row.VisitCount),
            ProgressNoteDueDate = row.ProgressNoteDueDate,
            CopayAmount = copayAmount,
            CopayStatusLabel = copayStatusLabel,
            CanRecordCopay = canRecordCopay,
            CopayActionUnavailableReason = unavailableReason,
            SuccessfulPaymentTransactionId = row.SuccessfulPaymentTransactionId
        };
    }

    private static (string Label, string Reason) BuildCopayStatus(
        decimal? copayAmount,
        bool hasSuccessfulPayment,
        bool paymentAvailable)
    {
        if (copayAmount is null || copayAmount <= 0)
        {
            return ("No copay due", "No copay is due for this appointment.");
        }

        if (hasSuccessfulPayment)
        {
            return ("Copay paid", "Copay has already been recorded for this appointment.");
        }

        if (!paymentAvailable)
        {
            return ("Copay not configured", "Copay collection is not configured for this appointment.");
        }

        return ("Copay due", string.Empty);
    }

    private static Guid? ResolveVisitNoteId(AppointmentQueryRow row, string visitWorkflowStatus)
    {
        if (string.Equals(visitWorkflowStatus, "Note Started", StringComparison.OrdinalIgnoreCase))
        {
            return row.VisitNoteId;
        }

        return row.HasCompletedNote &&
            string.Equals(visitWorkflowStatus, "Completed", StringComparison.OrdinalIgnoreCase)
                ? row.VisitNoteId
                : null;
    }

    private static string BuildClinicianName(string? firstName, string? lastName)
    {
        var fullName = string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(fullName) ? "Assigned Clinician" : $"Dr. {fullName}";
    }

    private static string MapAppointmentType(AppointmentType appointmentType) =>
        AppointmentTypeCatalog.GetDisplayName(appointmentType);

    private static int? ResolveVisitNumber(
        AppointmentStatus status,
        int? clinicalVisitOrdinal,
        int attendedVisitCount) => status switch
        {
            AppointmentStatus.Scheduled or AppointmentStatus.Confirmed => clinicalVisitOrdinal ?? (attendedVisitCount + 1),
            AppointmentStatus.CheckedIn or AppointmentStatus.InProgress or AppointmentStatus.Completed => clinicalVisitOrdinal ?? Math.Max(1, attendedVisitCount),
            _ => null
        };

    private static string MapAppointmentStatus(AppointmentStatus status) =>
        status switch
        {
            AppointmentStatus.CheckedIn => "Checked In",
            AppointmentStatus.InProgress => "In Progress",
            AppointmentStatus.Completed => "Completed",
            AppointmentStatus.Cancelled => "Cancelled",
            AppointmentStatus.NoShow => "No Show",
            _ => "Scheduled"
        };

    private static string MapVisitWorkflowStatus(
        AppointmentStatus appointmentStatus,
        bool hasStartedNote,
        bool hasCompletedNote)
    {
        if (appointmentStatus is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
        {
            return MapAppointmentStatus(appointmentStatus);
        }

        if (appointmentStatus == AppointmentStatus.Completed || hasCompletedNote)
        {
            return "Completed";
        }

        if (hasStartedNote || appointmentStatus == AppointmentStatus.InProgress)
        {
            return "Note Started";
        }

        return appointmentStatus switch
        {
            AppointmentStatus.CheckedIn => "Checked In",
            _ => "Scheduled"
        };
    }

    private static string MapIntakeStatus(bool hasIntake, DateTime? intakeSubmittedAt)
    {
        if (!hasIntake)
        {
            return "Missing";
        }

        return intakeSubmittedAt.HasValue ? "Completed" : "In Progress";
    }

    private sealed class AppointmentQueryRow
    {
        public Guid Id { get; init; }
        public Guid PatientRecordId { get; init; }
        public string PatientName { get; init; } = string.Empty;
        public string? MedicalRecordNumber { get; init; }
        public Guid? ClinicianId { get; init; }
        public string? ClinicianFirstName { get; init; }
        public string? ClinicianLastName { get; init; }
        public DateTime StartTimeUtc { get; init; }
        public DateTime EndTimeUtc { get; init; }
        public AppointmentType AppointmentType { get; init; }
        public int? ClinicalVisitOrdinal { get; init; }
        public AppointmentStatus AppointmentStatus { get; init; }
        public string? Notes { get; init; }
        public DateTime LastModifiedUtc { get; init; }
        public string PayerInfoJson { get; init; } = "{}";
        public decimal? NormalizedCopayAmount { get; init; }
        public int VisitCount { get; init; }
        public DateTime? ProgressNoteDueDate { get; set; }
        public string? SuccessfulPaymentTransactionId { get; init; }
        public bool HasStartedNote { get; set; }
        public bool HasCompletedNote { get; set; }
        public Guid? VisitNoteId { get; set; }
        public DateTime? IntakeSubmittedAt { get; init; }
        public bool HasIntake { get; init; }
    }

    private sealed class AppointmentNoteWorkflowRow
    {
        public Guid AppointmentId { get; init; }
        public Guid NoteId { get; init; }
        public bool IsCompleted { get; init; }
        public DateTime LastModifiedUtc { get; init; }
        public DateTime CreatedUtc { get; init; }
    }

    private sealed class AppointmentPlanOfCareRow
    {
        public Guid PatientId { get; init; }
        public DateTime DateOfService { get; init; }
        public DateTime LastModifiedUtc { get; init; }
        public string ContentJson { get; init; } = "{}";
        public IReadOnlyList<DateTime> ProgressNoteDueDates { get; set; } = Array.Empty<DateTime>();
    }

    private sealed class AppointmentConflictRow
    {
        public DateTime StartTimeUtc { get; init; }
        public DateTime EndTimeUtc { get; init; }
    }

    private sealed class AppointmentPaymentLockState
    {
        private readonly object gate = new();
        private int referenceCount;
        private bool removed;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public bool TryAddReference()
        {
            lock (gate)
            {
                if (removed)
                {
                    return false;
                }

                referenceCount++;
                return true;
            }
        }

        public bool ReleaseReferenceAndMarkRemoved()
        {
            lock (gate)
            {
                referenceCount--;
                if (referenceCount != 0)
                {
                    return false;
                }

                removed = true;
                return true;
            }
        }
    }

    private sealed class AppointmentPaymentLockLease(Guid appointmentId, AppointmentPaymentLockState state) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            state.Semaphore.Release();
            ReleaseAppointmentPaymentLockReference(appointmentId, state);
            return ValueTask.CompletedTask;
        }
    }
}
