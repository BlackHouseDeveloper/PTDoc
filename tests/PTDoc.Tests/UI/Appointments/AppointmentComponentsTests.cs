using System.Globalization;
using Bunit;
using PTDoc.Application.DTOs;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Appointments;

namespace PTDoc.Tests.UI.Appointments;

[Trait("Category", "CoreCi")]
public sealed class AppointmentComponentsTests : TestContext
{
    [Fact]
    public void AppointmentDetailModal_CheckedInAppointment_ShowsEnabledStartVisitOnly()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Checked In"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        Assert.Contains("Start Visit", cut.Markup, StringComparison.Ordinal);

        var startVisitButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Start Visit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(startVisitButtons);
        Assert.All(startVisitButtons, button => Assert.False(button.HasAttribute("disabled")));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Check In", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Start Note", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Send Reminder", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cut.FindAll("button"), button => button.TextContent.Contains("Edit Appointment", StringComparison.OrdinalIgnoreCase));

        startVisitButtons[0].Click();

        Assert.Equal("start-visit", requestedAction);
    }

    [Fact]
    public void AppointmentDetailModal_NoteStartedAppointment_ShowsEnterVisitAction()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Note Started"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        var enterVisitButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Enter Visit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(enterVisitButtons);
        Assert.All(enterVisitButtons, button => Assert.False(button.HasAttribute("disabled")));
        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Send Reminder", StringComparison.OrdinalIgnoreCase));

        enterVisitButtons[0].Click();

        Assert.Equal("start-visit", requestedAction);
    }

    [Fact]
    public void AppointmentDetailModal_UsesVisitWorkflowStatusForPrimaryAction()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Checked In", visitWorkflowStatus: "Note Started"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        Assert.Contains("Note Started", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Start Visit", cut.Markup, StringComparison.Ordinal);

        var enterVisitButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Enter Visit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(enterVisitButtons);
        Assert.All(enterVisitButtons, button => Assert.False(button.HasAttribute("disabled")));

        enterVisitButtons[0].Click();

        Assert.Equal("start-visit", requestedAction);
    }

    [Fact]
    public void AppointmentDetailModal_ScheduledAppointment_ShowsCheckInAction()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Scheduled"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Start Visit", StringComparison.OrdinalIgnoreCase));

        var checkInButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Check In", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(checkInButtons);
        Assert.All(checkInButtons, button => Assert.False(button.HasAttribute("disabled")));

        checkInButtons[0].Click();

        Assert.Equal("check-in", requestedAction);
    }

    [Fact]
    public void AppointmentDetailModal_ShowsBillingAndDocumentReadiness()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Completed")));

        Assert.Contains("Billing & Documents", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Copay not configured", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Intake complete", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Visit note missing", cut.Markup, StringComparison.Ordinal);

        var copayButton = Assert.Single(cut.FindAll("button"), button => button.TextContent.Contains("Record Copay", StringComparison.Ordinal));
        Assert.True(copayButton.HasAttribute("disabled"));
        Assert.Contains("Copay collection is not configured for this appointment.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentDetailModal_ScheduledAppointmentWithoutNote_RendersPendingClinicalDocument()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Scheduled")));

        Assert.Contains("Visit note pending", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Visit note missing", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentDetailModal_UsesExplicitVisitNoteForClinicalDocumentReadiness()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(
                status: "Completed",
                visitWorkflowStatus: "Completed",
                visitNoteId: Guid.NewGuid())));

        Assert.Contains("Visit note complete", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentDetailModal_CopayAvailable_EnablesRecordCopayAction()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(
                status: "Scheduled",
                canRecordCopay: true,
                copayStatusLabel: "Copay ready"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        Assert.Contains("Copay ready", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Copay collection is not configured for this appointment.", cut.Markup, StringComparison.Ordinal);

        var copayButton = Assert.Single(cut.FindAll("button"), button => button.TextContent.Contains("Record Copay", StringComparison.Ordinal));
        Assert.False(copayButton.HasAttribute("disabled"));

        copayButton.Click();

        Assert.Equal("record-copay", requestedAction);
    }

    [Fact]
    public void AppointmentDetailModal_NullIntakeStatus_RendersNeutralDocumentBadge()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var appointment = CreateAppointment(status: "Scheduled", intakeStatus: null);

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, appointment));

        Assert.Contains("Intake pending", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("appointment-detail-modal__status-badge--neutral", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("No Show")]
    [InlineData("Completed")]
    public void AppointmentDetailModal_ClosedAppointment_DisablesPrimaryActionWithReason(string status)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: status)));

        var primaryButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Start Visit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(primaryButtons);
        var reasonId = primaryButtons[0].GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrWhiteSpace(reasonId));
        var reason = cut.Find($"#{reasonId}");
        Assert.Contains("This appointment is closed and cannot be started.", reason.TextContent, StringComparison.Ordinal);

        Assert.All(primaryButtons, button =>
        {
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal(reasonId, button.GetAttribute("aria-describedby"));
        });

        Assert.DoesNotContain(cut.FindAll("button"), button => button.TextContent.Contains("Send Reminder", StringComparison.OrdinalIgnoreCase));

        var editButton = Assert.Single(cut.FindAll("button"), button => button.TextContent.Contains("Edit Appointment", StringComparison.Ordinal));
        Assert.True(editButton.HasAttribute("disabled"));
        Assert.Equal(reasonId, editButton.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void AppointmentDetailModal_EditAppointment_RequestsEditAction()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var requestedAction = string.Empty;

        var cut = RenderComponent<AppointmentDetailModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Appointment, CreateAppointment(status: "Scheduled"))
            .Add(component => component.OnActionRequested, action => requestedAction = action));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Edit Appointment", StringComparison.Ordinal))
            .Click();

        Assert.Equal("edit-appointment", requestedAction);
    }

    [Theory]
    [InlineData("Initial Evaluation", "Evaluation Note")]
    [InlineData("Re-Evaluation", "Progress Note")]
    [InlineData("Discharge", "Discharge Note")]
    [InlineData("Follow Up", "Daily Treatment Note")]
    [InlineData("Wellness Visit", "Daily Treatment Note")]
    public void AppointmentsPage_ResolvesVisitNoteTypeFromAppointmentType(string appointmentType, string expectedNoteType)
    {
        Assert.Equal(expectedNoteType, AppointmentVisitNoteTypeResolver.Resolve(appointmentType));
    }

    [Theory]
    [InlineData("Initial Evaluation", "Evaluation Note", false)]
    [InlineData("Re-Evaluation", "Progress Note", false)]
    [InlineData("Discharge", "Discharge Note", false)]
    [InlineData("Follow Up", "Daily Treatment Note", true)]
    [InlineData("Wellness Visit", "Daily Treatment Note", true)]
    [InlineData("Unexpected Consult", "Daily Treatment Note", false)]
    public void AppointmentsPage_ResolvesVisitNoteIntentFromAppointmentType(
        string appointmentType,
        string expectedNoteType,
        bool expectedEvaluationFallback)
    {
        var result = AppointmentVisitNoteTypeResolver.ResolveIntent(appointmentType);

        Assert.Equal(expectedNoteType, result.WorkspaceNoteType);
        Assert.Equal(expectedEvaluationFallback, result.AllowEvaluationFallback);
    }

    [Fact]
    public void NewAppointmentModal_EmptySubmit_ShowsInlineValidationAndDoesNotCallSubmit()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var submitCalled = false;

        var cut = RenderComponent<NewAppointmentModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ =>
            {
                submitCalled = true;
                return Task.FromResult(true);
            }));

        cut.Find("form").Submit();

        Assert.False(submitCalled);
        Assert.Contains("Review required appointment details.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Select a patient.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Select an appointment type.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Select a clinician.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#patient").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("#appointmentType").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("#clinician").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void NewAppointmentModal_WhenParentFails_KeepsModalOpenAndPreservesValues()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<NewAppointmentModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.AvailablePatients, new List<NewAppointmentModal.PatientOption>
            {
                new() { Id = "patient-1", Name = "Alex Patient" }
            })
            .Add(component => component.AvailableClinicians, new List<NewAppointmentModal.ClinicianOption>
            {
                new() { Id = "clinician-1", Name = "Dr. Taylor" }
            })
            .Add(component => component.OnSubmit, _ => Task.FromResult(false)));

        cut.Find("#patient").Change("patient-1");
        cut.Find("#appointmentType").Change("Follow Up");
        cut.Find("#clinician").Change("clinician-1");
        cut.Find("#notes").Change("Keep this note");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to save the appointment.", cut.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll(".modal-container"));
            Assert.Contains("Keep this note", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentFiltersPanel_ExposesExpandedAndControlsAttributes()
    {
        var cut = RenderComponent<AppointmentFiltersPanel>(parameters => parameters
            .Add(component => component.Filter, new AppointmentsFilter()));

        var appointmentTypeButton = cut.Find("button[aria-controls='appointment-type-filter-options']");
        Assert.Equal("false", appointmentTypeButton.GetAttribute("aria-expanded"));
        Assert.Equal("true", appointmentTypeButton.GetAttribute("aria-haspopup"));

        appointmentTypeButton.Click();

        appointmentTypeButton = cut.Find("button[aria-controls='appointment-type-filter-options']");
        Assert.Equal("true", appointmentTypeButton.GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("#appointment-type-filter-options"));
    }

    [Fact]
    public void AppointmentsRightColumn_FilteredEmptyState_RendersClearFiltersAction()
    {
        var clearCalled = false;

        var cut = RenderComponent<AppointmentsRightColumn>(parameters => parameters
            .Add(component => component.ShowEmptyState, true)
            .Add(component => component.HasActiveFilters, true)
            .Add(component => component.EmptyStateTitle, "No appointments match the current filters")
            .Add(component => component.EmptyStateDescription, "Clear filters or adjust the selected date to view appointments.")
            .Add(component => component.OnClearFilters, () => { clearCalled = true; }));

        Assert.Contains("No appointments match the current filters", cut.Markup, StringComparison.Ordinal);

        cut.Find(".appointments-empty-state__action").Click();

        Assert.True(clearCalled);
    }

    [Fact]
    public void AppointmentsViewTabs_ExposeSelectedNavigationState()
    {
        var cut = RenderComponent<AppointmentsViewTabs>(parameters => parameters
            .Add(component => component.SelectedView, AppointmentsView.Week));

        var links = cut.FindAll(".tab-button");
        Assert.Null(links[0].GetAttribute("aria-current"));
        Assert.Equal("page", links[1].GetAttribute("aria-current"));
        Assert.Empty(cut.FindAll("[role='tab']"));
    }

    [Fact]
    public void AppointmentsDaySwitcher_WeekView_ShowsWeekRangeAndWeekNavigationLabels()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var nonUsCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentCulture = nonUsCulture;
            CultureInfo.CurrentUICulture = nonUsCulture;

            var cut = RenderComponent<AppointmentsDaySwitcher>(parameters => parameters
                .Add(component => component.SelectedDate, new DateTime(2026, 6, 9))
                .Add(component => component.SelectedView, AppointmentsView.Week));

            Assert.Contains("juin 7 - 13, 2026", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("13/06/2026", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Week Schedule", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("Previous week", cut.FindAll("button")[0].GetAttribute("aria-label"));
            Assert.Equal("Next week", cut.FindAll("button")[1].GetAttribute("aria-label"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ClinicianScheduler_WeekView_RendersClinicianNameOnAppointmentCard()
    {
        var selectedDate = DateTime.Today;
        var localStart = selectedDate.AddHours(9);

        var cut = RenderComponent<ClinicianScheduler>(parameters => parameters
            .Add(component => component.SelectedDate, selectedDate)
            .Add(component => component.View, AppointmentsView.Week)
            .Add(component => component.WeekGrouping, AppointmentsWeekGrouping.Day)
            .Add(component => component.Clinicians, new List<ClinicianSchedule>
            {
                new() { Name = "Dr. Taylor", AppointmentCount = 1 }
            })
            .Add(component => component.Appointments, new List<AppointmentListItemResponse>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Alex Patient",
                    MedicalRecordNumber = "PT-123456",
                    ClinicianId = Guid.NewGuid(),
                    ClinicianName = "Taylor",
                    StartTimeUtc = localStart.ToUniversalTime(),
                    EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow Up",
                    AppointmentStatus = "Scheduled",
                    VisitWorkflowStatus = string.Empty,
                    IntakeStatus = "Completed",
                    Notes = "Follow up."
                }
            }));

        Assert.Contains("Alex Patient", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Dr. Taylor", cut.Find(".appointment-clinician").TextContent.Trim());
    }

    private static AppointmentDetailViewModel CreateAppointment(
        string status,
        string? visitWorkflowStatus = null,
        string? intakeStatus = "Completed",
        Guid? visitNoteId = null,
        bool canRecordCopay = false,
        string copayStatusLabel = "Copay not configured")
    {
        return new AppointmentDetailViewModel
        {
            AppointmentId = Guid.NewGuid(),
            VisitNoteId = visitNoteId,
            PatientRecordId = Guid.NewGuid(),
            PatientName = "Alex Patient",
            PatientId = "PT-123456",
            ClinicianId = Guid.NewGuid().ToString(),
            ClinicianName = "Dr. Taylor",
            AppointmentDate = new DateTime(2026, 5, 19),
            StartTime = new TimeOnly(9, 0),
            DurationMinutes = 45,
            AppointmentType = "Follow Up",
            AppointmentStatus = status,
            VisitWorkflowStatus = visitWorkflowStatus ?? string.Empty,
            IntakeStatus = intakeStatus,
            CanRecordCopay = canRecordCopay,
            CopayStatusLabel = copayStatusLabel,
            Notes = "Shoulder mobility follow-up."
        };
    }
}
