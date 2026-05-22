using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.UI.Components.ProgressTracking;
using PTDoc.UI.Components.ProgressTracking.Models;
using System.Text.Json;

namespace PTDoc.Tests.UI.ProgressTracking;

[Trait("Category", "CoreCi")]
public sealed class ProgressTrackingComponentsTests : TestContext
{
    [Fact]
    public void TrendsPanel_UsesProvidedMeasureLabel_AndAccessibleData()
    {
        var cut = RenderComponent<ProgressTrackingTrendsPanel>(parameters => parameters
            .Add(component => component.MeasureLabel, "ODI")
            .Add(component => component.OutcomeTrend, new[]
            {
                new ProgressTrendPointVm
                {
                    Label = "May 1",
                    Value = 64,
                    MeasureType = OutcomeMeasureType.OswestryDisabilityIndex,
                    MeasureLabel = "ODI",
                    ScoreDisplay = "64 %",
                    Interpretation = "Severe disability"
                }
            })
            .Add(component => component.ProviderProgress, Array.Empty<ProviderGoalProgressVm>()));

        Assert.Contains("ODI", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("LEFS", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("May 1", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("64 %", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Severe disability", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PatientsPanel_MarksSelectedPatient()
    {
        var patientId = Guid.NewGuid().ToString();
        var cut = RenderComponent<ProgressTrackingPatientsPanel>(parameters => parameters
            .Add(component => component.SelectedPatientId, patientId)
            .Add(component => component.Items, new[]
            {
                new ProgressTrackingPatientVm
                {
                    Id = patientId,
                    DisplayName = "Alex Patient",
                    StatusLabel = "Active"
                }
            }));

        var button = cut.Find("button");
        Assert.Equal("true", button.GetAttribute("aria-current"));
    }

    [Fact]
    public void Tabs_ExposeRovingTabIndex()
    {
        var cut = RenderComponent<ProgressTrackingTabs>(parameters => parameters
            .Add(component => component.ActiveTab, ProgressTrackingTab.Goals));

        var tabs = cut.FindAll("[role='tab']");
        Assert.Equal("-1", tabs[0].GetAttribute("tabindex"));
        Assert.Equal("0", tabs[2].GetAttribute("tabindex"));
        Assert.Equal("true", tabs[2].GetAttribute("aria-selected"));
    }

    [Fact]
    public void PageLoadFailure_ShowsGenericErrorWithoutRawExceptionMessage()
    {
        RegisterCommonPageServices();
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);

        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                500,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ThrowsAsync(new InvalidOperationException("sensitive database detail"));

        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentsOverviewResponse());

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(appointmentService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.ProgressTracking>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Progress tracking data could not be retrieved", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("sensitive database detail", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TrendsTab_BindsTrendErrorState_AndShowsTrendData()
    {
        RegisterCommonPageServices();
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var dateOfService = DateTime.UtcNow.Date.AddDays(-4);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);

        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                500,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Trend Patient",
                    NoteType = NoteType.ProgressNote.ToString(),
                    NoteStatus = NoteStatus.Signed,
                    IsSigned = true,
                    DateOfService = dateOfService,
                    LastModifiedUtc = dateOfService,
                    CptCodesJson = "[]"
                }
            });

        noteService
            .Setup(service => service.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == noteId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new NoteDetailResponse
                {
                    Note = new NoteResponse
                    {
                        Id = noteId,
                        PatientId = patientId,
                        NoteType = NoteType.ProgressNote,
                        NoteStatus = NoteStatus.Signed,
                        ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
                        {
                            NoteType = NoteType.ProgressNote,
                            Objective = new WorkspaceObjectiveV2
                            {
                                OutcomeMeasures =
                                [
                                    new OutcomeMeasureEntryV2
                                    {
                                        MeasureType = OutcomeMeasureType.OswestryDisabilityIndex,
                                        Score = 64,
                                        RecordedAtUtc = dateOfService
                                    }
                                ]
                            }
                        }),
                        DateOfService = dateOfService,
                        CreatedUtc = dateOfService,
                        LastModifiedUtc = dateOfService,
                        CptCodesJson = "[]"
                    }
                }
            });

        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentsOverviewResponse());

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(appointmentService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.ProgressTracking>();

        cut.WaitForAssertion(() => Assert.Contains("Loaded 1 patients from backend.", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("[role='tab']")
            .Single(tab => tab.TextContent.Contains("Trends", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("trendErrorMessage", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("ODI", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("64 %", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PendingAlert_NavigatesToNoteReview()
    {
        RegisterCommonPageServices();
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var noteDate = DateTime.UtcNow.Date.AddDays(-8);
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);

        noteService
            .Setup(service => service.GetNotesAsync(
                null,
                null,
                null,
                500,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null,
                null,
                null,
                0))
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Pending Patient",
                    NoteType = NoteType.ProgressNote.ToString(),
                    NoteStatus = NoteStatus.Draft,
                    DateOfService = noteDate,
                    LastModifiedUtc = noteDate,
                    CptCodesJson = "[]"
                }
            });

        noteService
            .Setup(service => service.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == noteId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new NoteDetailResponse
                {
                    Note = new NoteResponse
                    {
                        Id = noteId,
                        PatientId = patientId,
                        NoteType = NoteType.ProgressNote,
                        NoteStatus = NoteStatus.Draft,
                        ContentJson = "{}",
                        DateOfService = noteDate,
                        CreatedUtc = noteDate,
                        LastModifiedUtc = noteDate,
                        CptCodesJson = "[]"
                    }
                }
            });

        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentsOverviewResponse());

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(appointmentService.Object);

        var cut = RenderComponent<global::PTDoc.UI.Pages.ProgressTracking>();

        cut.WaitForAssertion(() => Assert.Contains("Review note", cut.Markup, StringComparison.Ordinal));
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Review note", StringComparison.Ordinal))
            .Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"/patient/{patientId:D}/note/{noteId:D}?section=review", navigation.Uri, StringComparison.Ordinal);
    }

    private void RegisterCommonPageServices()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("test-user");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
    }
}
