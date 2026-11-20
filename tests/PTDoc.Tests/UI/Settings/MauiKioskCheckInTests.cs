using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using PTDoc.Maui.Components;
using PTDoc.Tests.Integration;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Pages;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class MauiKioskCheckInTests(PtDocApiFactory api) : TestContext, IClassFixture<PtDocApiFactory>
{
    [Fact]
    public async Task AnonymousKiosk_WithExpiredStaffToken_EnrollsStationAndChecksInAppointment()
    {
        var (appointmentId, enrollmentCode, appointmentCode) = await PrepareAppointmentAndStationAsync();
        var staffTokens = new Mock<ITokenStore>();
        staffTokens.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse("expired-staff", "refresh", DateTimeOffset.UtcNow.AddMinutes(-5)));
        var refresh = new Mock<ITokenService>(MockBehavior.Strict);
        var staff = new Mock<IUserService>(MockBehavior.Strict);
        Services.AddScoped(_ => api.CreateDefaultClient(new Uri("https://localhost"),
            new AuthenticatedHttpMessageHandler(staffTokens.Object, refresh.Object, staff.Object)));
        Services.AddSettingsAdministrationApiClient();
        this.AddTestAuthorization();
        ComponentFactories.AddStub<MainLayout>(parameters => parameters.Get(layout => layout.Body)!);
        JSInterop.Setup<string?>("localStorage.getItem", "ptdoc.kiosk.deviceCredential").SetResult(null);
        JSInterop.SetupVoid("localStorage.setItem", _ => true).SetVoidResult();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/kiosk");
        var kiosk = RenderComponent<MauiRoutes>().FindComponent<KioskCheckIn>();

        kiosk.Find("form input").Change(enrollmentCode);
        await kiosk.Find("form").SubmitAsync();
        Assert.Contains("Station enrolled", kiosk.Find(".kiosk-check-in__status").TextContent);
        var stored = Assert.Single(JSInterop.Invocations["localStorage.setItem"]);
        Assert.Equal("ptdoc.kiosk.deviceCredential", stored.Arguments[0]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(stored.Arguments[1])));

        kiosk.Find("form input").Change(appointmentCode);
        await kiosk.Find("form").SubmitAsync();
        Assert.Contains("Check-in complete", kiosk.Find(".kiosk-check-in__status").TextContent);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var status = await db.Appointments.Where(item => item.Id == appointmentId).Select(item => item.Status).SingleAsync();
        Assert.Equal(AppointmentStatus.CheckedIn, status);
        staffTokens.VerifyNoOtherCalls();
        refresh.VerifyNoOtherCalls();
        staff.VerifyNoOtherCalls();
    }

    private async Task<(Guid AppointmentId, string EnrollmentCode, string AppointmentCode)> PrepareAppointmentAndStationAsync()
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinicId = PtDocApiFactory.AuthorizationClinicId;
        var clinicianId = await db.Users.Where(user => user.Username == "integration-pt").Select(user => user.Id).SingleAsync();
        var start = DateTime.UtcNow.AddHours(1);
        var appointment = new Appointment
        {
            Patient = new Patient
            {
                ClinicId = clinicId,
                FirstName = "Kiosk",
                LastName = "Fixture",
                DateOfBirth = new DateTime(1990, 1, 1),
                ModifiedByUserId = clinicianId
            },
            ClinicId = clinicId,
            ClinicalId = clinicianId,
            StartTimeUtc = start,
            EndTimeUtc = start.AddMinutes(30),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = clinicianId
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IKioskCheckInService>();
        var station = await service.CreateStationAsync(clinicId, new CreateKioskStationRequest("Test lobby"), clinicianId, "test-enrollment");
        var token = await service.CreateCheckInTokenAsync(clinicId, appointment.Id, clinicianId, "test-check-in");
        return (appointment.Id, Assert.IsType<KioskEnrollmentCodeDto>(station.Value).Code,
            Assert.IsType<KioskCheckInTokenDto>(token.Value).NumericCode);
    }
}
