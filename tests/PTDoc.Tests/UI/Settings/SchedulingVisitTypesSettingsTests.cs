using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class SchedulingVisitTypesSettingsTests : TestContext
{
    public SchedulingVisitTypesSettingsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("admin-user");
        authorization.SetRoles(Roles.Admin);
    }

    [Fact]
    public void DefaultState_RendersDocumentedVisitTypesAndSharedActions()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        Assert.Equal(12, cut.FindAll(".scheduled-item").Count);
        Assert.Equal("true", cut.Find("#visit-types-tab").GetAttribute("aria-selected"));
        Assert.Equal("0", cut.Find("#visit-types-tab").GetAttribute("tabindex"));
        Assert.All(
            cut.FindAll(".scheduling-settings__tab").Where(tab => tab.GetAttribute("id") != "visit-types-tab"),
            tab => Assert.Equal("-1", tab.GetAttribute("tabindex")));
        Assert.Null(cut.Find("#scheduling-settings-panel").GetAttribute("tabindex"));
        Assert.Contains("Initial Evaluation", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Requires Intake", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Consultation (Non-Billable)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Auto Check-In Messaging", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Save Changes", cut.Find(".scheduling-settings__action-bar").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find(".scheduling-panel__add").HasAttribute("disabled"));
        Assert.All(cut.FindAll(".scheduled-item__actions button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.All(cut.FindAll(".quick-admin-tools__row"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Fact]
    public void Tabs_RenderEachDocumentedPanelState()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        cut.Find("#schedule-blocks-tab").Click();
        Assert.Equal("0", cut.Find("#schedule-blocks-tab").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("#visit-types-tab").GetAttribute("tabindex"));
        Assert.Equal(4, cut.FindAll(".scheduled-item").Count);
        Assert.Equal(3, cut.FindAll(".scheduled-item__badge").Count);
        Assert.Contains("Team Meeting", cut.Markup, StringComparison.Ordinal);

        cut.Find("#calendar-behavior-tab").Click();
        Assert.Equal(6, cut.FindAll("button[role='switch']").Count);
        Assert.Equal("45 minutes", cut.Find("#default-appointment-duration").GetAttribute("value"));
        Assert.Equal("false", cut.Find("button[aria-label='Allow Double Booking']").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Auto-Confirm Appointments']").GetAttribute("aria-checked"));

        cut.Find("#clinic-hours-tab").Click();
        Assert.Equal(7, cut.FindAll(".clinic-hours__row").Count);
        Assert.Contains("Clinic time zone", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Lunch end", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("24 hours before", cut.Find("#send-reminder").GetAttribute("value"));
        Assert.Equal("true", cut.Find("button[aria-label='Send Appointment Reminders']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void CalendarAndReminderToggles_UpdateLocalVisibleState()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        cut.Find("#calendar-behavior-tab").Click();
        cut.Find("button[aria-label='Allow Double Booking']").Click();
        Assert.Equal("true", cut.Find("button[aria-label='Allow Double Booking']").GetAttribute("aria-checked"));

        cut.Find("#clinic-hours-tab").Click();
        cut.Find("button[aria-label='Send Appointment Reminders']").Click();
        Assert.Equal("false", cut.Find("button[aria-label='Send Appointment Reminders']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void ActionBar_ResetRestoresDefaultsAndCancelRestoresLastSavedSnapshot()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();
        cut.Find("#calendar-behavior-tab").Click();

        cut.Find("#default-appointment-duration").Change("60 minutes");
        cut.Find("#intake-sent-color").Input("#123456");
        cut.Find("button[aria-label='Allow Double Booking']").Click();
        FindAction(cut, "Save Changes").Click();
        Assert.Contains("Scheduling values updated locally", cut.Markup, StringComparison.Ordinal);

        cut.Find("#default-appointment-duration").Change("30 minutes");
        cut.Find("#intake-sent-color").Input("#abcdef");
        cut.Find("button[aria-label='Allow Double Booking']").Click();
        FindAction(cut, "Cancel").Click();

        Assert.Equal("60 minutes", cut.Find("#default-appointment-duration").GetAttribute("value"));
        Assert.Equal("#123456", cut.Find("#intake-sent-color").GetAttribute("value"));
        Assert.Equal("true", cut.Find("button[aria-label='Allow Double Booking']").GetAttribute("aria-checked"));
        Assert.Contains("Unsaved scheduling changes were discarded", cut.Markup, StringComparison.Ordinal);

        FindAction(cut, "Reset to Default").Click();

        Assert.Equal("45 minutes", cut.Find("#default-appointment-duration").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#intake-sent-color").GetAttribute("value"));
        Assert.Equal("false", cut.Find("button[aria-label='Allow Double Booking']").GetAttribute("aria-checked"));
        Assert.Contains("Canonical defaults applied locally", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabs_SupportKeyboardNavigationAndReferenceTheRenderedPanel()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        Assert.All(
            cut.FindAll("[role='tab']"),
            tab => Assert.Equal("scheduling-settings-panel", tab.GetAttribute("aria-controls")));
        Assert.Equal("visit-types-tab", cut.Find("#scheduling-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#visit-types-tab").KeyDown("ArrowRight");

        Assert.Equal("true", cut.Find("#schedule-blocks-tab").GetAttribute("aria-selected"));
        Assert.Equal("0", cut.Find("#schedule-blocks-tab").GetAttribute("tabindex"));
        Assert.Equal("schedule-blocks-tab", cut.Find("#scheduling-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#schedule-blocks-tab").KeyDown("End");

        Assert.Equal("true", cut.Find("#clinic-hours-tab").GetAttribute("aria-selected"));
        Assert.Equal("clinic-hours-tab", cut.Find("#scheduling-settings-panel").GetAttribute("aria-labelledby"));

        cut.Find("#clinic-hours-tab").KeyDown("Home");

        Assert.Equal("true", cut.Find("#visit-types-tab").GetAttribute("aria-selected"));

        cut.Find("#visit-types-tab").KeyDown("ArrowLeft");

        Assert.Equal("true", cut.Find("#clinic-hours-tab").GetAttribute("aria-selected"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EditBlock_PreservesScopeWeekdaysAndEffectiveDates(bool recurring)
    {
        var service = RegisterScheduling();
        var block = new ScheduleBlockDto(Guid.NewGuid(), Guid.NewGuid(), "Scoped block", "admin",
            WeekdayFlags.Tuesday | WeekdayFlags.Thursday, new TimeOnly(10, 0), new TimeOnly(11, 0),
            new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30), recurring, true, 9);
        service.Setup(item => item.GetScheduleBlocksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([block]);
        SaveScheduleBlockRequest? saved = null;
        service.Setup(item => item.UpdateScheduleBlockAsync(It.IsAny<Guid>(), block.Id, It.IsAny<SaveScheduleBlockRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, SaveScheduleBlockRequest, Guid, string, CancellationToken>((_, _, request, _, _, _) => saved = request)
            .ReturnsAsync(SettingsOperationResult<ScheduleBlockDto>.Success(block with { Name = "Renamed" }));
        var cut = RenderComponent<SchedulingVisitTypesSettings>();
        cut.Find("#schedule-blocks-tab").Click();
        cut.Find("button[aria-label='Edit Scoped block schedule block']").Click();
        cut.Find("#scheduling-editor input").Input("Renamed");
        cut.Find("#scheduling-editor .scheduling-settings__save-action").Click();

        cut.WaitForAssertion(() => Assert.NotNull(saved));
        Assert.Equal("Renamed", saved!.Name);
        Assert.Equal(block.ClinicianId, saved.ClinicianId);
        Assert.Equal(block.Weekdays, saved.Weekdays);
        Assert.Equal(block.EffectiveStartDate, saved.EffectiveStartDate);
        Assert.Equal(block.EffectiveEndDate, saved.EffectiveEndDate);
        Assert.Equal(block.IsRecurring, saved.IsRecurring);
        Assert.Equal(block.Version, saved.ExpectedVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialSave_CancelRestoresCommittedPreferencesAndOriginalHours(bool transportFailure)
    {
        var service = RegisterScheduling();
        var requests = new List<UpdateSchedulingPreferencesRequest>();
        var hoursRequests = new List<UpdateClinicHoursRequest>();
        service.Setup(item => item.UpdatePreferencesAsync(It.IsAny<Guid>(), It.IsAny<UpdateSchedulingPreferencesRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateSchedulingPreferencesRequest, Guid, string, CancellationToken>((_, request, _, _, _) => requests.Add(request))
            .ReturnsAsync(SettingsOperationResult<SchedulingPreferencesDto>.Success(Preferences with { DefaultAppointmentDurationMinutes = 60, Version = 2 }));
        service.Setup(item => item.UpdateClinicHoursAsync(It.IsAny<Guid>(), It.IsAny<UpdateClinicHoursRequest>(),
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateClinicHoursRequest, Guid, string, CancellationToken>((_, request, _, _, _) => hoursRequests.Add(request))
            .Returns(() => transportFailure
                ? Task.FromException<SettingsOperationResult<ClinicHoursDto>>(new HttpRequestException())
                : Task.FromResult(SettingsOperationResult<ClinicHoursDto>.Conflict()));
        var cut = RenderComponent<SchedulingVisitTypesSettings>();
        cut.Find("#calendar-behavior-tab").Click();
        cut.Find("#default-appointment-duration").Change("60 minutes");
        cut.Find("#clinic-hours-tab").Click();
        cut.Find("#clinic-time-zone").Input("America/New_York");
        FindAction(cut, "Save Changes").Click();
        cut.WaitForAssertion(() => Assert.Contains("preferences saved, but clinic hours", cut.Markup, StringComparison.Ordinal));
        Assert.Equal("America/New_York", cut.Find("#clinic-time-zone").GetAttribute("value"));
        FindAction(cut, "Cancel").Click();
        Assert.Equal("America/Los_Angeles", cut.Find("#clinic-time-zone").GetAttribute("value"));
        cut.Find("#calendar-behavior-tab").Click();
        Assert.Equal("60 minutes", cut.Find("#default-appointment-duration").GetAttribute("value"));
        FindAction(cut, "Save Changes").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, requests.Count));
        Assert.Equal(2, requests[1].ExpectedVersion);
        Assert.Equal(1, hoursRequests[1].ExpectedClinicVersion);
    }

    [Fact]
    public void FailedCatalogLoad_DisablesPlaceholderMutationsUntilReload()
    {
        var service = RegisterScheduling();
        var visit = new VisitTypeDto(Guid.NewGuid(), "eval", "Evaluation", 45, true, false, true, true, 0, 1);
        service.SetupSequence(item => item.GetVisitTypesAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException()).ReturnsAsync([visit]);
        var cut = RenderComponent<SchedulingVisitTypesSettings>();
        Assert.All(cut.FindAll(".scheduled-item__actions button"), button => Assert.True(button.HasAttribute("disabled")));
        cut.FindAll("button").Single(button => button.TextContent == "Reload scheduling settings").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".scheduled-item")));
        Assert.All(cut.FindAll(".scheduled-item__actions button"), button => Assert.False(button.HasAttribute("disabled")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchedulingDialogs_RegisterModalHelperAndReleaseOnEscape(bool deactivate)
    {
        var service = RegisterScheduling();
        var visit = new VisitTypeDto(Guid.NewGuid(), "eval", "Evaluation", 45, true, false, true, true, 0, 1);
        service.Setup(item => item.GetVisitTypesAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync([visit]);
        var modal = JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");
        modal.Mode = JSRuntimeMode.Loose;
        var cut = RenderComponent<SchedulingVisitTypesSettings>();
        cut.Find($"button[aria-label='{(deactivate ? "Delete" : "Edit")} Evaluation visit type']").Click();
        var id = deactivate ? "scheduling-deactivation" : "scheduling-editor";
        cut.WaitForAssertion(() => Assert.Contains(modal.Invocations, call => call.Identifier == "registerEscapeHandler" && Equals(call.Arguments[0], id)));
        await cut.InvokeAsync(() => cut.Instance.CloseFromJs());
        cut.WaitForAssertion(() => Assert.Contains(modal.Invocations, call => call.Identifier == "unregisterEscapeHandler" && Equals(call.Arguments[0], id)));
        Assert.Empty(cut.FindAll(".scheduling-dialog"));
    }

    private static readonly SchedulingPreferencesDto Preferences = new(45, 15, false, true, true, true, true, true,
        "Week", "AllDay", null, null, null, true, 24, 1);

    private Mock<ISchedulingAdministrationService> RegisterScheduling()
    {
        var service = new Mock<ISchedulingAdministrationService>();
        service.Setup(item => item.GetVisitTypesAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.Setup(item => item.GetScheduleBlocksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.Setup(item => item.GetPreferencesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Preferences);
        service.Setup(item => item.GetClinicHoursAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicHoursDto("America/Los_Angeles", 1, []));
        Services.AddSingleton(service.Object);
        return service;
    }

    private static AngleSharp.Dom.IElement FindAction(IRenderedComponent<SchedulingVisitTypesSettings> cut, string label) =>
        cut.FindAll(".scheduling-settings__action-bar button")
            .Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));
}
