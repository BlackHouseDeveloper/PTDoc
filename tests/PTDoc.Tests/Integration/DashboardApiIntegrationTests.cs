using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class DashboardApiIntegrationTests : IClassFixture<PtDocApiFactory>
{
    private readonly PtDocApiFactory _factory;

    public DashboardApiIntegrationTests(PtDocApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Notifications_GetState_Returns_DefaultPreferences_And_Sorts_By_Timestamp()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var adminUser = await db.Users.SingleAsync(user => user.Username == "integration-admin");

        var oldestTimestamp = new DateTimeOffset(2026, 4, 4, 12, 0, 0, TimeSpan.Zero);
        var middleTimestamp = new DateTimeOffset(2026, 4, 4, 14, 0, 0, TimeSpan.Zero);
        var newestTimestamp = new DateTimeOffset(2026, 4, 4, 16, 0, 0, TimeSpan.Zero);

        db.UserNotifications.AddRange(
            new UserNotification
            {
                UserId = adminUser.Id,
                Title = "Oldest notification",
                Message = "Created first",
                Timestamp = oldestTimestamp,
                Type = "system",
                IsRead = false,
                IsUrgent = false
            },
            new UserNotification
            {
                UserId = adminUser.Id,
                Title = "Newest notification",
                Message = "Created last",
                Timestamp = newestTimestamp,
                Type = "intake",
                IsRead = false,
                IsUrgent = true
            },
            new UserNotification
            {
                UserId = adminUser.Id,
                Title = "Middle notification",
                Message = "Created second",
                Timestamp = middleTimestamp,
                Type = "note",
                IsRead = true,
                IsUrgent = false
            });

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.Admin);
        using var response = await client.GetAsync("/api/v1/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var state = await response.Content.ReadFromJsonAsync<NotificationStateResponse>();
        Assert.NotNull(state);
        Assert.True(state!.Preferences.InAppNotifications);
        Assert.True(state.Preferences.EmailNotifications);
        Assert.Equal(
            ["Newest notification", "Middle notification", "Oldest notification"],
            state.Notifications.Select(notification => notification.Title).ToArray());
    }

    [Fact]
    public async Task IntakeEligiblePatients_Returns_200_And_Excludes_LockedOnly_Patients()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var frontDeskUser = await db.Users.SingleAsync(user => user.Username == "integration-frontdesk");

        var unlockedPatient = CreatePatient("Una", "Unlocked", frontDeskUser.Id);
        var newPatient = CreatePatient("Nora", "New", frontDeskUser.Id);
        var lockedOnlyPatient = CreatePatient("Lara", "Locked", frontDeskUser.Id);

        db.Patients.AddRange(unlockedPatient, newPatient, lockedOnlyPatient);
        db.IntakeForms.AddRange(
            CreateIntakeForm(unlockedPatient.Id, frontDeskUser.Id, isLocked: false),
            CreateIntakeForm(lockedOnlyPatient.Id, frontDeskUser.Id, isLocked: true));

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.FrontDesk);
        using var response = await client.GetAsync("/api/v1/intake/patients/eligible?take=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var patients = await response.Content.ReadFromJsonAsync<List<PatientListItemResponse>>();

        Assert.NotNull(patients);
        Assert.Contains(patients!, patient => patient.Id == unlockedPatient.Id);
        Assert.Contains(patients, patient => patient.Id == newPatient.Id);
        Assert.DoesNotContain(patients, patient => patient.Id == lockedOnlyPatient.Id);
    }

    [Fact]
    public async Task DashboardAlerts_Returns_LiveClinicalAlerts_AndExcludesResolvedItems()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");

        var today = DateTime.UtcNow.Date;
        var duePatient = CreatePatient("Nora", "DueToday", clinician.Id, medicalRecordNumber: "ALRT-DUE");
        var signedPatient = CreatePatient("Simon", "Signed", clinician.Id, medicalRecordNumber: "ALRT-SIGNED");
        var unsignedPatient = CreatePatient("Uma", "Unsigned", clinician.Id, medicalRecordNumber: "ALRT-UNSIGNED");
        var cosignPatient = CreatePatient("Peter", "PendingCosign", clinician.Id, medicalRecordNumber: "ALRT-COSIGN");
        var intakePatient = CreatePatient("Ian", "Intake", clinician.Id, medicalRecordNumber: "ALRT-INTAKE");
        var archivedPatient = CreatePatient("Ada", "Archived", clinician.Id, medicalRecordNumber: "ALRT-ARCHIVED", isArchived: true);
        var cancelledPatient = CreatePatient("Casey", "Cancelled", clinician.Id, medicalRecordNumber: "ALRT-CANCELLED");
        var lockedIntakePatient = CreatePatient("Lena", "LockedIntake", clinician.Id, medicalRecordNumber: "ALRT-LOCKED");
        var submittedIntakePatient = CreatePatient("Sam", "SubmittedIntake", clinician.Id, medicalRecordNumber: "ALRT-SUBMITTED");

        var dueAppointment = CreateAppointment(duePatient.Id, clinician.Id, today.AddHours(9), AppointmentStatus.Completed);
        var signedAppointment = CreateAppointment(signedPatient.Id, clinician.Id, today.AddHours(10), AppointmentStatus.Completed);
        var unsignedAppointment = CreateAppointment(unsignedPatient.Id, clinician.Id, today.AddHours(11), AppointmentStatus.Completed);
        var archivedAppointment = CreateAppointment(archivedPatient.Id, clinician.Id, today.AddHours(12), AppointmentStatus.Completed);
        var cancelledAppointment = CreateAppointment(cancelledPatient.Id, clinician.Id, today.AddHours(13), AppointmentStatus.Cancelled);

        var signedNote = CreateClinicalNote(
            signedPatient.Id,
            clinician.Id,
            signedAppointment.Id,
            NoteStatus.Signed,
            today.AddHours(10),
            today.AddHours(10));
        var unsignedNote = CreateClinicalNote(
            unsignedPatient.Id,
            clinician.Id,
            unsignedAppointment.Id,
            NoteStatus.Draft,
            today.AddDays(-1).AddHours(11),
            today.AddHours(11));
        var addendum = CreateClinicalNote(
            duePatient.Id,
            clinician.Id,
            dueAppointment.Id,
            NoteStatus.Draft,
            today,
            today,
            isAddendum: true);
        var pendingCoSignNote = CreateClinicalNote(
            cosignPatient.Id,
            clinician.Id,
            null,
            NoteStatus.PendingCoSign,
            today,
            today);

        db.Patients.AddRange(
            duePatient,
            signedPatient,
            unsignedPatient,
            cosignPatient,
            intakePatient,
            archivedPatient,
            cancelledPatient,
            lockedIntakePatient,
            submittedIntakePatient);
        db.Appointments.AddRange(
            dueAppointment,
            signedAppointment,
            unsignedAppointment,
            archivedAppointment,
            cancelledAppointment);
        db.ClinicalNotes.AddRange(signedNote, unsignedNote, addendum, pendingCoSignNote);
        db.IntakeForms.AddRange(
            CreateIntakeForm(intakePatient.Id, clinician.Id, isLocked: false, lastModifiedUtc: today.AddDays(-2)),
            CreateIntakeForm(lockedIntakePatient.Id, clinician.Id, isLocked: true, lastModifiedUtc: today.AddDays(-2)),
            CreateIntakeForm(submittedIntakePatient.Id, clinician.Id, isLocked: true, submittedAt: today.AddHours(8), lastModifiedUtc: today.AddDays(-2)));

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync("/api/v1/dashboard/alerts?take=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DashboardAlertsResponse>();
        Assert.NotNull(body);

        var alerts = body!.Alerts;
        var dueAlert = Assert.Single(alerts, alert => alert.Id == $"notesDueToday:{dueAppointment.Id:N}");
        Assert.Equal("notesDueToday", dueAlert.Kind);
        Assert.Equal("high", dueAlert.Priority);
        Assert.Equal(duePatient.Id, dueAlert.PatientId);
        Assert.Equal("ALRT-DUE", dueAlert.PatientMedicalRecordNumber);
        Assert.Equal($"/patient/{duePatient.Id:D}/new-note", dueAlert.TargetUrl);
        Assert.Equal("Start", dueAlert.ActionLabel);

        var unsignedAlert = Assert.Single(alerts, alert => alert.Id == $"unsignedNote:{unsignedNote.Id:N}");
        Assert.Equal("unsignedNote", unsignedAlert.Kind);
        Assert.Equal($"/patient/{unsignedPatient.Id:D}/note/{unsignedNote.Id:D}", unsignedAlert.TargetUrl);

        var coSignAlert = Assert.Single(alerts, alert => alert.Id == $"unsignedNote:{pendingCoSignNote.Id:N}");
        Assert.Equal("Co-sign Needed", coSignAlert.Title);
        Assert.Equal("Review", coSignAlert.ActionLabel);

        var intakeAlert = Assert.Single(alerts, alert => alert.PatientId == intakePatient.Id);
        Assert.Equal(intakePatient.Id, intakeAlert.PatientId);
        Assert.Equal($"/intake/{intakePatient.Id:D}", intakeAlert.TargetUrl);
        Assert.Equal("Open Intake", intakeAlert.ActionLabel);

        var reviewAlert = Assert.Single(alerts, alert => alert.PatientId == submittedIntakePatient.Id);
        Assert.Equal("submittedIntakeReview", reviewAlert.Kind);
        Assert.Equal($"/intake/{submittedIntakePatient.Id:D}", reviewAlert.TargetUrl);
        Assert.Equal("Review", reviewAlert.ActionLabel);

        Assert.DoesNotContain(alerts, alert => alert.Id == $"notesDueToday:{signedAppointment.Id:N}");
        Assert.DoesNotContain(alerts, alert => alert.Id == $"notesDueToday:{unsignedAppointment.Id:N}");
        Assert.DoesNotContain(alerts, alert => alert.Id == $"notesDueToday:{archivedAppointment.Id:N}");
        Assert.DoesNotContain(alerts, alert => alert.Id == $"notesDueToday:{cancelledAppointment.Id:N}");
        Assert.DoesNotContain(alerts, alert => alert.PatientId == lockedIntakePatient.Id);
        Assert.DoesNotContain(alerts, alert => alert.Id == $"unsignedNote:{addendum.Id:N}");
    }

    [Fact]
    public async Task DashboardSnapshot_Returns_ClinicalSnapshotShape()
    {
        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync("/api/v1/dashboard/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<DashboardSnapshotResponse>();
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot!.Overview);
        Assert.True(snapshot.GeneratedAtUtc > DateTimeOffset.MinValue);
        Assert.True(snapshot.TotalAlertCount >= snapshot.Alerts.Count);
        Assert.True(snapshot.UrgentAlertCount >= snapshot.Alerts.Count(alert => alert.IsUrgent));
        Assert.True(snapshot.RecentNotes.Count <= 5);
        Assert.True(snapshot.Alerts.Count <= 10);
    }

    [Fact]
    public async Task DashboardSnapshot_Returns_RecentPlansOfCare_FromEvaluationAndProgressNotes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");

        var planPatient = CreatePatient("Poppy", "PlanCare", clinician.Id, medicalRecordNumber: "POC-RECENT");
        var progressPatient = CreatePatient("Paxton", "ProgressPoc", clinician.Id, medicalRecordNumber: "POC-PROGRESS");
        var archivedPatient = CreatePatient("Aria", "ArchivedPoc", clinician.Id, medicalRecordNumber: "POC-ARCHIVED", isArchived: true);
        var dailyPatient = CreatePatient("Drew", "DailyNote", clinician.Id, medicalRecordNumber: "POC-DAILY");
        var addendumPatient = CreatePatient("Avery", "AddendumPoc", clinician.Id, medicalRecordNumber: "POC-ADDENDUM");
        var legacyPatient = CreatePatient("Lena", "LegacyPoc", clinician.Id, medicalRecordNumber: "POC-LEGACY");

        var now = DateTime.UtcNow.AddYears(1);
        var planNote = CreateClinicalNote(
            planPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            now.Date,
            now.AddMinutes(5),
            noteType: NoteType.Evaluation,
            contentJson: """
                {
                  "noteType": 0,
                  "assessment": {
                    "diagnosisCodes": [
                      { "code": "M25.511", "description": "Pain in right shoulder" },
                      { "code": "M62.81", "description": "Muscle weakness" }
                    ]
                  },
                  "plan": {
                    "treatmentFrequencyDaysPerWeek": [2],
                    "treatmentDurationWeeks": [6]
                  }
                }
                """,
            cptCodesJson: """
                [
                  { "code": "97110", "units": 2 },
                  { "code": "97530", "units": 1 }
                ]
                """);
        var progressNote = CreateClinicalNote(
            progressPatient.Id,
            clinician.Id,
            null,
            NoteStatus.PendingCoSign,
            now.Date,
            now.AddMinutes(4),
            noteType: NoteType.ProgressNote);
        var archivedNote = CreateClinicalNote(
            archivedPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            now.Date,
            now.AddMinutes(3),
            noteType: NoteType.Evaluation);
        var dailyNote = CreateClinicalNote(
            dailyPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            now.Date,
            now.AddMinutes(2),
            noteType: NoteType.Daily);
        var addendumNote = CreateClinicalNote(
            addendumPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            now.Date,
            now.AddMinutes(1),
            isAddendum: true,
            noteType: NoteType.Evaluation);
        var legacyNullDiagnosisNote = CreateClinicalNote(
            legacyPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            now.Date,
            now.AddMinutes(-1),
            noteType: NoteType.Evaluation,
            contentJson: """
                {
                  "noteType": 0,
                  "assessment": {
                    "diagnosisCodes": null
                  }
                }
                """);

        db.Patients.AddRange(planPatient, progressPatient, archivedPatient, dailyPatient, addendumPatient, legacyPatient);
        db.ClinicalNotes.AddRange(planNote, progressNote, archivedNote, dailyNote, addendumNote, legacyNullDiagnosisNote);

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync("/api/v1/dashboard/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await response.Content.ReadFromJsonAsync<DashboardSnapshotResponse>();
        Assert.NotNull(snapshot);

        var plans = snapshot!.RecentPlansOfCare;
        var planSummary = Assert.Single(plans, plan => plan.Id == planNote.Id);
        Assert.Equal(planPatient.Id, planSummary.PatientId);
        Assert.Equal("Poppy PlanCare", planSummary.PatientName);
        Assert.Equal("Evaluation Plan of Care", planSummary.Title);
        Assert.Equal("Draft", planSummary.Status);
        Assert.Equal($"/patient/{planPatient.Id:D}/note/{planNote.Id:D}", planSummary.TargetUrl);
        Assert.Equal(2, planSummary.IcdCount);
        Assert.Equal(3, planSummary.Sessions);
        Assert.Equal(12, planSummary.VisitsTotal);
        Assert.Equal(6, planSummary.WeekTotal);
        Assert.False(string.IsNullOrWhiteSpace(planSummary.LastEditedBy));

        var progressSummary = Assert.Single(plans, plan => plan.Id == progressNote.Id);
        Assert.Equal("Pending Review", progressSummary.Status);

        var legacySummary = Assert.Single(plans, plan => plan.Id == legacyNullDiagnosisNote.Id);
        Assert.Null(legacySummary.IcdCount);

        Assert.DoesNotContain(plans, plan => plan.Id == archivedNote.Id);
        Assert.DoesNotContain(plans, plan => plan.Id == dailyNote.Id);
        Assert.DoesNotContain(plans, plan => plan.Id == addendumNote.Id);
    }

    [Fact]
    public async Task NavigationBadges_Returns_LiveCounts_ForClinicalActionItems()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");

        var today = DateTime.UtcNow.Date;
        var badgeClinician = new User
        {
            Id = Guid.NewGuid(),
            Username = $"badge-clinician-{Guid.NewGuid():N}",
            PinHash = "integration-test-pin-hash",
            FirstName = "Badge",
            LastName = "Clinician",
            Role = Roles.PT,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        var missingNotePatient = CreatePatient("Nadia", "BadgeMissingNote", clinician.Id, medicalRecordNumber: "BADGE-MISSING");
        var unsignedPatient = CreatePatient("Uma", "BadgeUnsigned", clinician.Id, medicalRecordNumber: "BADGE-UNSIGNED");
        var intakePatient = CreatePatient("Ian", "BadgeIntake", clinician.Id, medicalRecordNumber: "BADGE-INTAKE");
        var reviewPatient = CreatePatient("Rhea", "BadgeReview", clinician.Id, medicalRecordNumber: "BADGE-REVIEW");

        db.Users.Add(badgeClinician);
        db.Patients.AddRange(missingNotePatient, unsignedPatient, intakePatient, reviewPatient);
        db.Appointments.Add(CreateAppointment(missingNotePatient.Id, badgeClinician.Id, today.AddHours(9), AppointmentStatus.Completed));
        db.ClinicalNotes.Add(CreateClinicalNote(
            unsignedPatient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            today.AddDays(-1),
            today));
        db.IntakeForms.AddRange(
            CreateIntakeForm(intakePatient.Id, clinician.Id, isLocked: false, lastModifiedUtc: today.AddHours(8)),
            CreateIntakeForm(reviewPatient.Id, clinician.Id, isLocked: true, submittedAt: today.AddHours(8), lastModifiedUtc: today.AddHours(8)));
        db.UserNotifications.Add(new UserNotification
        {
            UserId = clinician.Id,
            Title = "Badge notification",
            Message = "Unread notification for nav badge count",
            Timestamp = DateTimeOffset.UtcNow,
            Type = "system",
            IsRead = false,
            IsArchived = false
        });

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.PT);
        using var response = await client.GetAsync("/api/v1/navigation/badges");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var counts = await response.Content.ReadFromJsonAsync<NavigationBadgeCountsResponse>();
        Assert.NotNull(counts);
        Assert.True(counts!.IntakeCount >= 2);
        Assert.True(counts.NotesCount >= 2);
        Assert.True(counts.NotificationsCount >= 1);
        Assert.True(counts.GeneratedAtUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task NavigationBadges_HidesNoteCounts_WhenCallerCannotReadNotes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinician = await db.Users.SingleAsync(user => user.Username == "integration-pt");
        var patient = CreatePatient("Noah", "FrontDeskHiddenNote", clinician.Id, medicalRecordNumber: "BADGE-FD");

        db.Patients.Add(patient);
        db.ClinicalNotes.Add(CreateClinicalNote(
            patient.Id,
            clinician.Id,
            null,
            NoteStatus.Draft,
            DateTime.UtcNow.Date,
            DateTime.UtcNow));

        await db.SaveChangesAsync();

        using var client = _factory.CreateClientWithRole(Roles.FrontDesk);
        using var response = await client.GetAsync("/api/v1/navigation/badges");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var counts = await response.Content.ReadFromJsonAsync<NavigationBadgeCountsResponse>();
        Assert.NotNull(counts);
        Assert.Equal(0, counts!.NotesCount);
    }

    private static Patient CreatePatient(
        string firstName,
        string lastName,
        Guid modifiedByUserId,
        string? medicalRecordNumber = null,
        bool isArchived = false) => new()
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            MedicalRecordNumber = medicalRecordNumber,
            IsArchived = isArchived,
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
            SyncState = SyncState.Pending
        };

    private static IntakeForm CreateIntakeForm(
        Guid patientId,
        Guid modifiedByUserId,
        bool isLocked,
        DateTime? submittedAt = null,
        DateTime? lastModifiedUtc = null) => new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString("N"),
            IsLocked = isLocked,
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = "{}",
            SubmittedAt = submittedAt,
            LastModifiedUtc = lastModifiedUtc ?? DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
            SyncState = SyncState.Pending
        };

    private static Appointment CreateAppointment(
        Guid patientId,
        Guid clinicianId,
        DateTime startTimeUtc,
        AppointmentStatus status) => new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            ClinicalId = clinicianId,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = startTimeUtc.AddMinutes(45),
            AppointmentType = AppointmentType.FollowUp,
            Status = status,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = clinicianId,
            SyncState = SyncState.Pending
        };

    private static ClinicalNote CreateClinicalNote(
        Guid patientId,
        Guid modifiedByUserId,
        Guid? appointmentId,
        NoteStatus status,
        DateTime dateOfService,
        DateTime lastModifiedUtc,
        bool isAddendum = false,
        NoteType noteType = NoteType.Daily,
        string contentJson = "{}",
        string cptCodesJson = "[]") => new()
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            AppointmentId = appointmentId,
            IsAddendum = isAddendum,
            NoteType = noteType,
            NoteStatus = status,
            ContentJson = contentJson,
            CptCodesJson = cptCodesJson,
            DateOfService = dateOfService,
            CreatedUtc = lastModifiedUtc,
            LastModifiedUtc = lastModifiedUtc,
            ModifiedByUserId = modifiedByUserId,
            SyncState = SyncState.Pending,
            SignatureHash = status == NoteStatus.Signed ? "signed-test-note" : null,
            SignedUtc = status == NoteStatus.Signed ? lastModifiedUtc : null,
            SignedByUserId = status == NoteStatus.Signed ? modifiedByUserId : null
        };
}
