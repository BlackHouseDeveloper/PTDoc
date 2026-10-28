using Bunit;
using Bunit.TestDoubles;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.UI.Services;

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
    public async Task AppointmentsPage_AddPatient_CreatesSelectsAndPreservesAppointmentDraft()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var createdPatient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Alex",
            LastName = "New",
            Email = "alex.new@example.com",
            DateOfBirth = new DateTime(1990, 1, 2)
        };
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdPatient);
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);

        RegisterServices(
            configuredPatientService: patientService.Object,
            intakeService: intakeService.Object,
            intakeDeliveryService: deliveryService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?action=appointments.new");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement("#appointmentType").Change("Discharge");
        await cut.InvokeAsync(() => cut.Find("#notes").ChangeAsync(new ChangeEventArgs
        {
            Value = "Preserve this appointment draft."
        }));
        cut.WaitForAssertion(() =>
            Assert.Equal(
                "Preserve this appointment draft.",
                cut.Find("#notes").GetAttribute("value")));
        await cut.InvokeAsync(() => cut.Find(".btn-add-patient").ClickAsync(new MouseEventArgs()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Add New Patient", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("#new-appointment-title"));
        });

        FillAddPatientForm(cut, "Alex", "New", "alex.new@example.com", "1990-01-02");
        cut.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Add Patient", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#new-appointment-title"));
            Assert.DoesNotContain("Add New Patient", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(patientId.ToString("D"), Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#patient")).Value);
            Assert.Equal("Discharge", Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointmentType")).Value);
            Assert.Equal(
                "Preserve this appointment draft.",
                cut.Find("#notes").GetAttribute("value"));
        });

        patientService.Verify(
            service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        intakeService.VerifyNoOtherCalls();
        deliveryService.VerifyNoOtherCalls();
    }

    [Fact]
    public void AppointmentsPage_AddPatientAndSendIntake_WhenPatientCreationFails_DoesNotStartIntake()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Patient persistence failed"));
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);

        RegisterServices(
            configuredPatientService: patientService.Object,
            intakeService: intakeService.Object,
            intakeDeliveryService: deliveryService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?action=appointments.new");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".btn-add-patient").Click();
        FillAddPatientForm(cut, "Casey", "Retry", "casey.retry@example.com", "1988-05-06");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Patient and Send Intake", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The patient was not created.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Add New Patient", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Send Intake Form", cut.Markup, StringComparison.Ordinal);
        });

        patientService.Verify(
            service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        intakeService.VerifyNoOtherCalls();
        deliveryService.VerifyNoOtherCalls();
    }

    [Fact]
    public void AppointmentsPage_AddPatientAndSendIntake_CreatesBeforeSendingAndReturnsToDraft()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var intakeId = Guid.NewGuid();
        var createdPatient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Jamie",
            LastName = "Intake",
            Email = "jamie.intake@example.com",
            DateOfBirth = new DateTime(1986, 3, 4)
        };
        var sequence = new MockSequence();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patientService
            .InSequence(sequence)
            .Setup(service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdPatient);
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .InSequence(sequence)
            .Setup(service => service.SearchEligiblePatientsAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());
        intakeService
            .InSequence(sequence)
            .Setup(service => service.EnsureDraftAsync(patientId, It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IntakeEnsureDraftResult.Created(new IntakeResponseDraft
            {
                IntakeId = intakeId,
                PatientId = patientId
            }));
        var deliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        deliveryService
            .Setup(service => service.GetDeliveryStatusAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryStatusResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteActive = true
            });
        deliveryService
            .Setup(service => service.GetDeliveryBundleAsync(intakeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliveryBundleResponse
            {
                IntakeId = intakeId,
                PatientId = patientId,
                InviteUrl = "https://ptdoc.example/intake/invite",
                QrSvg = "<svg></svg>",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
        deliveryService
            .Setup(service => service.SendInviteAsync(
                It.Is<IntakeSendInviteRequest>(request =>
                    request.IntakeId == intakeId
                    && request.Channel == IntakeDeliveryChannel.Email
                    && request.Destination == "jamie.intake@example.com"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntakeDeliverySendResult
            {
                Success = true,
                IntakeId = intakeId,
                PatientId = patientId,
                Channel = IntakeDeliveryChannel.Email
            });
        var toastService = new Mock<IToastService>(MockBehavior.Loose);

        RegisterServices(
            configuredPatientService: patientService.Object,
            intakeService: intakeService.Object,
            intakeDeliveryService: deliveryService.Object,
            configuredToastService: toastService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?action=appointments.new");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement("#appointmentType").Change("Follow Up");
        cut.Find(".btn-add-patient").Click();
        FillAddPatientForm(cut, "Jamie", "Intake", "jamie.intake@example.com", "1986-03-04");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Patient and Send Intake", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Send Intake Form", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("#new-appointment-title"));
            Assert.Equal(patientId.ToString("D"), Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#patient-select")).Value);
            Assert.Equal("jamie.intake@example.com", cut.Find("#email").GetAttribute("value"));
        });

        cut.Find(".modal-container[aria-labelledby='send-intake-title'] form").Submit();

        cut.WaitForAssertion(() =>
        {
            deliveryService.Verify(
                service => service.SendInviteAsync(It.IsAny<IntakeSendInviteRequest>(), It.IsAny<CancellationToken>()),
                Times.Once);
            toastService.Verify(
                service => service.ShowSuccess("Patient added and intake sent successfully.", null),
                Times.Once);
        });

        patientService.Verify(
            service => service.CreateAsync(It.IsAny<CreatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);

        cut.Find("button[aria-label='Close modal']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#new-appointment-title"));
            Assert.Equal(patientId.ToString("D"), Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#patient")).Value);
            Assert.Equal("Follow Up", Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointmentType")).Value);
        });
    }

    [Fact]
    public void AppointmentsPage_AddPatientAction_RequiresPatientWritePolicy()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterServices(role: Roles.FrontDesk);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?action=appointments.new");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#new-appointment-title"));
            Assert.Empty(cut.FindAll(".btn-add-patient"));
            Assert.DoesNotContain("Add New Patient", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentsPage_DetailsRouteAction_OpensSelectedAppointment()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var appointment = CreateAppointment(
            "Dashboard Patient",
            clinicianId,
            "Taylor PT",
            DateTime.Today.AddHours(9));
        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments = [appointment],
            Clinicians = [new AppointmentClinicianResponse { Id = clinicianId, DisplayName = "Taylor PT" }]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo(
            $"/appointments?dateRange=today&appointmentId={appointment.Id:D}&action=appointments.details");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Appointment Details", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Dashboard Patient", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("true", cut.Find(".appointments-page").GetAttribute("aria-hidden"));
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

    [Fact]
    public void AppointmentsPage_QueryOnlyWeekNavigation_RendersWeekEmptyState()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/appointments");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForAssertion(() => Assert.Contains("No appointments scheduled for this day", cut.Markup, StringComparison.Ordinal));

        navigation.NavigateTo("/appointments?dateRange=week");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("appointments-week-clinician-selector", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("No clinicians available", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("#appointments-week-clinician-select").HasAttribute("disabled"));
            Assert.Contains("week-grouping-control", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Week Schedule", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("No appointments scheduled for this week", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Today's Appointments", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentsPage_NeedsNoteQuery_FiltersToActionableAppointments()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var today = DateTime.Today;
        var dueStart = DateTime.SpecifyKind(today.AddHours(9), DateTimeKind.Local);
        var scheduledStart = DateTime.SpecifyKind(today.AddHours(10), DateTimeKind.Local);
        var startedStart = DateTime.SpecifyKind(today.AddHours(11), DateTimeKind.Local);
        var completedStart = DateTime.SpecifyKind(today.AddHours(12), DateTimeKind.Local);

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Needs Note",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = dueStart.ToUniversalTime(),
                    EndTimeUtc = dueStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Checked In",
                    VisitWorkflowStatus = "Checked In",
                    IntakeStatus = "Complete"
                },
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Scheduled Only",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = scheduledStart.ToUniversalTime(),
                    EndTimeUtc = scheduledStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Scheduled",
                    VisitWorkflowStatus = "Scheduled",
                    IntakeStatus = "Complete"
                },
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Started Note",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = startedStart.ToUniversalTime(),
                    EndTimeUtc = startedStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Checked In",
                    VisitWorkflowStatus = "Note Started",
                    VisitNoteId = Guid.NewGuid(),
                    IntakeStatus = "Complete"
                },
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Completed Without Note",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = completedStart.ToUniversalTime(),
                    EndTimeUtc = completedStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Completed",
                    VisitWorkflowStatus = string.Empty,
                    IntakeStatus = "Complete"
                }
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?needsNote=true&dateRange=today");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs Note", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Completed Without Note", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Scheduled Only", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Started Note", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentsPage_ClearNeedsNoteFilter_NormalizesRoute()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var scheduledStart = DateTime.SpecifyKind(DateTime.Today.AddHours(10), DateTimeKind.Local);

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Scheduled Only",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = scheduledStart.ToUniversalTime(),
                    EndTimeUtc = scheduledStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Scheduled",
                    VisitWorkflowStatus = "Scheduled",
                    IntakeStatus = "Complete"
                }
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?needsNote=true&dateRange=today");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No appointments need notes for this period", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Clear filters", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Scheduled Only", cut.Markup, StringComparison.Ordinal);
        });
        Assert.EndsWith(
            "/appointments?dateRange=today",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsPage_WeekView_DefaultsToClinicianGroupingAndCanSwitchToDay()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);
        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Week Grouping Patient",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = localStart.ToUniversalTime(),
                    EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Scheduled",
                    VisitWorkflowStatus = "Scheduled",
                    IntakeStatus = "Complete"
                }
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?dateRange=week");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("week-grouping-clinician", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Week grouping", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll(".week-grouping-control__button")
            .First(button => button.TextContent.Contains("Day", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("week-grouping-day", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Contains(
            "groupBy=day",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsPage_WeekViewClickFromToday_UsesRouteBackedFallback()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);
        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                new AppointmentListItemResponse
                {
                    Id = Guid.NewGuid(),
                    PatientRecordId = Guid.NewGuid(),
                    PatientName = "Week Click Patient",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = localStart.ToUniversalTime(),
                    EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow-up",
                    AppointmentStatus = "Scheduled",
                    VisitWorkflowStatus = "Scheduled",
                    IntakeStatus = "Complete"
                }
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".tab-button");

        var weekTab = cut.FindAll(".tab-button")
            .First(tab => tab.TextContent.Contains("Week View", StringComparison.Ordinal));

        Assert.Equal("/appointments?dateRange=week", weekTab.GetAttribute("href"));

        Services.GetRequiredService<NavigationManager>().NavigateTo(weekTab.GetAttribute("href")!);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("week-grouping-control", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("week-grouping-clinician", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Week Schedule", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Today's Schedule", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentsPage_AdminWeekView_ShowsClinicianSelectorAndFiltersSelection()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var taylorId = Guid.NewGuid();
        var jordanId = Guid.NewGuid();
        var weekStart = GetSundayStartOfWeek(DateTime.Today);
        var taylorStart = DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(9), DateTimeKind.Local);
        var jordanStart = DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(10), DateTimeKind.Local);

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                CreateAppointment("Taylor Week Patient", taylorId, "Taylor PT", taylorStart),
                CreateAppointment("Jordan Week Patient", jordanId, "Jordan PTA", jordanStart)
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse { Id = taylorId, DisplayName = "Taylor PT" },
                new AppointmentClinicianResponse { Id = jordanId, DisplayName = "Jordan PTA" }
            ]
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/appointments?dateRange=week&clinicianId={jordanId:D}");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("appointments-week-clinician-selector", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Jordan Week Patient", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Taylor Week Patient", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("#appointments-week-clinician-select").Change(taylorId.ToString("D"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Taylor Week Patient", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Jordan Week Patient", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Contains(
            $"clinicianId={taylorId:D}",
            Services.GetRequiredService<NavigationManager>().Uri,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppointmentsPage_PtWeekView_HidesSelectorAndScopesToCurrentClinician()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var taylorId = Guid.NewGuid();
        var jordanId = Guid.NewGuid();
        var weekStart = GetSundayStartOfWeek(DateTime.Today);
        var taylorStart = DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(9), DateTimeKind.Local);
        var jordanStart = DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(10), DateTimeKind.Local);

        RegisterServices(
            new AppointmentsOverviewResponse
            {
                Appointments =
                [
                    CreateAppointment("Taylor Week Patient", taylorId, "Taylor PT", taylorStart),
                    CreateAppointment("Jordan Week Patient", jordanId, "Jordan PTA", jordanStart)
                ],
                Clinicians =
                [
                    new AppointmentClinicianResponse { Id = taylorId, DisplayName = "Taylor PT" },
                    new AppointmentClinicianResponse { Id = jordanId, DisplayName = "Jordan PTA" }
                ]
            },
            role: Roles.PT,
            username: "Taylor PT",
            userId: taylorId);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?dateRange=week");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("appointments-week-clinician-selector", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Taylor Week Patient", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Jordan Week Patient", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AppointmentsPage_PtWeekView_WithoutInternalUserId_FailsClosed()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var weekStart = GetSundayStartOfWeek(DateTime.Today);
        var appointmentStart = DateTime.SpecifyKind(weekStart.AddDays(1).AddHours(9), DateTimeKind.Local);

        RegisterServices(
            new AppointmentsOverviewResponse
            {
                Appointments = [CreateAppointment("Restricted Week Patient", clinicianId, "Taylor PT", appointmentStart)],
                Clinicians = [new AppointmentClinicianResponse { Id = clinicianId, DisplayName = "Taylor PT" }]
            },
            role: Roles.PT,
            username: "Taylor PT");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/appointments?dateRange=week");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Restricted Week Patient", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("No appointments scheduled for this week", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("Follow Up", "Daily%20Treatment%20Note", true)]
    [InlineData("Initial Evaluation", "Evaluation%20Note", false)]
    [InlineData("Discharge", "Discharge%20Note", false)]
    public void AppointmentsPage_StartVisit_NavigatesToWorkspaceWithAppointmentContext(string appointmentType, string expectedEncodedNoteType, bool expectEvaluationFallback)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var appointmentDate = DateTime.Today;
        var localStart = DateTime.SpecifyKind(appointmentDate.AddHours(10), DateTimeKind.Local);
        var appointment = new AppointmentListItemResponse
        {
            Id = appointmentId,
            PatientRecordId = patientId,
            PatientName = "Alex Patient",
            ClinicianId = clinicianId,
            ClinicianName = "Taylor PT",
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
            AppointmentType = appointmentType,
            AppointmentStatus = "Checked In",
            IntakeStatus = "Completed"
        };

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments = [appointment],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Appointment Details", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Start Visit", StringComparison.Ordinal))
            .Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        var fallbackQuery = expectEvaluationFallback ? "&allowEvaluationFallback=true" : string.Empty;
        Assert.EndsWith(
            $"/patient/{patientId:D}/new-note?noteType={expectedEncodedNoteType}&appointmentId={appointmentId:D}&dateOfService={appointmentDate:yyyy-MM-dd}{fallbackQuery}",
            navigation.Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsPage_NoteStartedAppointment_EntersVisitFromDetails()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var appointmentDate = DateTime.Today;
        var localStart = DateTime.SpecifyKind(appointmentDate.AddHours(11), DateTimeKind.Local);

        RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments =
            [
                new AppointmentListItemResponse
                {
                    Id = appointmentId,
                    PatientRecordId = patientId,
                    PatientName = "Jordan Patient",
                    ClinicianId = clinicianId,
                    ClinicianName = "Taylor PT",
                    StartTimeUtc = localStart.ToUniversalTime(),
                    EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                    AppointmentType = "Follow Up",
                    AppointmentStatus = "Checked In",
                    VisitWorkflowStatus = "Note Started",
                    VisitNoteId = noteId,
                    IntakeStatus = "Completed"
                }
            ],
            Clinicians =
            [
                new AppointmentClinicianResponse
                {
                    Id = clinicianId,
                    DisplayName = "Taylor PT"
                }
            ]
        });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Note Started", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Enter Visit", StringComparison.Ordinal))
            .Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith(
            $"/patient/{patientId:D}/note/{noteId:D}",
            navigation.Uri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsPage_EditAppointment_OpensPrefilledTypeForm()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);

        RegisterServices(
            new AppointmentsOverviewResponse
            {
                Appointments =
                [
                    new AppointmentListItemResponse
                    {
                        Id = Guid.NewGuid(),
                        PatientRecordId = patientId,
                        PatientName = "Alex Patient",
                        ClinicianId = clinicianId,
                        ClinicianName = "Taylor PT",
                        StartTimeUtc = localStart.ToUniversalTime(),
                        EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                        AppointmentType = "Follow-up",
                        AppointmentStatus = "Scheduled",
                        VisitWorkflowStatus = "Scheduled",
                        IntakeStatus = "Completed",
                        Notes = "Change visit type if needed."
                    }
                ],
                Clinicians =
                [
                    new AppointmentClinicianResponse
                    {
                        Id = clinicianId,
                        DisplayName = "Taylor PT"
                    }
                ]
            },
            patients:
            [
                new PatientListItemResponse
                {
                    Id = patientId,
                    DisplayName = "Alex Patient"
                }
            ]);

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Appointment Type", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Edit Appointment", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => Assert.Contains("Edit Appointment", cut.Markup, StringComparison.Ordinal));
        var appointmentTypeSelect = Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointmentType"));
        Assert.Equal("Follow Up", appointmentTypeSelect.Value);
    }

    [Fact]
    public void AppointmentsPage_AppointmentTypeEditor_PersistsOnlyTypeAndRefreshesSchedule()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var appointmentId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);
        var lastModifiedUtc = new DateTime(2026, 8, 9, 13, 0, 0, DateTimeKind.Utc);
        var appointment = new AppointmentListItemResponse
        {
            Id = appointmentId,
            PatientRecordId = patientId,
            PatientName = "Alex Patient",
            ClinicianId = clinicianId,
            ClinicianName = "Taylor PT",
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
            AppointmentType = "Follow-up",
            AppointmentStatus = "Scheduled",
            VisitWorkflowStatus = "Scheduled",
            IntakeStatus = "Completed",
            ProgressNoteDueDate = DateTime.Today.AddDays(12),
            VisitNumber = 4,
            Notes = "Preserve this scheduling note.",
            LastModifiedUtc = lastModifiedUtc
        };
        var appointmentService = RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments = [appointment],
            Clinicians = [new AppointmentClinicianResponse { Id = clinicianId, DisplayName = "Taylor PT" }]
        });
        UpdateAppointmentTypeRequest? capturedRequest = null;
        appointmentService
            .Setup(service => service.UpdateAppointmentTypeAsync(appointmentId, It.IsAny<UpdateAppointmentTypeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdateAppointmentTypeRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(new AppointmentListItemResponse
            {
                Id = appointmentId,
                PatientRecordId = patientId,
                PatientName = "Alex Patient",
                ClinicianId = clinicianId,
                ClinicianName = "Taylor PT",
                StartTimeUtc = appointment.StartTimeUtc,
                EndTimeUtc = appointment.EndTimeUtc,
                AppointmentType = "Re-evaluation",
                AppointmentStatus = "Scheduled",
                VisitWorkflowStatus = "Scheduled",
                IntakeStatus = "Completed",
                ProgressNoteDueDate = appointment.ProgressNoteDueDate,
                VisitNumber = appointment.VisitNumber,
                Notes = appointment.Notes,
                LastModifiedUtc = lastModifiedUtc.AddMinutes(1)
            });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block").Click();
        cut.Find("#appointment-detail-type").Change("Re-evaluation");
        cut.Find(".appointment-detail-modal__save-type").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(capturedRequest);
            Assert.Equal("Re-evaluation", capturedRequest!.AppointmentType);
            Assert.Equal(lastModifiedUtc, capturedRequest.ExpectedLastModifiedUtc);
            Assert.Contains("Appointment type updated.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Re-evaluation", cut.Find(".appointment-block").TextContent, StringComparison.Ordinal);
            Assert.Equal(
                "Re-evaluation",
                Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointment-detail-type")).Value);
        });

        appointmentService.Verify(
            service => service.UpdateAppointmentTypeAsync(appointmentId, It.IsAny<UpdateAppointmentTypeRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        appointmentService.Verify(
            service => service.UpdateAsync(appointmentId, It.IsAny<UpdateAppointmentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        cut.Find(".appointment-detail-modal__close").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".appointment-detail-modal__container")));
        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Equal(
            "Re-evaluation",
            Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointment-detail-type")).Value));
    }

    [Fact]
    public void AppointmentsPage_FailedAppointmentTypeUpdate_RestoresPersistedScheduleValue()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);
        var appointment = CreateAppointment("Alex Patient", clinicianId, "Taylor PT", localStart);
        var appointmentService = RegisterServices(new AppointmentsOverviewResponse
        {
            Appointments = [appointment],
            Clinicians = [new AppointmentClinicianResponse { Id = clinicianId, DisplayName = "Taylor PT" }]
        });
        appointmentService
            .Setup(service => service.UpdateAppointmentTypeAsync(appointment.Id, It.IsAny<UpdateAppointmentTypeRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Persistence failure"));

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block").Click();
        cut.Find("#appointment-detail-type").Change("Discharge");
        cut.Find(".appointment-detail-modal__save-type").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The previous type is still in effect.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Follow-up", cut.Find(".appointment-block").TextContent, StringComparison.Ordinal);
            Assert.Equal(
                "Follow-up",
                Assert.IsAssignableFrom<IHtmlSelectElement>(cut.Find("#appointment-detail-type")).Value);
        });
    }

    [Fact]
    public void AppointmentsPage_AppointmentDetails_DisablesCopayActionUntilWorkflowExists()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);

        RegisterServices(
            new AppointmentsOverviewResponse
            {
                Appointments =
                [
                    new AppointmentListItemResponse
                    {
                        Id = Guid.NewGuid(),
                        PatientRecordId = patientId,
                        PatientName = "Alex Patient",
                        ClinicianId = clinicianId,
                        ClinicianName = "Taylor PT",
                        StartTimeUtc = localStart.ToUniversalTime(),
                        EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                        AppointmentType = "Follow Up",
                        AppointmentStatus = "Scheduled",
                        VisitWorkflowStatus = "Scheduled",
                        IntakeStatus = "Completed"
                    }
                ],
                Clinicians =
                [
                    new AppointmentClinicianResponse
                    {
                        Id = clinicianId,
                        DisplayName = "Taylor PT"
                    }
                ]
            });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Appointment Details", cut.Markup, StringComparison.Ordinal));

        var copayButton = Assert.Single(
            cut.FindAll("button"),
            button => button.TextContent.Contains("Record Copay", StringComparison.Ordinal));

        Assert.True(copayButton.HasAttribute("disabled"));
        Assert.Contains("Copay collection is not configured for this appointment.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AppointmentsPage_CheckInWithCopayDue_OpensPaymentModalBeforeCheckIn()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var patientId = Guid.NewGuid();
        var clinicianId = Guid.NewGuid();
        var localStart = DateTime.SpecifyKind(DateTime.Today.AddHours(9), DateTimeKind.Local);

        RegisterServices(
            new AppointmentsOverviewResponse
            {
                Appointments =
                [
                    new AppointmentListItemResponse
                    {
                        Id = Guid.NewGuid(),
                        PatientRecordId = patientId,
                        PatientName = "Alex Patient",
                        ClinicianId = clinicianId,
                        ClinicianName = "Taylor PT",
                        StartTimeUtc = localStart.ToUniversalTime(),
                        EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
                        AppointmentType = "Follow Up",
                        AppointmentStatus = "Scheduled",
                        VisitWorkflowStatus = "Scheduled",
                        IntakeStatus = "Complete",
                        CopayAmount = 30m,
                        CopayStatusLabel = "Copay due",
                        CanRecordCopay = true
                    }
                ],
                Clinicians =
                [
                    new AppointmentClinicianResponse
                    {
                        Id = clinicianId,
                        DisplayName = "Taylor PT"
                    }
                ]
            });

        var cut = RenderComponent<global::PTDoc.UI.Pages.Appointments>();
        cut.WaitForElement(".appointment-block");

        cut.Find(".appointment-block").Click();
        cut.WaitForAssertion(() => Assert.Contains("Appointment Details", cut.Markup, StringComparison.Ordinal));

        cut.FindAll(".appointment-detail-modal__container button")
            .First(button => button.TextContent.Contains("Check In", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Collect copay before check-in", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("$30.00", cut.Markup, StringComparison.Ordinal);
        });
    }

    private Mock<IAppointmentService> RegisterServices(
        AppointmentsOverviewResponse? overview = null,
        IReadOnlyList<PatientListItemResponse>? patients = null,
        string role = Roles.Admin,
        string username = "Admin User",
        Guid? userId = null,
        IPatientService? configuredPatientService = null,
        IIntakeService? intakeService = null,
        IIntakeDeliveryService? intakeDeliveryService = null,
        IToastService? configuredToastService = null)
    {
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized(username);
        authorization.SetRoles(role);
        var grantedPolicies = new List<string> { AuthorizationPolicies.SchedulingAccess };
        if (role is Roles.PT or Roles.PTA or Roles.Admin)
        {
            grantedPolicies.Add(AuthorizationPolicies.PatientWrite);
            grantedPolicies.Add(AuthorizationPolicies.IntakeWrite);
        }
        else if (role == Roles.FrontDesk)
        {
            grantedPolicies.Add(AuthorizationPolicies.IntakeWrite);
        }

        authorization.SetPolicies(grantedPolicies.ToArray());
        if (userId.HasValue)
        {
            authorization.SetClaims(new Claim(PTDocClaimTypes.InternalUserId, userId.Value.ToString("D")));
        }

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

        var defaultPatientService = new Mock<IPatientService>(MockBehavior.Strict);
        defaultPatientService
            .Setup(service => service.SearchAsync(null, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patients ?? Array.Empty<PatientListItemResponse>());

        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);
        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(overview ?? new AppointmentsOverviewResponse());

        var schedulingAdministrationService = new Mock<ISchedulingAdministrationService>(MockBehavior.Strict);
        schedulingAdministrationService
            .Setup(service => service.GetVisitTypesAsync(
                Guid.Empty,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VisitTypeDto>());

        var toastService = new Mock<IToastService>(MockBehavior.Loose);
        var defaultIntakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        var defaultIntakeDeliveryService = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        var paymentClientService = new Mock<IPaymentClientService>(MockBehavior.Strict);
        paymentClientService
            .Setup(service => service.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentClientConfigurationResponse
            {
                Enabled = true,
                Environment = "Sandbox",
                ApiLoginId = "login",
                ClientKey = "client-key"
            });

        Services.AddSingleton(headerConfigurationService.Object);
        Services.AddSingleton(configuredPatientService ?? defaultPatientService.Object);
        Services.AddSingleton(appointmentService.Object);
        Services.AddSingleton(intakeService ?? defaultIntakeService.Object);
        Services.AddSingleton(intakeDeliveryService ?? defaultIntakeDeliveryService.Object);
        Services.AddSingleton(schedulingAdministrationService.Object);
        Services.AddSingleton(paymentClientService.Object);
        Services.AddSingleton(configuredToastService ?? toastService.Object);
        return appointmentService;
    }

    private static AppointmentListItemResponse CreateAppointment(
        string patientName,
        Guid clinicianId,
        string clinicianName,
        DateTime localStart)
    {
        return new AppointmentListItemResponse
        {
            Id = Guid.NewGuid(),
            PatientRecordId = Guid.NewGuid(),
            PatientName = patientName,
            ClinicianId = clinicianId,
            ClinicianName = clinicianName,
            StartTimeUtc = localStart.ToUniversalTime(),
            EndTimeUtc = localStart.AddMinutes(45).ToUniversalTime(),
            AppointmentType = "Follow-up",
            AppointmentStatus = "Scheduled",
            VisitWorkflowStatus = "Scheduled",
            IntakeStatus = "Complete"
        };
    }

    private static void FillAddPatientForm(
        IRenderedComponent<global::PTDoc.UI.Pages.Appointments> cut,
        string firstName,
        string lastName,
        string email,
        string dateOfBirth)
    {
        cut.Find("#firstName").Change(firstName);
        cut.Find("#lastName").Change(lastName);
        cut.Find("#email").Change(email);
        cut.Find("#dob").Change(dateOfBirth);
    }

    private static DateTime GetSundayStartOfWeek(DateTime date)
    {
        var daysSinceSunday = (int)date.DayOfWeek;
        return date.Date.AddDays(-daysSinceSunday);
    }
}
