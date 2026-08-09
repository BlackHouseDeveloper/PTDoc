using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.UI.Components.Dashboard;

namespace PTDoc.Tests.UI.Dashboard;

[Trait("Category", "CoreCi")]
public sealed class AppointmentsSectionTests : TestContext
{
    [Fact]
    public void AppointmentsSection_RendersDataDrivenStates_AndRoutesToExistingWorkflows()
    {
        var checkedIn = CreateAppointment(
            "Sarah Johnson",
            "PT001",
            "Checked In",
            "Completed",
            "Lower back pain evaluation",
            8,
            9);
        var scheduled = CreateAppointment(
            "Lisa Thompson",
            "PT005",
            "Scheduled",
            "Completed",
            "Cervical spine treatment",
            10,
            10);
        scheduled.ProgressNoteDueDate = new DateTime(2026, 12, 10);
        var missingIntake = CreateAppointment(
            "David Kim",
            "PT006",
            "Scheduled",
            "Missing",
            "Ankle sprain follow-up",
            5,
            15);

        var cut = RenderComponent<AppointmentsSection>(parameters => parameters
            .Add(component => component.Appointments, new[] { checkedIn, scheduled, missingIntake }));

        Assert.Equal(3, cut.FindAll("article.appointment-card").Count);
        Assert.Single(cut.FindAll("article.appointment-card--critical"));
        Assert.Contains("Action Required", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("8", cut.Find("strong.appointment-card__visit-count").TextContent.Trim());
        Assert.Contains("Start Note", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Check In", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, cut.Markup.Split("PN Due:", StringSplitOptions.None).Length - 1);
        Assert.Contains("Dec 10, 2026", cut.Markup, StringComparison.Ordinal);
        Assert.All(cut.FindAll("button.appointment-card__action--cancel"), button => Assert.True(button.HasAttribute("disabled")));

        var navigation = Services.GetRequiredService<NavigationManager>();

        cut.Find("button[aria-label='Add appointment']").Click();
        Assert.EndsWith("/appointments?action=appointments.new", navigation.Uri, StringComparison.Ordinal);

        cut.Find($"button[aria-label='Start note for {checkedIn.PatientName}']").Click();
        Assert.EndsWith(
            $"/appointments?dateRange=today&appointmentId={checkedIn.Id:D}&action=appointments.start-note",
            navigation.Uri,
            StringComparison.Ordinal);

        cut.Find($"button[aria-label='Reschedule appointment for {scheduled.PatientName}']").Click();
        Assert.EndsWith(
            $"/appointments?dateRange=today&appointmentId={scheduled.Id:D}&action=appointments.reschedule",
            navigation.Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsSection_UsesAccessibleLoadingErrorAndEmptyStates()
    {
        var loading = RenderComponent<AppointmentsSection>(parameters => parameters
            .Add(component => component.IsLoading, true));
        Assert.NotNull(loading.Find("[role='status'][aria-label=\"Loading today's appointments\"]"));

        var error = RenderComponent<AppointmentsSection>(parameters => parameters
            .Add(component => component.HasError, true)
            .Add(component => component.ErrorMessage, "Appointments unavailable."));
        Assert.Contains("Appointments unavailable.", error.Find("[role='alert']").TextContent, StringComparison.Ordinal);

        var empty = RenderComponent<AppointmentsSection>();
        Assert.Contains("No appointments scheduled today", empty.Markup, StringComparison.Ordinal);
    }

    private static AppointmentListItemResponse CreateAppointment(
        string patientName,
        string medicalRecordNumber,
        string workflowStatus,
        string intakeStatus,
        string notes,
        int visitCount,
        int hour)
    {
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(hour), DateTimeKind.Local);
        return new AppointmentListItemResponse
        {
            Id = Guid.NewGuid(),
            PatientRecordId = Guid.NewGuid(),
            PatientName = patientName,
            MedicalRecordNumber = medicalRecordNumber,
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
            AppointmentType = "Follow-up",
            AppointmentStatus = workflowStatus,
            VisitWorkflowStatus = workflowStatus,
            IntakeStatus = intakeStatus,
            Notes = notes,
            VisitCount = visitCount
        };
    }
}
