using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.UI.Components.Dashboard;

namespace PTDoc.Tests.UI.Dashboard;

[Trait("Category", "CoreCi")]
public sealed class NotificationsSectionTests : TestContext
{
    [Fact]
    public void NotificationsSection_RendersAlertCards_WithPriorityMetadataAndActions()
    {
        var alerts = CreateAlerts();

        var cut = RenderComponent<NotificationsSection>(parameters => parameters
            .Add(component => component.Alerts, alerts));

        Assert.Equal(4, cut.FindAll("[data-testid='dashboard-alert-card']").Count);
        Assert.Contains("3 Urgent", cut.Markup);
        Assert.Contains("Note Due Today", cut.Markup);
        Assert.Contains("Authorization Expiring", cut.Markup);
        Assert.Contains("Incomplete Intake", cut.Markup);
        Assert.Contains("Unsigned Note", cut.Markup);
        Assert.Contains("Notes", cut.Markup);
        Assert.Contains("Authorization", cut.Markup);
        Assert.Contains("Intake", cut.Markup);
        Assert.Contains("Unsigned items", cut.Markup);
        Assert.Contains("dashboard-alerts-authorization", cut.Markup);
        Assert.Contains("High Priority", cut.Markup);
        Assert.Contains("Medium", cut.Markup);
        Assert.Contains("Emily Rodriguez", cut.Markup);
        Assert.Contains("ID: PT003", cut.Markup);
        Assert.Contains("Due:", cut.Markup);
        Assert.All(cut.FindAll("[data-testid='dashboard-alert-group']"), group =>
            Assert.Equal("group", group.GetAttribute("role")));

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.Find("button[aria-label='Start for Michael Chen']").Click();

        Assert.EndsWith($"/patient/{alerts[0].PatientId:D}/new-note", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationsSection_CollapsesAndExpandsAlertList()
    {
        var cut = RenderComponent<NotificationsSection>(parameters => parameters
            .Add(component => component.Alerts, CreateAlerts()));

        cut.Find("button[aria-label='Collapse alerts']").Click();

        Assert.Empty(cut.FindAll("[data-testid='dashboard-alert-card']"));

        cut.Find("button[aria-label='Expand alerts']").Click();

        Assert.Equal(4, cut.FindAll("[data-testid='dashboard-alert-card']").Count);
    }

    [Fact]
    public void NotificationsSection_DismissesAlertsForCurrentComponentSessionOnly()
    {
        var cut = RenderComponent<NotificationsSection>(parameters => parameters
            .Add(component => component.Alerts, CreateAlerts().Take(1).ToList()));

        cut.Find("button[aria-label='Dismiss Note Due Today for Michael Chen']").Click();

        Assert.Empty(cut.FindAll("[data-testid='dashboard-alert-card']"));
        Assert.Contains("0 Urgent", cut.Markup);
        Assert.Contains("No active clinical alerts", cut.Markup);
    }

    [Fact]
    public void NotificationsSection_ShowsEmptyState_WhenNoAlertsAreVisible()
    {
        var cut = RenderComponent<NotificationsSection>(parameters => parameters
            .Add(component => component.Alerts, Array.Empty<DashboardAlertItemResponse>()));

        Assert.Contains("No active clinical alerts", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='dashboard-alert-card']"));
    }

    private static List<DashboardAlertItemResponse> CreateAlerts()
    {
        var patientId = Guid.NewGuid();
        return
        [
            new()
            {
                Id = "notesDueToday:appt-1",
                Kind = "notesDueToday",
                Priority = "high",
                Title = "Note Due Today",
                Message = "Today's appointment needs a signed note.",
                PatientId = patientId,
                PatientName = "Michael Chen",
                PatientMedicalRecordNumber = "PT002",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-1),
                DueDateUtc = DateTime.UtcNow.Date,
                TargetUrl = $"/patient/{patientId:D}/new-note",
                ActionLabel = "Start",
                IsUrgent = true
            },
            new()
            {
                Id = "authorizationExpiration:patient-1",
                Kind = "authorizationExpiration",
                Priority = "medium",
                Title = "Authorization Expiring",
                Message = "Authorization coverage is nearing its end date.",
                PatientId = Guid.NewGuid(),
                PatientName = "Ari Auth",
                PatientMedicalRecordNumber = "PT004",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-3),
                DueDateUtc = DateTime.UtcNow.Date.AddDays(5),
                TargetUrl = "/patient/patient-4/info",
                ActionLabel = "Review Auth",
                IsUrgent = true
            },
            new()
            {
                Id = "incompleteIntake:intake-1",
                Kind = "incompleteIntake",
                Priority = "high",
                Title = "Incomplete Intake",
                Message = "Patient has not completed intake form.",
                PatientId = Guid.NewGuid(),
                PatientName = "Emily Rodriguez",
                PatientMedicalRecordNumber = "PT003",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-2),
                TargetUrl = "/intake/patient-1",
                ActionLabel = "Open Intake",
                IsUrgent = true
            },
            new()
            {
                Id = "unsignedNote:note-1",
                Kind = "unsignedNote",
                Priority = "medium",
                Title = "Unsigned Note",
                Message = "Daily note is due today.",
                PatientId = Guid.NewGuid(),
                PatientName = "Sarah Johnson",
                PatientMedicalRecordNumber = "PT001",
                Timestamp = DateTimeOffset.UtcNow.AddHours(-4),
                DueDateUtc = DateTime.UtcNow.Date,
                TargetUrl = "/patient/patient-2/note/note-1",
                ActionLabel = "Open Note",
                IsUrgent = false
            }
        ];
    }
}
