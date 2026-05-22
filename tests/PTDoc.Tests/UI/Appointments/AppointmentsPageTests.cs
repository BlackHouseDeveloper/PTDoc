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

    private void RegisterServices()
    {
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
            .ReturnsAsync(new AppointmentsOverviewResponse());

        var toastService = new Mock<IToastService>(MockBehavior.Loose);

        Services.AddSingleton(headerConfigurationService.Object);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(appointmentService.Object);
        Services.AddSingleton(toastService.Object);
    }
}
