using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Settings;
using Xunit;

namespace PTDoc.Tests.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsWriteConflictTests
{
    [Theory]
    [InlineData("preferences", true)]
    [InlineData("hours", true)]
    [InlineData("auto-check-in", true)]
    [InlineData("preferences", false)]
    [InlineData("hours", false)]
    [InlineData("auto-check-in", false)]
    public async Task MissingAggregate_InsertRaceReturnsConflictWithoutSwallowingOtherFailures(string aggregate, bool unique)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fault = new WriteFaultInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(fault).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var clinic = new Clinic { Name = "Write Race", Slug = $"write-race-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        context.SchedulingPreferences.RemoveRange(context.SchedulingPreferences);
        context.ClinicBusinessHours.RemoveRange(context.ClinicBusinessHours);
        context.AutoCheckInPolicies.RemoveRange(context.AutoCheckInPolicies);
        await context.SaveChangesAsync();
        var originalVersion = clinic.Version;
        var audit = StagingAudit(context);
        var scheduling = new SchedulingAdministrationService(context, audit.Object);
        fault.Exception = new DbUpdateException("Injected provider failure",
            new SqliteException("Injected provider failure", unique ? 19 : 1, unique ? 2067 : 1));

        async Task<SettingsOperationStatus> WriteAsync()
        {
            if (aggregate == "preferences")
            {
                return (await scheduling.UpdatePreferencesAsync(clinic.Id,
                    new UpdateSchedulingPreferencesRequest(30, 0, false, false, true, true, true, true,
                        "Week", "Week", null, null, null, false, 24, 0), Guid.NewGuid(), "preferences-race")).Status;
            }
            if (aggregate == "hours")
            {
                return (await scheduling.UpdateClinicHoursAsync(clinic.Id,
                    new UpdateClinicHoursRequest("America/Los_Angeles", clinic.Version,
                        Enum.GetValues<DayOfWeek>().Select(day =>
                            new SaveClinicBusinessHourRequest(day, false, null, null, null, null, 0)).ToArray()),
                    Guid.NewGuid(), "hours-race")).Status;
            }
            return (await new AutoCheckInAdministrationService(context, audit.Object).UpdateAsync(clinic.Id,
                new UpdateAutoCheckInPolicyRequest(false, 24, true, true, AutoCheckInTemplateCatalog.Default, 3, [], 0),
                Guid.NewGuid(), "auto-check-in-race")).Status;
        }

        if (!unique)
        {
            await Assert.ThrowsAsync<DbUpdateException>(WriteAsync);
            return;
        }
        Assert.Equal(SettingsOperationStatus.Conflict, await WriteAsync());
        Assert.DoesNotContain(context.ChangeTracker.Entries(),
            entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        await context.SaveChangesAsync();
        Assert.Empty(await context.AuditLogs.ToListAsync());
        Assert.Equal(originalVersion, await context.Clinics.Select(item => item.Version).SingleAsync());
    }

    [Fact]
    public async Task KioskStationConcurrency_RollsBackAppointmentAndTokenClaim()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var fault = new WriteFaultInterceptor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).AddInterceptors(fault).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var clinic = new Clinic { Name = "Kiosk Race", Slug = $"kiosk-race-{Guid.NewGuid():N}" };
        var clinician = new User
        {
            ClinicId = clinic.Id,
            Username = "kiosk-race-clinician",
            FirstName = "Test",
            LastName = "Clinician",
            Role = "PT",
            IsActive = true,
            PinHash = "not-used"
        };
        var patient = new Patient
        {
            ClinicId = clinic.Id,
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ModifiedByUserId = clinician.Id
        };
        var appointment = new Appointment
        {
            ClinicId = clinic.Id,
            Patient = patient,
            PatientId = patient.Id,
            ClinicalId = clinician.Id,
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = clinician.Id
        };
        context.AddRange(clinic, clinician, patient, appointment);
        await context.SaveChangesAsync();
        var audit = StagingAudit(context);
        var kiosk = new KioskCheckInService(context, audit.Object, new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await kiosk.CreateStationAsync(clinic.Id, new CreateKioskStationRequest("Race Station"), clinician.Id, "station");
        var enrollment = await kiosk.EnrollAsync(station.Value!.Code);
        var token = await kiosk.CreateCheckInTokenAsync(clinic.Id, appointment.Id, clinician.Id, "token");
        fault.Predicate = db => db.ChangeTracker.Entries<KioskStation>()
            .Any(entry => entry.State == EntityState.Modified && entry.Entity.LastSeenAtUtc.HasValue);
        fault.Exception = new DbUpdateConcurrencyException("Concurrent station revocation");

        var result = await kiosk.CheckInAsync(enrollment.Value!.DeviceCredential, token.Value!.QrPayload);

        Assert.Equal(SettingsOperationStatus.NotFound, result.Status);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Equal(AppointmentStatus.Scheduled, (await context.Appointments.SingleAsync()).Status);
        Assert.Null((await context.KioskCheckInTokens.SingleAsync()).ConsumedAtUtc);
        Assert.False(await context.AuditLogs.AnyAsync(item => item.EventType == "KioskAppointmentCheckedIn"));
    }

    private static Mock<IAuditService> StagingAudit(ApplicationDbContext context)
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogSettingsEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((value, _) => context.AuditLogs.Add(new AuditLog
            {
                EventType = value.EventType,
                EntityId = value.EntityId,
                CorrelationId = value.CorrelationId ?? string.Empty,
                TimestampUtc = DateTime.UtcNow
            }))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private sealed class WriteFaultInterceptor : SaveChangesInterceptor
    {
        public Exception? Exception { get; set; }
        public Func<DbContext, bool> Predicate { get; set; } = _ => true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Exception is { } failure && eventData.Context is { } context && Predicate(context))
            {
                Exception = null;
                throw failure;
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
