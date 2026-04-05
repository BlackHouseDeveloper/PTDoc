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

[Trait("Category", "Integration")]
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

    private static Patient CreatePatient(string firstName, string lastName, Guid modifiedByUserId) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = modifiedByUserId,
        SyncState = SyncState.Pending
    };

    private static IntakeForm CreateIntakeForm(Guid patientId, Guid modifiedByUserId, bool isLocked) => new()
    {
        Id = Guid.NewGuid(),
        PatientId = patientId,
        TemplateVersion = "1.0",
        AccessToken = Guid.NewGuid().ToString("N"),
        IsLocked = isLocked,
        ResponseJson = "{}",
        PainMapData = "{}",
        Consents = "{}",
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = modifiedByUserId,
        SyncState = SyncState.Pending
    };
}
