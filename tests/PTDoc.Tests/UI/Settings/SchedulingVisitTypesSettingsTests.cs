using Bunit;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class SchedulingVisitTypesSettingsTests : TestContext
{
    [Fact]
    public void DefaultState_RendersDocumentedVisitTypesAndSharedActions()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        Assert.Equal(12, cut.FindAll(".scheduled-item").Count);
        Assert.Equal("true", cut.Find("#visit-types-tab").GetAttribute("aria-selected"));
        Assert.Contains("Initial Evaluation", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Requires Intake", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Consultation (Non-Billable)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Auto Check-In Messaging", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Save Changes", cut.Find(".scheduling-settings__action-bar").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Tabs_RenderEachDocumentedPanelState()
    {
        var cut = RenderComponent<SchedulingVisitTypesSettings>();

        cut.Find("#schedule-blocks-tab").Click();
        Assert.Equal(4, cut.FindAll(".scheduled-item").Count);
        Assert.Equal(3, cut.FindAll(".scheduled-item__badge").Count);
        Assert.Contains("Team Meeting", cut.Markup, StringComparison.Ordinal);

        cut.Find("#calendar-behavior-tab").Click();
        Assert.Equal(6, cut.FindAll("button[role='switch']").Count);
        Assert.Equal("45 minutes", cut.Find("#default-appointment-duration").GetAttribute("value"));
        Assert.Equal("false", cut.Find("button[aria-label='Allow Double Booking']").GetAttribute("aria-checked"));
        Assert.Equal("true", cut.Find("button[aria-label='Auto-Confirm Appointments']").GetAttribute("aria-checked"));

        cut.Find("#clinic-hours-tab").Click();
        Assert.Contains("Clinic Start Time", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Lunch Break End", cut.Markup, StringComparison.Ordinal);
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
}
