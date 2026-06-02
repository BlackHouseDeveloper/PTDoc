using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.Tests.UI.Appointments;

[Trait("Category", "CoreCi")]
public sealed class AppointmentsPageTests : TestContext
{
    [Fact]
    public void AppointmentsPage_NewAppointmentRouteAction_OpensModalAndNormalizesUrl()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?action=appointments.new");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("New Appointment", cut.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll(".modal-container"));
            Assert.Equal("true", cut.Find(".appointments-page").GetAttribute("aria-hidden"));
            Assert.True(cut.Find(".appointments-page").HasAttribute("inert"));
            Assert.Contains(JSInterop.Invocations["import"], invocation =>
                invocation.Arguments.Count > 0
                && string.Equals(
                    invocation.Arguments[0]?.ToString(),
                    "./_content/PTDoc.UI/js/navigation.js",
                    StringComparison.Ordinal));
        });

        cut.Find(".btn-cancel").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find(".appointments-page").GetAttribute("aria-hidden"));
            Assert.False(cut.Find(".appointments-page").HasAttribute("inert"));
        });
    }

    [Fact]
    public void AppointmentsPage_NoAppointments_RendersEmptyState()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No appointments scheduled for this day", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("Follow Up", "Daily%20Treatment%20Note", true)]
    [InlineData("Initial Evaluation", "Evaluation%20Note", false)]
    [InlineData("Discharge", "Discharge%20Note", false)]
    public void AppointmentsPage_StartVisit_NavigatesToWorkspaceWithAppointmentContext(string appointmentType, string expectedEncodedNoteType, bool expectEvaluationFallback)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var appointmentDate = DateTime.Today;
        var localStart = DateTime.SpecifyKind(appointmentDate.AddHours(10), DateTimeKind.Local);
        var appointment = new AppointmentListItemResponse
        {
            Id = appointmentId,
            PatientRecordId = patientId,
            PatientName = "Alex Patient",
            ClinicianId = clinicianId,
            ClinicianName = "Taylor PT",
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
            AppointmentType = appointmentType,
            AppointmentStatus = "Checked In",
            IntakeStatus = "Complete"
        };

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments = [appointment],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Appointment Details", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Start Visit", StringComparison.Ordinal))
            .Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        var fallbackQuery = expectEvaluationFallback ? "&allowEvaluationFallback=true" : string.Empty;
        Assert.EndsWith(
            $"/patient/{patientId:D}/new-note?noteType={expectedEncodedNoteType}&appointmentId={appointmentId:D}&dateOfService={appointmentDate:yyyy-MM-dd}{fallbackQuery}",
            navigation.Uri,
            StringComparison.Ordinal);
    }

    private void RegisterServices(AppointmentsOverviewResponse? overview = null)
    {
        Services.AddLogging();

        var headerConfigurationService = new Mock<IHeaderConfigurationService>(MockBehavior.Loose);
        headerConfigurationService
            .Setup(service => service.GetConfiguration(It.IsAny<string>()))
            .Returns(new HeaderConfiguration
            {
                Route = "/appointments",
                Title = "Appointments",
                ShowPrimaryAction = true,
                PrimaryActionText = "New Appointment"
            });

        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);
        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview ?? new AppointmentsOverviewResponse());

        var toastService = new Mock<IToastService>(MockBehavior.Loose);

        Services.AddSingleton(headerConfigurationService.Object);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(appointmentService.Object);
        Services.AddSingleton(toastService.Object);
    }
}
