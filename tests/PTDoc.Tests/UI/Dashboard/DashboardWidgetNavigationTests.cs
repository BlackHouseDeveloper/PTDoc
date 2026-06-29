using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.Dashboard;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Dashboard;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Dashboard;

[Trait("Category", "CoreCi")]
public sealed class DashboardWidgetNavigationTests : TestContext
{
    [Fact]
    public void OverviewSection_NavigatesOverviewCards_ToUsefulWorkflowRoutes()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<OverviewSection>(parameters => parameters
            .Add(component => component.PatientsToday, 3)
            .Add(component => component.Appointments, 4)
            .Add(component => component.NotesDue, 2)
            .Add(component => component.Drafts, 5)
            .Add(component => component.Unsigned, 6)
            .Add(component => component.Intakes, 7));

        cut.Find("button[aria-label=\"Open today's appointments\"]").Click();
        Assert.EndsWith("/appointments", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label=\"Open appointments\"]").Click();
        Assert.EndsWith("/appointments", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label=\"Open appointments needing notes today\"]").Click();
        Assert.EndsWith("/appointments?needsNote=true&dateRange=today", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label=\"Open draft notes\"]").Click();
        Assert.EndsWith("/notes?status=Draft", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label=\"Open unsigned notes\"]").Click();
        Assert.EndsWith("/notes?status=Unsigned", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button[aria-label=\"Open intake work queue\"]").Click();
        Assert.EndsWith("/intake", navigation.Uri, StringComparison.Ordinal);

        Assert.DoesNotContain("All Alerts", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", cut.Markup, StringComparison.Ordinal);
    }

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
    public void RecentlyEditedPOCsCard_RendersData_AndNavigatesToSupportedRoutes()
    {
        var noteId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = RenderComponent<RecentlyEditedPOCsCard>(parameters => parameters
            .Add(component => component.Items, new List<DashboardPlanOfCareSummaryResponse>
            {
                new()
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Sarah Johnson",
                    Title = "Evaluation Plan of Care",
                    Status = "Draft",
                    LastEditedAt = DateTime.UtcNow.AddMinutes(-5),
                    LastEditedBy = "Calvin Carter",
                    TargetUrl = $"/patient/{patientId:D}/note/{noteId:D}",
                    IcdCount = 2,
                    Sessions = 3
                }
            }));

        Assert.Contains("Recently Edited Plan of Care", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Sarah Johnson", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Evaluation Plan of Care", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("edited by Calvin Carter", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("ICD", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("CPT units", cut.Markup, StringComparison.Ordinal);

        cut.Find("button.poc-view-button").Click();
        Assert.EndsWith($"/patient/{patientId:D}/note/{noteId:D}", navigation.Uri, StringComparison.Ordinal);

        cut.Find("button.poc-view-all").Click();
        Assert.EndsWith("/notes", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentlyEditedPOCsCard_UsesEmptyState_AndOmitsUnsupportedMetrics()
    {
        var empty = RenderComponent<RecentlyEditedPOCsCard>(parameters => parameters
            .Add(component => component.Items, Array.Empty<DashboardPlanOfCareSummaryResponse>()));

        Assert.Contains("No recently edited plans of care", empty.Markup, StringComparison.Ordinal);

        var withoutMetrics = RenderComponent<RecentlyEditedPOCsCard>(parameters => parameters
            .Add(component => component.Items, new List<DashboardPlanOfCareSummaryResponse>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    PatientName = "Michael Chen",
                    Title = "Progress Note Plan of Care",
                    Status = "Signed",
                    LastEditedAt = DateTime.UtcNow,
                    TargetUrl = "/patient/test/note/test"
                }
            }));

        Assert.Empty(withoutMetrics.FindAll(".poc-metrics"));
        Assert.DoesNotContain("Utilization", withoutMetrics.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRightColumn_RendersRecentlyEditedPOCs_InPlaceOfRecentNotes()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        authorization.SetPolicies(
            AuthorizationPolicies.ClinicalStaff,
            AuthorizationPolicies.PatientWrite,
            AuthorizationPolicies.IntakeWrite);
        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton(Mock.Of<IPatientService>());
        Services.AddSingleton(Mock.Of<IIntakeService>());
        Services.AddSingleton(Mock.Of<IIntakeDeliveryService>());
        Services.AddSingleton<IDashboardAlertService>(new StaticDashboardAlertService(new DashboardSnapshotResponse
        {
            Overview = new DashboardOverviewCountsResponse
            {
                PatientsToday = 1
            },
            RecentNotes =
            [
                new NoteListItemApiResponse
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    PatientName = "Legacy Note Patient",
                    NoteType = "Daily",
                    LastModifiedUtc = DateTime.UtcNow
                }
            ],
            RecentPlansOfCare =
            [
                new DashboardPlanOfCareSummaryResponse
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    PatientName = "POC Patient",
                    Title = "Evaluation Plan of Care",
                    Status = "Draft",
                    LastEditedAt = DateTime.UtcNow,
                    TargetUrl = "/patient/test/note/test"
                }
            ]
        }));

        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
        var root = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<PTDoc.UI.Pages.Dashboard>(3);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        root.WaitForAssertion(() =>
        {
            Assert.Contains("Recently Edited Plan of Care", root.Markup, StringComparison.Ordinal);
            Assert.Contains("POC Patient", root.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Recent Notes", root.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Legacy Note Patient", root.Markup, StringComparison.Ordinal);
            Assert.True(
                root.Markup.IndexOf("Recent Activity", StringComparison.Ordinal) <
                root.Markup.IndexOf("Recently Edited Plan of Care", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void AddPatientSuccess_OpensSendIntakeModalWithCreatedPatientPreselected()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        authorization.SetPolicies(
            AuthorizationPolicies.ClinicalStaff,
            AuthorizationPolicies.PatientWrite,
            AuthorizationPolicies.IntakeWrite);

        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var createdPatient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Morgan",
            LastName = "Dashboard",
            Email = "morgan.dashboard@example.com",
            Phone = "555-0199",
            DateOfBirth = new DateTime(1991, 5, 6)
        };
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        CreatePatientRequest? capturedCreateRequest = null;
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreatePatientRequest, CancellationToken>((request, _) => capturedCreateRequest = request)
            .ReturnsAsync(createdPatient);

        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.SearchEligiblePatientsAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        intakeService
            .Setup(service => service.EnsureDraftAsync(patientId, It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IntakeEnsureDraftResult.Created(new IntakeResponseDraft
            {
                IntakeId = intakeId,
                PatientId = patientId
            }));

        var intakeDeliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        intakeDeliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });

        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton<IPatientService>(patientService.Object);
        Services.AddSingleton<IIntakeService>(intakeService.Object);
        Services.AddSingleton<IIntakeDeliveryService>(intakeDeliveryService.Object);
        Services.AddSingleton<IDashboardAlertService>(new StaticDashboardAlertService(new DashboardSnapshotResponse
        {
            Overview = new DashboardOverviewCountsResponse
            {
                PatientsToday = 1
            }
        }));

        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();
        var root = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<PTDoc.UI.Pages.Dashboard>(3);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        root.WaitForAssertion(() => Assert.Contains("Recent Activity", root.Markup, StringComparison.Ordinal));
        root.WaitForElement("button[aria-label='Add new patient']");
        root.Find("button[aria-label='Add new patient']").Click();
        root.Find("#firstName").Change("Morgan");
        root.Find("#lastName").Change("Dashboard");
        root.Find("#email").Change("morgan.dashboard@example.com");
        root.Find("#phone").Change("555-0199");
        root.Find("#dob").Change("1991-05-06");
        root.Find("#referringPhysician").Change(" ");
        root.Find("#authorizationNumber").Change(" ");
        root.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Patient + Send Intake", StringComparison.Ordinal))
            .Click();

        root.WaitForAssertion(() =>
        {
            Assert.Contains("Send Intake Form", root.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Add New Patient", root.Markup, StringComparison.Ordinal);
            Assert.Equal(patientId.ToString("D"), root.Find("#patient-select").GetAttribute("value"));
            Assert.Equal("morgan.dashboard@example.com", root.Find("#email").GetAttribute("value"));
            Assert.Equal("555-0199", root.Find("#phone").GetAttribute("value"));
        });

        patientService.Verify(service => service.CreateAsync(
            It.Is<CreatePatientRequest>(request =>
                request.FirstName == "Morgan" &&
                request.LastName == "Dashboard" &&
                request.Email == "morgan.dashboard@example.com" &&
                request.Phone == "555-0199" &&
                request.DateOfBirth == new DateTime(1991, 5, 6)),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedCreateRequest);
        Assert.Null(capturedCreateRequest!.ReferringPhysician);
        Assert.Null(capturedCreateRequest.AuthorizationNumber);
        intakeDeliveryService.Verify(
            service => service.SendInviteAsync(It.IsAny<IntakeSendInviteRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    private sealed class StaticDashboardAlertService(DashboardSnapshotResponse snapshot) : IDashboardAlertService
    {
        public Task<DashboardAlertsResponse> GetAlertsAsync(int take = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardAlertsResponse());

        public Task<DashboardSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
