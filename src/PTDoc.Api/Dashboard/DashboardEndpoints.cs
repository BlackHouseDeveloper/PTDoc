using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Dashboard;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Globalization;
using System.Text.Json;

namespace PTDoc.Api.Dashboard;

public static class DashboardEndpoints
{
    private const int DefaultTake = 10;
    private const int MaxTake = 50;
    private const int AuthorizationAlertWindowDays = 30;
    private const int AuthorizationUrgentWindowDays = 7;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take.GetValueOrDefault(DefaultTake), 1, MaxTake);
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.Date;
        var tomorrow = today.AddDays(1);
        var visibility = BuildVisibilityContext(identityContext);
        var orderedAlerts = await BuildVisibleAlertsAsync(db, requestedTake, today, tomorrow, now, visibility, cancellationToken);

        return Results.Ok(new DashboardAlertsResponse
        {
            Alerts = orderedAlerts,
            UrgentCount = orderedAlerts.Count(alert => alert.IsUrgent),
            GeneratedAtUtc = now
        });
    }

    private static async Task<IResult> GetSnapshot(
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.Date;
        var tomorrow = today.AddDays(1);
        var visibility = BuildVisibilityContext(identityContext);

        var overview = await BuildOverviewCountsAsync(db, today, tomorrow, now, visibility, cancellationToken);
        var alerts = await BuildVisibleAlertsAsync(db, DefaultTake, today, tomorrow, now, visibility, cancellationToken);
        alerts = await EnsureAuthorizationAlertIncludedAsync(
            db,
            alerts,
            today,
            now,
            visibility,
            overview.AuthorizationActionItems,
            cancellationToken);
        var urgentAlertCount = await CountUrgentAlertsAsync(db, today, now, visibility, overview.NotesDueToday, cancellationToken);
        var recentNotes = await BuildRecentNotesAsync(db, visibility, cancellationToken);
        var recentPlansOfCare = await BuildRecentPlansOfCareAsync(db, visibility, cancellationToken);
        var recentActivities = await BuildRecentActivitiesAsync(db, today, tomorrow, visibility, cancellationToken);

        return Results.Ok(new DashboardSnapshotResponse
        {
            Overview = overview,
            Alerts = alerts,
            UrgentAlertCount = urgentAlertCount,
            TotalAlertCount = overview.PendingItems,
            RecentNotes = recentNotes,
            RecentPlansOfCare = recentPlansOfCare,
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
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var requestedTake = Math.Clamp(take, 1, MaxTake);
        var alerts = new List<DashboardAlertItemResponse>();

        alerts.AddRange(await BuildUnsignedNoteAlertsAsync(db, today, now, visibility, cancellationToken));
        alerts.AddRange(await BuildSubmittedIntakeReviewAlertsAsync(db, now, visibility, cancellationToken));
        alerts.AddRange(await BuildIncompleteIntakeAlertsAsync(db, now, visibility, cancellationToken));
        alerts.AddRange(await BuildNotesDueTodayAlertsAsync(db, today, tomorrow, visibility, cancellationToken));
        alerts.AddRange(await BuildAuthorizationAlertsAsync(db, today, now, visibility, cancellationToken));

        return OrderAlerts(alerts)
            .Take(requestedTake)
            .ToList();
    }

    private static async Task<List<DashboardAlertItemResponse>> EnsureAuthorizationAlertIncludedAsync(
        ApplicationDbContext db,
        List<DashboardAlertItemResponse> alerts,
        DateTime today,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        int authorizationActionItems,
        CancellationToken cancellationToken)
    {
        if (authorizationActionItems <= 0 || alerts.Any(alert => IsAuthorizationAlertKind(alert.Kind)))
        {
            return alerts;
        }

        var authorizationAlert = (await BuildAuthorizationAlertsAsync(db, today, now, visibility, cancellationToken))
            .OrderBy(alert => PriorityRank(alert.Priority))
            .ThenBy(alert => KindRank(alert.Kind))
            .ThenBy(alert => alert.DueDateUtc ?? alert.Timestamp.UtcDateTime)
            .ThenBy(alert => alert.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (authorizationAlert is null)
        {
            return alerts;
        }

        if (alerts.Count >= DefaultTake)
        {
            alerts[^1] = authorizationAlert;
            return OrderAlerts(alerts)
                .Take(DefaultTake)
                .ToList();
        }

        alerts.Add(authorizationAlert);
        return OrderAlerts(alerts)
            .Take(DefaultTake)
            .ToList();
    }

    private static async Task<DashboardOverviewCountsResponse> BuildOverviewCountsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var appointmentQuery = ApplyAppointmentVisibility(db.Appointments.AsNoTracking(), visibility);
        var appointmentRows = await appointmentQuery
            .Where(appointment =>
                appointment.StartTimeUtc >= today &&
                appointment.StartTimeUtc < tomorrow &&
                appointment.Patient != null &&
                !appointment.Patient.IsArchived)
            .Select(appointment => new
            {
                appointment.Id,
                appointment.PatientId,
                appointment.ClinicalId,
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

        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var relatedTodayNotes = await noteQuery
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

        var draftNotes = await noteQuery
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus == NoteStatus.Draft &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var unsignedNotes = await noteQuery
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var intakeQuery = ApplyIntakeVisibility(db.IntakeForms.AsNoTracking(), db, visibility);
        var incompleteIntakes = await intakeQuery
            .CountAsync(intake =>
                !intake.IsLocked &&
                intake.SubmittedAt == null &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        var submittedIntakesAwaitingReview = await intakeQuery
            .CountAsync(intake =>
                intake.IsLocked &&
                intake.SubmittedAt != null &&
                intake.ReviewedAtUtc == null &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        var authorizationActionItems = await CountAuthorizationAlertsAsync(db, today, now, visibility, cancellationToken);

        return new DashboardOverviewCountsResponse
        {
            PatientsToday = appointmentRows.Select(appointment => appointment.PatientId).Distinct().Count(),
            AppointmentsToday = appointmentRows.Count,
            NotesDueToday = notesDueToday,
            PendingItems = notesDueToday + unsignedNotes + incompleteIntakes + submittedIntakesAwaitingReview + authorizationActionItems,
            AuthorizationActionItems = authorizationActionItems,
            DraftNotes = draftNotes,
            UnsignedNotes = unsignedNotes,
            IncompleteIntakes = incompleteIntakes,
            SubmittedIntakesAwaitingReview = submittedIntakesAwaitingReview
        };
    }

    private static async Task<int> CountUrgentAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        int notesDueToday,
        CancellationToken cancellationToken)
    {
        var staleIntakeThreshold = now.UtcDateTime.AddHours(-24);

        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var overdueUnsignedNotes = await noteQuery
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.DateOfService < today &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var intakeQuery = ApplyIntakeVisibility(db.IntakeForms.AsNoTracking(), db, visibility);
        var staleIncompleteIntakes = await intakeQuery
            .CountAsync(intake =>
                !intake.IsLocked &&
                intake.SubmittedAt == null &&
                intake.LastModifiedUtc <= staleIntakeThreshold &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        var staleSubmittedIntakesAwaitingReview = await intakeQuery
            .CountAsync(intake =>
                intake.IsLocked &&
                intake.SubmittedAt != null &&
                intake.ReviewedAtUtc == null &&
                intake.SubmittedAt <= staleIntakeThreshold &&
                intake.Patient != null &&
                !intake.Patient.IsArchived,
                cancellationToken);

        var urgentAuthorizationAlerts = (await BuildAuthorizationAlertsAsync(db, today, now, visibility, cancellationToken))
            .Count(alert => alert.IsUrgent);

        return notesDueToday + overdueUnsignedNotes + staleIncompleteIntakes + staleSubmittedIntakesAwaitingReview + urgentAuthorizationAlerts;
    }

    private static async Task<IReadOnlyList<NoteListItemApiResponse>> BuildRecentNotesAsync(
        ApplicationDbContext db,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        return await noteQuery
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

    private static async Task<IReadOnlyList<DashboardPlanOfCareSummaryResponse>> BuildRecentPlansOfCareAsync(
        ApplicationDbContext db,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var notes = await noteQuery
            .Where(note =>
                !note.IsAddendum &&
                (note.NoteType == NoteType.Evaluation || note.NoteType == NoteType.ProgressNote) &&
                note.Patient != null &&
                !note.Patient.IsArchived)
            .OrderByDescending(note => note.LastModifiedUtc)
            .ThenByDescending(note => note.DateOfService)
            .Take(5)
            .Select(note => new PlanOfCareNoteCandidate(
                note.Id,
                note.PatientId,
                note.Patient != null ? note.Patient.FirstName + " " + note.Patient.LastName : string.Empty,
                note.NoteType,
                note.NoteStatus,
                note.IsReEvaluation,
                note.DateOfService,
                note.LastModifiedUtc,
                note.ModifiedByUserId,
                note.ContentJson,
                note.CptCodesJson))
            .ToListAsync(cancellationToken);

        var editorIds = notes
            .Select(note => note.ModifiedByUserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var editorLookup = editorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Users
                .AsNoTracking()
                .Where(user => editorIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Name = (user.FirstName + " " + user.LastName).Trim()
                })
                .ToDictionaryAsync(user => user.Id, user => user.Name, cancellationToken);

        return notes.Select(note =>
        {
            var payload = TryDeserializeWorkspacePayload(note.ContentJson);
            var icdCount = payload?.Assessment?.DiagnosisCodes?.Count;
            var cptUnits = TryCountCptUnits(note.CptCodesJson);
            int? targetVisits = TryGetSingleValue(payload?.Plan?.TreatmentFrequencyDaysPerWeek) is { } daysPerWeek &&
                               TryGetSingleValue(payload?.Plan?.TreatmentDurationWeeks) is { } durationWeeks
                ? daysPerWeek * durationWeeks
                : null;

            return new DashboardPlanOfCareSummaryResponse
            {
                Id = note.Id,
                PatientId = note.PatientId,
                PatientName = string.IsNullOrWhiteSpace(note.PatientName)
                    ? "Unknown patient"
                    : note.PatientName,
                Title = BuildPlanOfCareTitle(note),
                Status = ToPlanOfCareStatus(note.NoteStatus),
                LastEditedAt = note.LastModifiedUtc,
                LastEditedBy = editorLookup.TryGetValue(note.ModifiedByUserId, out var editorName) && !string.IsNullOrWhiteSpace(editorName)
                    ? editorName
                    : null,
                TargetUrl = $"/patient/{note.PatientId:D}/note/{note.Id:D}",
                WeekTotal = targetVisits.HasValue ? TryGetSingleValue(payload?.Plan?.TreatmentDurationWeeks) : null,
                VisitsTotal = targetVisits,
                Sessions = cptUnits,
                IcdCount = icdCount
            };
        }).ToList();
    }

    private static async Task<IReadOnlyList<DashboardRecentActivityResponse>> BuildRecentActivitiesAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var notes = await noteQuery
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

        var appointmentQuery = ApplyAppointmentVisibility(db.Appointments.AsNoTracking(), visibility);
        var appointments = await appointmentQuery
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
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var appointmentQuery = ApplyAppointmentVisibility(db.Appointments.AsNoTracking(), visibility);
        var appointments = await appointmentQuery
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

        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var relatedNotes = await noteQuery
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
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var staleThreshold = now.UtcDateTime.AddHours(-24);

        var intakeQuery = ApplyIntakeVisibility(db.IntakeForms.AsNoTracking(), db, visibility);
        var intakes = await intakeQuery
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

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildSubmittedIntakeReviewAlertsAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var staleThreshold = now.UtcDateTime.AddHours(-24);

        var intakeQuery = ApplyIntakeVisibility(db.IntakeForms.AsNoTracking(), db, visibility);
        var intakes = await intakeQuery
            .Include(intake => intake.Patient)
            .Where(intake =>
                intake.IsLocked &&
                intake.SubmittedAt != null &&
                intake.ReviewedAtUtc == null &&
                intake.Patient != null &&
                !intake.Patient.IsArchived)
            .OrderBy(intake => intake.SubmittedAt)
            .ThenBy(intake => intake.LastModifiedUtc)
            .Take(MaxTake * 4)
            .Select(intake => new SubmittedIntakeReviewAlertCandidate(
                intake.Id,
                intake.PatientId,
                intake.Patient!.FirstName,
                intake.Patient.LastName,
                intake.Patient.MedicalRecordNumber,
                intake.SubmittedAt!.Value))
            .ToListAsync(cancellationToken);

        return intakes.Select(intake =>
        {
            var isUrgent = intake.SubmittedAtUtc <= staleThreshold;
            return new DashboardAlertItemResponse
            {
                Id = $"submittedIntakeReview:{intake.Id:N}",
                Kind = DashboardAlertKinds.SubmittedIntakeReview,
                Priority = isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
                Title = "Intake Review Needed",
                Message = "Patient intake is submitted and awaiting clinician review.",
                PatientId = intake.PatientId,
                PatientName = FormatPatientName(intake.PatientFirstName, intake.PatientLastName),
                PatientMedicalRecordNumber = intake.PatientMedicalRecordNumber,
                Timestamp = ToUtcOffset(intake.SubmittedAtUtc),
                DueDateUtc = intake.SubmittedAtUtc,
                TargetUrl = $"/intake/{intake.PatientId:D}",
                ActionLabel = "Review",
                IsUrgent = isUrgent
            };
        }).ToList();
    }

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildUnsignedNoteAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var noteQuery = ApplyNoteVisibility(db.ClinicalNotes.AsNoTracking(), visibility);
        var notes = await noteQuery
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

    private static async Task<int> CountAuthorizationAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken) =>
        (await BuildAuthorizationAlertsAsync(db, today, now, visibility, cancellationToken)).Count;

    private static async Task<IReadOnlyList<DashboardAlertItemResponse>> BuildAuthorizationAlertsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTimeOffset now,
        DashboardVisibilityContext visibility,
        CancellationToken cancellationToken)
    {
        var patientQuery = ApplyPatientVisibility(db.Patients.AsNoTracking(), db, visibility);
        var patients = await patientQuery
            .Where(patient =>
                !patient.IsArchived &&
                !string.IsNullOrWhiteSpace(patient.PayerInfoJson) &&
                patient.PayerInfoJson != "{}" &&
                (EF.Functions.Like(patient.PayerInfoJson, "%authorizationStatus%") ||
                 EF.Functions.Like(patient.PayerInfoJson, "%authorizationEndDate%") ||
                 EF.Functions.Like(patient.PayerInfoJson, "%reAuthorizationDueDate%") ||
                 EF.Functions.Like(patient.PayerInfoJson, "%visitsRemaining%") ||
                 EF.Functions.Like(patient.PayerInfoJson, "%visitAlertThreshold%")))
            .Select(patient => new PatientAuthorizationAlertCandidate(
                patient.Id,
                patient.FirstName,
                patient.LastName,
                patient.MedicalRecordNumber,
                patient.LastModifiedUtc,
                patient.PayerInfoJson))
            .ToListAsync(cancellationToken);

        var alerts = new List<DashboardAlertItemResponse>();
        foreach (var patient in patients)
        {
            if (TryDeserializePayerAuthorization(patient.PayerInfoJson) is not { } payer)
            {
                continue;
            }

            AddAuthorizationStatusAlert(alerts, patient, payer, now);
            AddAuthorizationDateAlert(
                alerts,
                patient,
                ReadJsonScalar(payer.AuthorizationEndDate),
                today,
                now,
                DashboardAlertKinds.AuthorizationExpiration,
                "Authorization Expired",
                "Authorization Expiring",
                "Authorization coverage has expired.",
                "Authorization coverage is nearing its end date.");
            AddAuthorizationDateAlert(
                alerts,
                patient,
                ReadJsonScalar(payer.ReAuthorizationDueDate),
                today,
                now,
                DashboardAlertKinds.AuthorizationReauthorizationDue,
                "Re-Authorization Overdue",
                "Re-Authorization Due",
                "Re-authorization is overdue.",
                "Re-authorization is due soon.");
            AddAuthorizationVisitLimitAlert(alerts, patient, payer, now);
        }

        return alerts;
    }

    private static void AddAuthorizationStatusAlert(
        List<DashboardAlertItemResponse> alerts,
        PatientAuthorizationAlertCandidate patient,
        PatientAuthorizationInfo payer,
        DateTimeOffset now)
    {
        var normalizedStatus = NormalizeStatus(ReadJsonScalar(payer.AuthorizationStatus));
        if (string.IsNullOrWhiteSpace(normalizedStatus) || normalizedStatus == "active")
        {
            return;
        }

        var (title, message, isUrgent) = normalizedStatus switch
        {
            "denied" => ("Authorization Denied", "Authorization is marked denied and needs follow-up.", true),
            "expired" => ("Authorization Expired", "Authorization is marked expired and needs follow-up.", true),
            "pending" => ("Authorization Pending", "Authorization is pending and needs follow-up before care continues.", false),
            _ => ("Authorization Needs Review", "Authorization status needs review.", false)
        };

        alerts.Add(BuildAuthorizationAlert(
            patient,
            $"{DashboardAlertKinds.AuthorizationStatus}:{patient.PatientId:N}:{normalizedStatus}",
            DashboardAlertKinds.AuthorizationStatus,
            isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
            title,
            message,
            now,
            dueDateUtc: null,
            isUrgent));
    }

    private static void AddAuthorizationDateAlert(
        List<DashboardAlertItemResponse> alerts,
        PatientAuthorizationAlertCandidate patient,
        string? rawDate,
        DateTime today,
        DateTimeOffset now,
        string kind,
        string overdueTitle,
        string dueSoonTitle,
        string overdueMessage,
        string dueSoonMessage)
    {
        if (!TryParseDate(rawDate, out var dueDate))
        {
            return;
        }

        var dueDateOnly = dueDate.Date;
        var daysUntilDue = (dueDateOnly - today).Days;
        if (daysUntilDue > AuthorizationAlertWindowDays)
        {
            return;
        }

        var isOverdue = daysUntilDue < 0;
        var isUrgent = isOverdue || daysUntilDue <= AuthorizationUrgentWindowDays;
        var message = isOverdue
            ? $"{overdueMessage} Due date: {dueDateOnly:MMM d, yyyy}."
            : $"{dueSoonMessage} Due date: {dueDateOnly:MMM d, yyyy}.";

        alerts.Add(BuildAuthorizationAlert(
            patient,
            $"{kind}:{patient.PatientId:N}:{dueDateOnly:yyyyMMdd}",
            kind,
            isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
            isOverdue ? overdueTitle : dueSoonTitle,
            message,
            now,
            dueDateOnly,
            isUrgent));
    }

    private static void AddAuthorizationVisitLimitAlert(
        List<DashboardAlertItemResponse> alerts,
        PatientAuthorizationAlertCandidate patient,
        PatientAuthorizationInfo payer,
        DateTimeOffset now)
    {
        if (!TryParseInteger(ReadJsonScalar(payer.VisitsRemaining), out var visitsRemaining) ||
            !TryParseInteger(ReadJsonScalar(payer.VisitAlertThreshold), out var alertThreshold))
        {
            return;
        }

        if (alertThreshold < 0 || visitsRemaining > alertThreshold)
        {
            return;
        }

        var isUrgent = visitsRemaining <= 0;
        var message = visitsRemaining <= 0
            ? "No authorized visits remain. Review authorization before additional visits."
            : $"{visitsRemaining} authorized visit{(visitsRemaining == 1 ? string.Empty : "s")} remain; threshold is {alertThreshold}.";

        alerts.Add(BuildAuthorizationAlert(
            patient,
            $"{DashboardAlertKinds.AuthorizationVisitLimit}:{patient.PatientId:N}:{visitsRemaining}:{alertThreshold}",
            DashboardAlertKinds.AuthorizationVisitLimit,
            isUrgent ? DashboardAlertPriorities.High : DashboardAlertPriorities.Medium,
            isUrgent ? "Authorization Visits Exhausted" : "Authorization Visit Limit",
            message,
            now,
            dueDateUtc: null,
            isUrgent));
    }

    private static DashboardAlertItemResponse BuildAuthorizationAlert(
        PatientAuthorizationAlertCandidate patient,
        string id,
        string kind,
        string priority,
        string title,
        string message,
        DateTimeOffset now,
        DateTime? dueDateUtc,
        bool isUrgent) => new()
        {
            Id = id,
            Kind = kind,
            Priority = priority,
            Title = title,
            Message = message,
            PatientId = patient.PatientId,
            PatientName = FormatPatientName(patient.PatientFirstName, patient.PatientLastName),
            PatientMedicalRecordNumber = patient.PatientMedicalRecordNumber,
            Timestamp = ToUtcOffset(patient.LastModifiedUtc == default ? now.UtcDateTime : patient.LastModifiedUtc),
            DueDateUtc = dueDateUtc,
            TargetUrl = $"/patient/{patient.PatientId:D}/info",
            ActionLabel = "Review Auth",
            IsUrgent = isUrgent
        };

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

    private static IOrderedEnumerable<DashboardAlertItemResponse> OrderAlerts(IEnumerable<DashboardAlertItemResponse> alerts) =>
        alerts
            .OrderBy(alert => PriorityRank(alert.Priority))
            .ThenBy(alert => KindRank(alert.Kind))
            .ThenBy(alert => alert.DueDateUtc ?? alert.Timestamp.UtcDateTime)
            .ThenBy(alert => alert.Id, StringComparer.Ordinal);

    private static int KindRank(string kind) => kind switch
    {
        DashboardAlertKinds.NotesDueToday => 0,
        DashboardAlertKinds.AuthorizationStatus => 1,
        DashboardAlertKinds.AuthorizationExpiration => 2,
        DashboardAlertKinds.AuthorizationReauthorizationDue => 3,
        DashboardAlertKinds.AuthorizationVisitLimit => 4,
        DashboardAlertKinds.UnsignedNote => 5,
        DashboardAlertKinds.SubmittedIntakeReview => 6,
        DashboardAlertKinds.IncompleteIntake => 7,
        _ => 8
    };

    private static bool IsAuthorizationAlertKind(string kind) => kind is
        DashboardAlertKinds.AuthorizationStatus or
        DashboardAlertKinds.AuthorizationExpiration or
        DashboardAlertKinds.AuthorizationReauthorizationDue or
        DashboardAlertKinds.AuthorizationVisitLimit;

    private static DashboardVisibilityContext BuildVisibilityContext(IIdentityContextAccessor identityContext) =>
        new(identityContext.TryGetCurrentUserId(), identityContext.GetCurrentUserRole());

    private static IQueryable<Appointment> ApplyAppointmentVisibility(
        IQueryable<Appointment> query,
        DashboardVisibilityContext visibility)
    {
        if (!visibility.RequiresProviderScope)
        {
            return query;
        }

        return visibility.CurrentUserId is { } userId
            ? query.Where(appointment => appointment.ClinicalId == userId)
            : query.Where(_ => false);
    }

    private static IQueryable<ClinicalNote> ApplyNoteVisibility(
        IQueryable<ClinicalNote> query,
        DashboardVisibilityContext visibility)
    {
        if (!visibility.RequiresProviderScope)
        {
            return query;
        }

        return visibility.CurrentUserId is { } userId
            ? query.Where(note =>
                note.ModifiedByUserId == userId ||
                note.SignedByUserId == userId ||
                note.CoSignedByUserId == userId ||
                (note.Appointment != null && note.Appointment.ClinicalId == userId))
            : query.Where(_ => false);
    }

    private static IQueryable<IntakeForm> ApplyIntakeVisibility(
        IQueryable<IntakeForm> query,
        ApplicationDbContext db,
        DashboardVisibilityContext visibility)
    {
        if (!visibility.RequiresProviderScope)
        {
            return query;
        }

        return visibility.CurrentUserId is { } userId
            ? query.Where(intake =>
                intake.ModifiedByUserId == userId ||
                db.Appointments.Any(appointment =>
                    appointment.PatientId == intake.PatientId &&
                    appointment.ClinicalId == userId) ||
                db.ClinicalNotes.Any(note =>
                    !note.IsAddendum &&
                    note.PatientId == intake.PatientId &&
                    (note.ModifiedByUserId == userId ||
                     note.SignedByUserId == userId ||
                     note.CoSignedByUserId == userId ||
                     (note.Appointment != null && note.Appointment.ClinicalId == userId))))
            : query.Where(_ => false);
    }

    private static IQueryable<Patient> ApplyPatientVisibility(
        IQueryable<Patient> query,
        ApplicationDbContext db,
        DashboardVisibilityContext visibility)
    {
        if (!visibility.RequiresProviderScope)
        {
            return query;
        }

        return visibility.CurrentUserId is { } userId
            ? query.Where(patient =>
                patient.ModifiedByUserId == userId ||
                db.Appointments.Any(appointment =>
                    appointment.PatientId == patient.Id &&
                    appointment.ClinicalId == userId) ||
                db.ClinicalNotes.Any(note =>
                    !note.IsAddendum &&
                    note.PatientId == patient.Id &&
                    (note.ModifiedByUserId == userId ||
                     note.SignedByUserId == userId ||
                     note.CoSignedByUserId == userId ||
                     (note.Appointment != null && note.Appointment.ClinicalId == userId))))
            : query.Where(_ => false);
    }

    private sealed record DashboardVisibilityContext(Guid? CurrentUserId, string? CurrentUserRole)
    {
        public bool RequiresProviderScope =>
            string.Equals(CurrentUserRole, Roles.PT, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(CurrentUserRole, Roles.PTA, StringComparison.OrdinalIgnoreCase);
    }

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

    private static string BuildPlanOfCareTitle(PlanOfCareNoteCandidate note)
    {
        if (note.NoteType == NoteType.Evaluation && note.IsReEvaluation)
        {
            return "Re-Evaluation Plan of Care";
        }

        return note.NoteType switch
        {
            NoteType.Evaluation => "Evaluation Plan of Care",
            NoteType.ProgressNote => "Progress Note Plan of Care",
            _ => $"{FormatEnumLabel(note.NoteType.ToString())} Plan of Care"
        };
    }

    private static string ToPlanOfCareStatus(NoteStatus status) => status switch
    {
        NoteStatus.Signed => "Signed",
        NoteStatus.PendingCoSign => "Pending Review",
        NoteStatus.Draft => "Draft",
        _ => "Active"
    };

    private static NoteWorkspaceV2Payload? TryDeserializeWorkspacePayload(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson) || contentJson == "{}")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(contentJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryCountCptUnits(string cptCodesJson)
    {
        if (string.IsNullOrWhiteSpace(cptCodesJson) || cptCodesJson == "[]")
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(cptCodesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var total = 0;
            foreach (var code in document.RootElement.EnumerateArray())
            {
                if (code.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                total += code.TryGetProperty("units", out var camelUnits) && camelUnits.TryGetInt32(out var camelValue)
                    ? Math.Max(0, camelValue)
                    : code.TryGetProperty("Units", out var pascalUnits) && pascalUnits.TryGetInt32(out var pascalValue)
                        ? Math.Max(0, pascalValue)
                        : 1;
            }

            return total;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetSingleValue(IReadOnlyCollection<int>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        return values.Count == 1 ? values.First() : null;
    }

    private static PatientAuthorizationInfo? TryDeserializePayerAuthorization(string payerInfoJson)
    {
        try
        {
            return JsonSerializer.Deserialize<PatientAuthorizationInfo>(payerInfoJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseDate(string? rawDate, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return false;
        }

        var trimmed = rawDate.Trim();
        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedOffset))
        {
            value = parsedOffset.UtcDateTime.Date;
            return true;
        }

        if (!DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed.Kind switch
        {
            DateTimeKind.Utc => parsed.Date,
            DateTimeKind.Local => parsed.ToUniversalTime().Date,
            _ => DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
        };
        return true;
    }

    private static bool TryParseInteger(string? rawValue, out int value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
        {
            value = (int)Math.Floor(decimalValue);
            return true;
        }

        return false;
    }

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : status.Trim()
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();

    private static string? ReadJsonScalar(JsonElement? value)
    {
        if (value is not { } element)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    private static class DashboardAlertKinds
    {
        public const string NotesDueToday = "notesDueToday";
        public const string IncompleteIntake = "incompleteIntake";
        public const string UnsignedNote = "unsignedNote";
        public const string SubmittedIntakeReview = "submittedIntakeReview";
        public const string AuthorizationStatus = "authorizationStatus";
        public const string AuthorizationExpiration = "authorizationExpiration";
        public const string AuthorizationReauthorizationDue = "authorizationReauthorizationDue";
        public const string AuthorizationVisitLimit = "authorizationVisitLimit";
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

    private sealed record SubmittedIntakeReviewAlertCandidate(
        Guid Id,
        Guid PatientId,
        string PatientFirstName,
        string PatientLastName,
        string? PatientMedicalRecordNumber,
        DateTime SubmittedAtUtc);

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

    private sealed record PatientAuthorizationAlertCandidate(
        Guid PatientId,
        string PatientFirstName,
        string PatientLastName,
        string? PatientMedicalRecordNumber,
        DateTime LastModifiedUtc,
        string PayerInfoJson);

    private sealed class PatientAuthorizationInfo
    {
        public JsonElement? AuthorizationStatus { get; set; }
        public JsonElement? AuthorizationEndDate { get; set; }
        public JsonElement? ReAuthorizationDueDate { get; set; }
        public JsonElement? VisitsRemaining { get; set; }
        public JsonElement? VisitAlertThreshold { get; set; }
    }

    private sealed record RelatedNoteCandidate(
        Guid PatientId,
        Guid? AppointmentId,
        DateTime DateOfService);

    private sealed record PlanOfCareNoteCandidate(
        Guid Id,
        Guid PatientId,
        string PatientName,
        NoteType NoteType,
        NoteStatus NoteStatus,
        bool IsReEvaluation,
        DateTime DateOfService,
        DateTime LastModifiedUtc,
        Guid ModifiedByUserId,
        string ContentJson,
        string CptCodesJson);
}
