using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Pages;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class PageScopedAppointmentUsageTests : TestContext
{
    [Fact]
    public void PatientProfile_UsesPatientScopedAppointmentsForTimeline()
    {
        var today = DateTime.UtcNow.Date;
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        var toastService = new Mock<IToastService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Alex",
                LastName = "Patient",
                DateOfBirth = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Email = "alex@example.com"
            });

        noteService
            .Setup(service => service.GetNotesAsync(patientId, null, null, 25, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NoteListItemApiResponse>());

        appointmentService
            .Setup(service => service.GetByPatientAsync(
                patientId,
                It.Is<DateTime>(startDate => startDate <= today.AddDays(-179) && startDate >= today.AddDays(-181)),
                It.Is<DateTime>(endDate => endDate == today),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppointmentListItemResponse>());

        intakeService
            .Setup(service => service.GetDraftByPatientIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeResponseDraft?)null);

        RegisterCommonServices();
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(appointmentService.Object);
        Services.AddSingleton(intakeService.Object);
        Services.AddSingleton(toastService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.PatientProfile>(parameters => parameters.Add(component => component.Id, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            appointmentService.Verify(service => service.GetByPatientAsync(
                patientId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        });

        appointmentService.Verify(service => service.GetOverviewAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ExportCenter_UsesClinicianDirectoryInsteadOfOverviewForProviders()
    {
        var today = DateTime.UtcNow.Date;
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        appointmentService
            .Setup(service => service.GetCliniciansAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new AppointmentClinicianResponse
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Taylor PT"
                }
            });

        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.Is<DateTime>(startDate => startDate == today.AddDays(-30)),
                It.Is<DateTime>(endDate => endDate == today),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentsOverviewResponse());

        noteService
            .Setup(service => service.GetNotesAsync(null, null, null, 200, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<NoteListItemApiResponse>());

        noteService
            .Setup(service => service.ResolveExportPreviewTargetAsync(It.IsAny<ExportPreviewTargetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExportPreviewTargetResponse
            {
                UnavailableReason = "No SOAP note matches the current export filters."
            });

        RegisterCommonServices();
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(appointmentService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.ExportCenter>();

        cut.WaitForAssertion(() =>
        {
            appointmentService.Verify(service => service.GetCliniciansAsync(It.IsAny<CancellationToken>()), Times.Once);
            appointmentService.Verify(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
                Times.Once);
        });
    }

    [Fact]
    public void Settings_NotificationsSection_UsesCardWrapper()
    {
        var adminApprovalService = new Mock<IAdminApprovalService>(MockBehavior.Strict);
        var notificationCenterService = new Mock<INotificationCenterService>(MockBehavior.Strict);

        adminApprovalService
            .Setup(service => service.GetPendingAsync(It.IsAny<AdminApprovalQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminApprovalPage([], 0, 1, 10));

        notificationCenterService
            .Setup(service => service.GetStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationCenterState());

        RegisterCommonServices();
        Services.AddSingleton(adminApprovalService.Object);
        Services.AddSingleton(notificationCenterService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.Settings>();
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Notifications and Messaging", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var section = cut.Find("section.settings-section-card[aria-labelledby='notifications-title']");
            Assert.Contains("Notifications and Messaging", section.TextContent, StringComparison.Ordinal);
        });
    }

    private void RegisterCommonServices()
    {
        var headerConfigurationService = new Mock<IHeaderConfigurationService>(MockBehavior.Loose);
        headerConfigurationService
            .Setup(service => service.GetConfiguration(It.IsAny<string>()))
            .Returns(new HeaderConfiguration());

        Services.AddSingleton(headerConfigurationService.Object);
    }
}
