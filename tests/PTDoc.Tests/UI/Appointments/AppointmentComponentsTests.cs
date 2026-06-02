using Bunit;
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

        startVisitButtons[0].Click();

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

        var reason = cut.Find(".appointment-detail-modal__quick-action-reason");
        Assert.Contains("This appointment is closed and cannot be started.", reason.TextContent, StringComparison.Ordinal);

        var primaryButtons = cut.FindAll("button").Where(button => button.TextContent.Contains("Start Visit", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(primaryButtons);
        Assert.All(primaryButtons, button =>
        {
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal(reason.Id, button.GetAttribute("aria-describedby"));
        });

        Assert.All(
            cut.FindAll("button").Where(button =>
                button.TextContent.Contains("Send Reminder", StringComparison.Ordinal)
                || button.TextContent.Contains("Reschedule", StringComparison.Ordinal)),
            button =>
            {
                Assert.True(button.HasAttribute("disabled"));
                Assert.Equal(reason.Id, button.GetAttribute("aria-describedby"));
            });
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
    public void AppointmentsViewTabs_ExposeSelectedTabState()
    {
        var cut = RenderComponent<AppointmentsViewTabs>(parameters => parameters
            .Add(component => component.SelectedView, AppointmentsView.Week));

        var tabs = cut.FindAll("[role='tab']");
        Assert.Equal("false", tabs[0].GetAttribute("aria-selected"));
        Assert.Equal("true", tabs[1].GetAttribute("aria-selected"));
    }

    private static AppointmentDetailViewModel CreateAppointment(string status)
    {
        return new AppointmentDetailViewModel
        {
            AppointmentId = Guid.NewGuid(),
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
            IntakeStatus = "Completed",
            Notes = "Shoulder mobility follow-up."
        };
    }
}
