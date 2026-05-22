using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Dashboard;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Dashboard;

public static class DashboardEndpoints
{
    private const int DefaultTake = 10;
    private const int MaxTake = 50;

    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dashboard")
            .WithTags("Dashboard")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        group.MapGet("/alerts", GetAlerts)
            .WithName("GetDashboardAlerts")
            .WithSummary("Get live clinical dashboard alerts for notes and intake follow-up");

        group.MapGet("/snapshot", GetSnapshot)
            .WithName("GetDashboardSnapshot")
            .WithSummary("Get the clinical dashboard overview, alerts, notes, and activity snapshot");
    }

    private static async Task<IResult> GetAlerts(
        [FromQuery] int? take,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take.GetValueOrDefault(DefaultTake), 1, MaxTake);
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.Date;
        var tomorrow = today.AddDays(1);
        var orderedAlerts = await BuildVisibleAlertsAsync(db, requestedTake, today, tomorrow, now, cancellationToken);

        return Results.Ok(new DashboardAlertsResponse
        {
            Alerts = orderedAlerts,
            UrgentCount = orderedAlerts.Count(alert => alert.IsUrgent),
            GeneratedAtUtc = now
        });
    }

    private static async Task<IResult> GetSnapshot(
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.Date;
        var tomorrow = today.AddDays(1);

        var overview = await BuildOverviewCountsAsync(db, today, tomorrow, cancellationToken);
        var alerts = await BuildVisibleAlertsAsync(db, DefaultTake, today, tomorrow, now, cancellationToken);
        var urgentAlertCount = await CountUrgentAlertsAsync(db, today, now, overview.NotesDueToday, cancellationToken);
        var recentNotes = await BuildRecentNotesAsync(db, cancellationToken);
        var recentActivities = await BuildRecentActivitiesAsync(db, today, tomorrow, cancellationToken);

        return Results.Ok(new DashboardSnapshotResponse
        {
            Overview = overview,
            Alerts = alerts,
            UrgentAlertCount = urgentAlertCount,
            TotalAlertCount = overview.PendingItems,
            RecentNotes = recentNotes,
            RecentActivities = recentActivities,
            GeneratedAtUtc = now
        });
    }

    private static async Task<List<DashboardAlertItemResponse>> BuildVisibleAlertsAsync(
        ApplicationDbContext db,
        int take,
        DateTime today,
        DateTime tomorrow,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take, 1, MaxTake);
        var alerts = new List<DashboardAlertItemResponse>();

        alerts.AddRange(await BuildUnsignedNoteAlertsAsync(db, today, now, cancellationToken));
        alerts.AddRange(await BuildIncompleteIntakeAlertsAsync(db, now, cancellationToken));
        alerts.AddRange(await BuildNotesDueTodayAlertsAsync(db, today, tomorrow, cancellationToken));

        return alerts
            .OrderBy(alert => PriorityRank(alert.Priority))
            .ThenBy(alert => alert.DueDateUtc ?? alert.Timestamp.UtcDateTime)
            .ThenBy(alert => KindRank(alert.Kind))
            .ThenBy(alert => alert.Id, StringComparer.Ordinal)
            .Take(requestedTake)
            .ToList();
    }

    private static async Task<DashboardOverviewCountsResponse> BuildOverviewCountsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        CancellationToken cancellationToken)
    {
        var appointmentRows = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.StartTimeUtc >= today &&
                appointment.StartTimeUtc < tomorrow &&
                appointment.Patient != null &&
                !appointment.Patient.IsArchived)
            .Select(appointment => new
            {
                appointment.Id,
                appointment.PatientId,
                appointment.Status
            })
            .ToListAsync(cancellationToken);

        var activeAppointmentRows = appointmentRows
            .Where(appointment =>
                appointment.Status == AppointmentStatus.CheckedIn ||
                appointment.Status == AppointmentStatus.InProgress ||
                appointment.Status == AppointmentStatus.Completed)
            .ToList();

        var activeAppointmentIds = activeAppointmentRows.Select(appointment => appointment.Id).ToHashSet();
        var activeAppointmentPatientIds = activeAppointmentRows.Select(appointment => appointment.PatientId).ToHashSet();

        var relatedTodayNotes = await db.ClinicalNotes
            .AsNoTracking()
            .Where(note =>
                !note.IsAddendum &&
                ((note.AppointmentId.HasValue && activeAppointmentIds.Contains(note.AppointmentId.Value)) ||
                 (activeAppointmentPatientIds.Contains(note.PatientId) &&
                  note.DateOfService >= today &&
                  note.DateOfService < tomorrow)))
            .Select(note => new RelatedNoteCandidate(
                note.PatientId,
                note.AppointmentId,
                note.DateOfService))
            .ToListAsync(cancellationToken);

        var notesDueToday = activeAppointmentRows.Count(appointment =>
            !HasAnyNoteForAppointmentOrDate(
                new AppointmentAlertCandidate(appointment.Id, appointment.PatientId, string.Empty, string.Empty, null, today),
                relatedTodayNotes,
                today,
                tomorrow));

        var draftNotes = await db.ClinicalNotes
            .AsNoTracking()
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus == NoteStatus.Draft &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var unsignedNotes = await db.ClinicalNotes
            .AsNoTracking()
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var incompleteIntakes = await db.IntakeForms
            .AsNoTracking()
            .CountAsync(intake =>
                !intake.IsLocked &&
                intake.SubmittedAt == null &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        return new DashboardOverviewCountsResponse
        {
            PatientsToday = appointmentRows.Select(appointment => appointment.PatientId).Distinct().Count(),
            AppointmentsToday = appointmentRows.Count,
            NotesDueToday = notesDueToday,
            PendingItems = notesDueToday + unsignedNotes + incompleteIntakes,
            DraftNotes = draftNotes,
            UnsignedNotes = unsignedNotes,
            IncompleteIntakes = incompleteIntakes
        };
    }

    private static async Task<int> CountUrgentAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        int notesDueToday,
        CancellationToken cancellationToken)
    {
        var staleIntakeThreshold = now.UtcDateTime.AddHours(-24);

        var overdueUnsignedNotes = await db.ClinicalNotes
            .AsNoTracking()
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.DateOfService < today &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var staleIncompleteIntakes = await db.IntakeForms
            .AsNoTracking()
            .CountAsync(intake =>
                !intake.IsLocked &&
                intake.SubmittedAt == null &&
                intake.LastModifiedUtc <= staleIntakeThreshold &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        return notesDueToday + overdueUnsignedNotes + staleIncompleteIntakes;
    }

    private static async Task<IReadOnlyList<NoteListItemApiResponse>> BuildRecentNotesAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        return await db.ClinicalNotes
            .AsNoTracking()
            .Where(note => !note.IsAddendum && note.Patient != null && !note.Patient.IsArchived)
            .OrderByDescending(note => note.LastModifiedUtc)
            .ThenByDescending(note => note.DateOfService)
            .Take(5)
            .Select(note => new NoteListItemApiResponse
            {
                Id = note.Id,
                PatientId = note.PatientId,
                PatientName = note.Patient != null
                    ? note.Patient.FirstName + " " + note.Patient.LastName
                    : string.Empty,
                NoteType = note.NoteType.ToString(),
                IsSigned = note.NoteStatus == NoteStatus.Signed,
                NoteStatus = note.NoteStatus,
                DateOfService = note.DateOfService,
                LastModifiedUtc = note.LastModifiedUtc,
                CptCodesJson = note.CptCodesJson
            })
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<DashboardRecentActivityResponse>> BuildRecentActivitiesAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        CancellationToken cancellationToken)
    {
        var notes = await db.ClinicalNotes
            .AsNoTracking()
            .Where(note => !note.IsAddendum && note.Patient != null && !note.Patient.IsArchived)
            .OrderByDescending(note => note.LastModifiedUtc)
            .Take(20)
            .Select(note => new
            {
                note.Id,
                note.PatientId,
                PatientName = note.Patient != null
                    ? note.Patient.FirstName + " " + note.Patient.LastName
                    : string.Empty,
                note.NoteType,
                note.NoteStatus,
                note.LastModifiedUtc
            })
            .ToListAsync(cancellationToken);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(appointment =>
                appointment.StartTimeUtc >= today &&
                appointment.StartTimeUtc < tomorrow &&
                appointment.Patient != null &&
                !appointment.Patient.IsArchived)
            .OrderBy(appointment => appointment.StartTimeUtc)
            .Select(appointment => new
            {
                appointment.Id,
                appointment.PatientId,
                PatientName = appointment.Patient != null
                    ? appointment.Patient.FirstName + " " + appointment.Patient.LastName
                    : string.Empty,
                appointment.AppointmentType,
                appointment.Status,
                appointment.StartTimeUtc
            })
            .ToListAsync(cancellationToken);

        var noteActivities = notes.Select(note => new DashboardRecentActivityResponse
        {
            Id = note.Id.ToString("N"),
            Type = note.NoteStatus == NoteStatus.Signed
                ? ActivityType.NoteCompleted.ToString()
                : ActivityType.NoteUpdated.ToString(),
            Description = note.NoteStatus == NoteStatus.Signed
                ? $"Signed {note.NoteType}"
                : $"Updated {note.NoteType}",
            PatientId = note.PatientId.ToString("D"),
            PatientName = note.PatientName,
            Timestamp = note.LastModifiedUtc
        });

        var appointmentActivities = appointments.Select(appointment => new DashboardRecentActivityResponse
        {
            Id = appointment.Id.ToString("N"),
            Type = appointment.Status == AppointmentStatus.CheckedIn
                ? ActivityType.AppointmentCheckedIn.ToString()
                : ActivityType.AppointmentScheduled.ToString(),
            Description = appointment.Status == AppointmentStatus.CheckedIn
                ? $"Checked in {appointment.AppointmentType} appointment"
                : $"Scheduled {appointment.AppointmentType} appointment",
            PatientId = appointment.PatientId.ToString("D"),
            PatientName = appointment.PatientName,
            Timestamp = appointment.StartTimeUtc
        });

        return noteActivities
            .Concat(appointmentActivities)
            .OrderByDescending(activity => activity.Timestamp)
            .Take(10)
            .ToList();
    }

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildNotesDueTodayAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        CancellationToken cancellationToken)
    {
        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Patient)
            .Where(appointment =>
                appointment.StartTimeUtc >= today &&
                appointment.StartTimeUtc < tomorrow &&
                (appointment.Status == AppointmentStatus.CheckedIn ||
                 appointment.Status == AppointmentStatus.InProgress ||
                 appointment.Status == AppointmentStatus.Completed) &&
                appointment.Patient != null &&
                !appointment.Patient.IsArchived)
            .Select(appointment => new AppointmentAlertCandidate(
                appointment.Id,
                appointment.PatientId,
                appointment.Patient!.FirstName,
                appointment.Patient.LastName,
                appointment.Patient.MedicalRecordNumber,
                appointment.StartTimeUtc))
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0)
        {
            return Array.Empty<DashboardAlertItemResponse>();
        }

        var appointmentIds = appointments.Select(appointment => appointment.Id).ToHashSet();
        var patientIds = appointments.Select(appointment => appointment.PatientId).ToHashSet();

        var relatedNotes = await db.ClinicalNotes
            .AsNoTracking()
            .Where(note =>
                !note.IsAddendum &&
                ((note.AppointmentId.HasValue && appointmentIds.Contains(note.AppointmentId.Value)) ||
                 (patientIds.Contains(note.PatientId) &&
                  note.DateOfService >= today &&
                  note.DateOfService < tomorrow)))
            .Select(note => new RelatedNoteCandidate(
                note.PatientId,
                note.AppointmentId,
                note.DateOfService))
            .ToListAsync(cancellationToken);

        return appointments
            .Where(appointment => !HasAnyNoteForAppointmentOrDate(appointment, relatedNotes, today, tomorrow))
            .Select(appointment => new DashboardAlertItemResponse
            {
                Id = $"notesDueToday:{appointment.Id:N}",
                Kind = DashboardAlertKinds.NotesDueToday,
                Priority = DashboardAlertPriorities.High,
                Title = "Note Due Today",
                Message = "Today's appointment needs a signed note.",
                PatientId = appointment.PatientId,
                PatientName = FormatPatientName(appointment.PatientFirstName, appointment.PatientLastName),
                PatientMedicalRecordNumber = appointment.PatientMedicalRecordNumber,
                Timestamp = ToUtcOffset(appointment.StartTimeUtc),
                DueDateUtc = appointment.StartTimeUtc,
                TargetUrl = $"/patient/{appointment.PatientId:D}/new-note",
                ActionLabel = "Start",
                IsUrgent = true
            })
            .ToList();
    }

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildIncompleteIntakeAlertsAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var staleThreshold = now.UtcDateTime.AddHours(-24);

        var intakes = await db.IntakeForms
            .AsNoTracking()
            .Include(intake => intake.Patient)
            .Where(intake =>
                !intake.IsLocked &&
                intake.SubmittedAt == null &&
                intake.Patient != null &&
                !intake.Patient.IsArchived)
            .OrderBy(intake => intake.LastModifiedUtc)
            .Take(MaxTake * 4)
            .Select(intake => new IntakeAlertCandidate(
                intake.Id,
                intake.PatientId,
                intake.Patient!.FirstName,
                intake.Patient.LastName,
                intake.Patient.MedicalRecordNumber,
                intake.LastModifiedUtc))
            .ToListAsync(cancellationToken);

        return intakes.Select(intake =>
        {
            var isUrgent = intake.LastModifiedUtc <= staleThreshold;
            return new DashboardAlertItemResponse
            {
                Id = $"incompleteIntake:{intake.Id:N}",
                Kind = DashboardAlertKinds.IncompleteIntake,
                Priority = isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
                Title = "Incomplete Intake Form",
                Message = "Patient has not completed intake form.",
                PatientId = intake.PatientId,
                PatientName = FormatPatientName(intake.PatientFirstName, intake.PatientLastName),
                PatientMedicalRecordNumber = intake.PatientMedicalRecordNumber,
                Timestamp = ToUtcOffset(intake.LastModifiedUtc),
                TargetUrl = $"/intake/{intake.PatientId:D}",
                ActionLabel = "Open Intake",
                IsUrgent = isUrgent
            };
        }).ToList();
    }

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildUnsignedNoteAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var notes = await db.ClinicalNotes
            .AsNoTracking()
            .Include(note => note.Patient)
            .Where(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.Patient != null &&
                !note.Patient.IsArchived)
            .OrderBy(note => note.DateOfService)
            .ThenBy(note => note.LastModifiedUtc)
            .Take(MaxTake * 4)
            .Select(note => new NoteAlertCandidate(
                note.Id,
                note.PatientId,
                note.Patient!.FirstName,
                note.Patient.LastName,
                note.Patient.MedicalRecordNumber,
                note.NoteStatus,
                note.NoteType,
                note.DateOfService,
                note.LastModifiedUtc))
            .ToListAsync(cancellationToken);

        return notes.Select(note =>
        {
            var isUrgent = note.DateOfService.Date < today;
            return new DashboardAlertItemResponse
            {
                Id = $"unsignedNote:{note.Id:N}",
                Kind = DashboardAlertKinds.UnsignedNote,
                Priority = isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
                Title = note.NoteStatus == NoteStatus.PendingCoSign ? "Co-sign Needed" : "Unsigned Note",
                Message = BuildUnsignedNoteMessage(note, today, now),
                PatientId = note.PatientId,
                PatientName = FormatPatientName(note.PatientFirstName, note.PatientLastName),
                PatientMedicalRecordNumber = note.PatientMedicalRecordNumber,
                Timestamp = ToUtcOffset(note.LastModifiedUtc),
                DueDateUtc = note.DateOfService,
                TargetUrl = $"/patient/{note.PatientId:D}/note/{note.Id:D}",
                ActionLabel = note.NoteStatus == NoteStatus.PendingCoSign ? "Review" : "Open Note",
                IsUrgent = isUrgent
            };
        }).ToList();
    }

    private static bool HasAnyNoteForAppointmentOrDate(
        AppointmentAlertCandidate appointment,
        IEnumerable<RelatedNoteCandidate> relatedNotes,
        DateTime today,
        DateTime tomorrow) =>
        relatedNotes.Any(note =>
            note.AppointmentId == appointment.Id ||
            (note.PatientId == appointment.PatientId &&
             note.DateOfService >= today &&
             note.DateOfService < tomorrow));

    private static string BuildUnsignedNoteMessage(
        NoteAlertCandidate note,
        DateTime today,
        DateTimeOffset now)
    {
        var noteType = FormatEnumLabel(note.NoteType.ToString());
        if (note.NoteStatus == NoteStatus.PendingCoSign)
        {
            return $"{noteType} is pending co-signature.";
        }

        if (note.DateOfService.Date < today)
        {
            var days = Math.Max(1, (today - note.DateOfService.Date).Days);
            return days == 1
                ? $"{noteType} note was due yesterday."
                : $"{noteType} note is {days} days overdue.";
        }

        if (note.DateOfService.Date == today)
        {
            return $"{noteType} note is due today.";
        }

        var hoursSinceUpdate = Math.Max(1, (int)(now - ToUtcOffset(note.LastModifiedUtc)).TotalHours);
        return $"{noteType} note is still unsigned after {hoursSinceUpdate}h.";
    }

    private static int PriorityRank(string priority) => priority switch
    {
        DashboardAlertPriorities.High => 0,
        DashboardAlertPriorities.Medium => 1,
        _ => 2
    };

    private static int KindRank(string kind) => kind switch
    {
        DashboardAlertKinds.NotesDueToday => 0,
        DashboardAlertKinds.UnsignedNote => 1,
        DashboardAlertKinds.IncompleteIntake => 2,
        _ => 3
    };

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utc);
    }

    private static string FormatPatientName(string firstName, string lastName)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "Unknown patient" : fullName;
    }

    private static string FormatEnumLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Clinical";
        }

        var builder = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (i > 0 && char.IsUpper(current) && !char.IsWhiteSpace(value[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static class DashboardAlertKinds
    {
        public const string NotesDueToday = "notesDueToday";
        public const string IncompleteIntake = "incompleteIntake";
        public const string UnsignedNote = "unsignedNote";
    }

    private static class DashboardAlertPriorities
    {
        public const string High = "high";
        public const string Medium = "medium";
    }

    private sealed record AppointmentAlertCandidate(
        Guid Id,
        Guid PatientId,
        string PatientFirstName,
        string PatientLastName,
        string? PatientMedicalRecordNumber,
        DateTime StartTimeUtc);

    private sealed record IntakeAlertCandidate(
        Guid Id,
        Guid PatientId,
        string PatientFirstName,
        string PatientLastName,
        string? PatientMedicalRecordNumber,
        DateTime LastModifiedUtc);

    private sealed record NoteAlertCandidate(
        Guid Id,
        Guid PatientId,
        string PatientFirstName,
        string PatientLastName,
        string? PatientMedicalRecordNumber,
        NoteStatus NoteStatus,
        NoteType NoteType,
        DateTime DateOfService,
        DateTime LastModifiedUtc);

    private sealed record RelatedNoteCandidate(
        Guid PatientId,
        Guid? AppointmentId,
        DateTime DateOfService);
}
