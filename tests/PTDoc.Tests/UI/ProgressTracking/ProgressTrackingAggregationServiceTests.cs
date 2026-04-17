using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.ProgressTracking.Models;
using PTDoc.UI.Services;
using System.Globalization;
using System.Text.Json;

namespace PTDoc.Tests.UI.ProgressTracking;

[Trait("Category", "CoreCi")]
public sealed class ProgressTrackingAggregationServiceTests
{
    [Fact]
    public async Task LoadAsync_UsesNewestAppointmentForRecency_AndBatchLoadsLatestNotes()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var olderNoteUtc = new DateTime(2026, 4, 1, 14, 0, 0, DateTimeKind.Utc);
        var newerAppointmentUtc = olderNoteUtc.AddDays(3);

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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Alex Patient",
                    NoteType = NoteType.ProgressNote.ToString(),
                    NoteStatus = NoteStatus.Signed,
                    IsSigned = true,
                    DateOfService = olderNoteUtc,
                    LastModifiedUtc = olderNoteUtc,
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
                        ContentJson = """{"assessment":{"goals":[]},"objective":{"outcomeMeasures":[]}}""",
                        DateOfService = olderNoteUtc,
                        CreatedUtc = olderNoteUtc,
                        LastModifiedUtc = olderNoteUtc,
                        CptCodesJson = "[]"
                    }
                }
            });

        appointmentService
            .Setup(service => service.GetOverviewAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppointmentsOverviewResponse
            {
                Appointments = new[]
                {
                    new AppointmentListItemResponse
                    {
                        Id = Guid.NewGuid(),
                        PatientRecordId = patientId,
                        PatientName = "Alex Patient",
                        ClinicianName = "Dr. Rivera",
                        StartTimeUtc = newerAppointmentUtc,
                        EndTimeUtc = newerAppointmentUtc.AddHours(1),
                        AppointmentType = "Reassessment",
                        AppointmentStatus = "Scheduled",
                        IntakeStatus = "Complete"
                    }
                }
            });

        var service = new ProgressTrackingAggregationService(noteService.Object, appointmentService.Object);

        var snapshot = await service.LoadAsync(new ProgressTrackingFilterState());

        var patient = Assert.Single(snapshot.Patients);
        Assert.Equal(newerAppointmentUtc, patient.LastAssessmentDate);
        Assert.Equal("Dr. Rivera", patient.Provider);
        noteService.Verify(service => service.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        noteService.Verify(service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadTrendPointsAsync_UsesBatchRead()
    {
        var firstNoteId = Guid.NewGuid();
        var secondNoteId = Guid.NewGuid();
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var appointmentService = new Mock<IAppointmentService>(MockBehavior.Strict);

        noteService
            .Setup(service => service.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids.Contains(firstNoteId) && ids.Contains(secondNoteId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateNoteDetail(firstNoteId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), "55"),
                CreateNoteDetail(secondNoteId, new DateTime(2026, 4, 8, 0, 0, 0, DateTimeKind.Utc), "72")
            });

        var service = new ProgressTrackingAggregationService(noteService.Object, appointmentService.Object);

        var points = await service.LoadTrendPointsAsync([firstNoteId, secondNoteId]);

        Assert.Collection(points,
            point =>
            {
                Assert.Equal("Apr 1", point.Label);
                Assert.Equal(55, point.Value);
            },
            point =>
            {
                Assert.Equal("Apr 8", point.Label);
                Assert.Equal(72, point.Value);
            });

        noteService.Verify(service => service.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        noteService.Verify(service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadAsync_ParsesWorkspaceV2ScoresAndGoals()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var dateOfService = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);

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
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new NoteListItemApiResponse
                {
                    Id = noteId,
                    PatientId = patientId,
                    PatientName = "Taylor Patient",
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
                            },
                            Assessment = new WorkspaceAssessmentV2
                            {
                                Goals =
                                [
                                    new WorkspaceGoalEntryV2
                                    {
                                        Description = "Walk 20 minutes without rest",
                                        Status = GoalStatus.Active
                                    },
                                    new WorkspaceGoalEntryV2
                                    {
                                        Description = "Return to gardening",
                                        Status = GoalStatus.Met
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

        var service = new ProgressTrackingAggregationService(noteService.Object, appointmentService.Object);

        var snapshot = await service.LoadAsync(new ProgressTrackingFilterState());

        var patient = Assert.Single(snapshot.Patients);
        Assert.Equal(64, patient.CurrentScore);
        Assert.True(patient.HasOutcomeScore);
        Assert.Equal(1, patient.MetGoalCount);
        Assert.Equal(1, patient.ActiveGoalCount);
        Assert.Contains("Walk 20 minutes without rest", patient.Goals);
        Assert.Contains("Return to gardening", patient.Goals);
        Assert.Equal("Progress Note · 2 tracked goals", patient.Condition);
    }

    private static NoteDetailResponse CreateNoteDetail(Guid noteId, DateTime dateOfService, string outcomeScore)
    {
        return new NoteDetailResponse
        {
            Note = new NoteResponse
            {
                Id = noteId,
                PatientId = Guid.NewGuid(),
                NoteType = NoteType.ProgressNote,
                NoteStatus = NoteStatus.Signed,
                ContentJson = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{\"objective\":{{\"outcomeMeasures\":[{{\"name\":\"ODI\",\"score\":\"{outcomeScore}\",\"date\":\"{dateOfService:O}\"}}]}}}}"),
                DateOfService = dateOfService,
                CreatedUtc = dateOfService,
                LastModifiedUtc = dateOfService,
                CptCodesJson = "[]"
            }
        };
    }
}
