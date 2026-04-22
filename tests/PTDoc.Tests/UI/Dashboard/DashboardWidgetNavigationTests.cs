using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Dashboard;
using PTDoc.UI.Components.Dashboard;

namespace PTDoc.Tests.UI.Dashboard;

[Trait("Category", "CoreCi")]
public sealed class DashboardWidgetNavigationTests : TestContext
{
    [Fact]
    public void RecentActivityCard_UsesCanonicalPatientProfileRoute()
    {
        var cut = RenderComponent<RecentActivityCard>(parameters => parameters
            .Add(component => component.Activities, new List<RecentActivity>
            {
                new()
                {
                    Id = "activity-1",
                    Type = ActivityType.NoteUpdated,
                    Description = "Updated daily note",
                    PatientId = "patient-123",
                    PatientName = "Amelia Adams",
                    Timestamp = DateTime.UtcNow
                }
            }));

        var patientLink = cut.Find("a.activity-patient-link");

        Assert.Equal("/patient/patient-123", patientLink.GetAttribute("href"));
    }

    [Fact]
    public void ExpiringAuthorizationsWidget_UsesPatientIdForLinks_AndUpdateNavigation()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<ExpiringAuthorizationsWidget>(parameters => parameters
            .Add(component => component.Authorizations, new List<ExpiringAuthorization>
            {
                new()
                {
                    Id = "auth-789",
                    PatientId = "patient-456",
                    PatientName = "Amelia Adams",
                    MedicalRecordNumber = "MRN-100",
                    ExpirationDate = DateTime.UtcNow.AddDays(5),
                    VisitsUsed = 4,
                    VisitsTotal = 12,
                    Payer = "Aetna",
                    Urgency = AuthorizationUrgency.Medium
                }
            }));

        var patientLink = cut.Find("a.patient-link");
        Assert.Equal("/patient/patient-456", patientLink.GetAttribute("href"));

        cut.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Update", StringComparison.Ordinal))
            .Click();

        Assert.EndsWith("/patient/patient-456/info", navigation.Uri, StringComparison.Ordinal);
    }
}
