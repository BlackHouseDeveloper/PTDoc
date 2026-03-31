using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Appointments;

/// <summary>
/// Endpoints for appointment scheduling workflows.
/// </summary>
public static class AppointmentEndpoints
{
    private const string AppointmentOverbookingErrorCode = "APPOINTMENT_OVERBOOKING";
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

        group.MapPost("/", CreateAppointment)
            .WithName("CreateAppointment")
            .WithSummary("Create a new appointment");

        group.MapPut("/{id:guid}", UpdateAppointment)
            .WithName("UpdateAppointment")
            .WithSummary("Update an existing appointment");

        group.MapPost("/{id:guid}/check-in", CheckInAppointment)
            .WithName("CheckInAppointment")
            .WithSummary("Mark an appointment as checked in");
    }

    private static async Task<IResult> ListAppointments(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromServices] ApplicationDbContext db,
        [FromServices] ITenantContextAccessor tenantContext,
        CancellationToken cancellationToken)
    {
        var normalizedStartDate = startDate == default ? DateTime.Today : startDate.Date;
        var normalizedEndDate = endDate == default ? normalizedStartDate : endDate.Date;
        var currentClinicId = tenantContext.GetCurrentClinicId();

        if (normalizedEndDate < normalizedStartDate)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(endDate), ["End date must be greater than or equal to start date."] }
            });
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

        var clinicians = await db.Users
            .AsNoTracking()
            .Where(user => user.IsActive
                && (currentClinicId == null || user.ClinicId == currentClinicId)
                && SchedulableClinicianRoles.Contains(user.Role))
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new AppointmentClinicianResponse
            {
                Id = user.Id,
                DisplayName = user.FirstName + " " + user.LastName
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new AppointmentsOverviewResponse
        {
            Appointments = appointments.Select(ToResponse).ToList(),
            Clinicians = clinicians
        });
    }

    private static async Task<IResult> CreateAppointment(
        [FromBody] CreateAppointmentRequest request,
        [FromServices] ApplicationDbContext db,
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

        var appointment = new Appointment
        {
            PatientId = patient.Id,
            ClinicalId = clinician.Id,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            AppointmentType = appointmentType,
            Status = AppointmentStatus.Scheduled,
            Notes = NormalizeNotes(request.Notes),
            ClinicId = patient.ClinicId,
            SyncState = SyncState.Pending
        };

        db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSchedulingConflictDbException(ex))
        {
            return BuildSchedulingConflictResult();
        }

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, cancellationToken);
        return Results.Created($"/api/v1/appointments/{appointment.Id}", response);
    }

    private static async Task<IResult> UpdateAppointment(
        Guid id,
        [FromBody] UpdateAppointmentRequest request,
        [FromServices] ApplicationDbContext db,
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsSchedulingConflictDbException(ex))
        {
            return BuildSchedulingConflictResult();
        }

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CheckInAppointment(
        Guid id,
        [FromServices] ApplicationDbContext db,
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

        if (appointment.Status != AppointmentStatus.CheckedIn
            && appointment.Status != AppointmentStatus.InProgress
            && appointment.Status != AppointmentStatus.Completed)
        {
            appointment.Status = AppointmentStatus.CheckedIn;
            await db.SaveChangesAsync(cancellationToken);
        }

        var response = await BuildAppointmentResponseAsync(db, appointment.Id, cancellationToken);
        return Results.Ok(response);
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
                AppointmentStatus = appointment.Status,
                Notes = appointment.Notes,
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
        CancellationToken cancellationToken)
    {
        var row = await BuildAppointmentRowsQuery(
                db.Appointments
                    .AsNoTracking()
                    .Where(appointment => appointment.Id == appointmentId),
                db)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            throw new InvalidOperationException($"Appointment {appointmentId} was saved but could not be reloaded.");
        }

        return ToResponse(row);
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

    private static bool TryMapAppointmentType(string appointmentType, out AppointmentType result)
    {
        switch (appointmentType.Trim())
        {
            case "Initial Evaluation":
            case "Re-Evaluation":
                result = AppointmentType.InitialEvaluation;
                return true;
            case "Follow Up":
            case "Follow-up":
                result = AppointmentType.FollowUp;
                return true;
            case "Discharge":
                result = AppointmentType.Discharge;
                return true;
            default:
                result = AppointmentType.FollowUp;
                return false;
        }
    }

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

    private static string? NormalizeNotes(string? notes) =>
        string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    private static AppointmentListItemResponse ToResponse(AppointmentQueryRow row)
    {
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
            IntakeStatus = MapIntakeStatus(row.HasIntake, row.IntakeSubmittedAt),
            Notes = row.Notes?.Trim() ?? string.Empty
        };
    }

    private static string BuildClinicianName(string? firstName, string? lastName)
    {
        var fullName = string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(fullName) ? "Assigned Clinician" : $"Dr. {fullName}";
    }

    private static string MapAppointmentType(AppointmentType appointmentType) =>
        appointmentType switch
        {
            AppointmentType.InitialEvaluation => "Initial Evaluation",
            AppointmentType.FollowUp => "Follow-up",
            AppointmentType.Discharge => "Discharge",
            _ => "Follow-up"
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
        public AppointmentStatus AppointmentStatus { get; init; }
        public string? Notes { get; init; }
        public DateTime? IntakeSubmittedAt { get; init; }
        public bool HasIntake { get; init; }
    }

    private sealed class AppointmentConflictRow
    {
        public DateTime StartTimeUtc { get; init; }
        public DateTime EndTimeUtc { get; init; }
    }
}
