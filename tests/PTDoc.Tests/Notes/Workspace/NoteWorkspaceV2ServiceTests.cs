using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "Unit")]
public sealed class NoteWorkspaceV2ServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly NoteWorkspaceV2Service _service;

    public NoteWorkspaceV2ServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"NoteWorkspaceV2_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);

        var registry = new OutcomeMeasureRegistry();
        var catalogs = new WorkspaceReferenceCatalogService(registry);
        _service = new NoteWorkspaceV2Service(
            _context,
            new TestIdentityContextAccessor(),
            new TestTenantContextAccessor(),
            new PlanOfCareCalculator(),
            new AssessmentCompositionService(),
            new GoalManagementService(catalogs),
            registry);
    }

    [Fact]
    public async Task SaveAsync_PersistsWorkspaceSnapshotAndLegacyBillingFields()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        _context.Patients.Add(patient);

        var priorNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = new DateTime(2026, 3, 1),
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow
        };
        priorNote.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            NoteId = priorNote.Id,
            BodyPart = BodyPart.Cervical,
            MetricType = MetricType.ROM,
            Value = "40 degrees"
        });
        _context.ClinicalNotes.Add(priorNote);

        await _context.SaveChangesAsync();

        var saveRequest = new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            DateOfService = new DateTime(2026, 3, 30),
            NoteType = NoteType.Evaluation,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation,
                Subjective = new WorkspaceSubjectiveV2
                {
                    FunctionalLimitations =
                    [
                        new FunctionalLimitationEntryV2
                        {
                            BodyPart = BodyPart.Cervical,
                            Category = "Mobility",
                            Description = "Unable to turn head fully to look over shoulder while driving"
                        }
                    ],
                    NarrativeContext = new SubjectNarrativeContextV2
                    {
                        ChiefComplaint = "Neck pain with driving"
                    }
                },
                Objective = new WorkspaceObjectiveV2
                {
                    PrimaryBodyPart = BodyPart.Cervical,
                    Metrics =
                    [
                        new ObjectiveMetricInputV2
                        {
                            BodyPart = BodyPart.Cervical,
                            MetricType = MetricType.ROM,
                            Value = "50 degrees"
                        }
                    ],
                    OutcomeMeasures =
                    [
                        new OutcomeMeasureEntryV2
                        {
                            MeasureType = OutcomeMeasureType.NeckDisabilityIndex,
                            Score = 22,
                            RecordedAtUtc = new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc)
                        }
                    ]
                },
                Assessment = new WorkspaceAssessmentV2
                {
                    Goals =
                    [
                        new WorkspaceGoalEntryV2
                        {
                            Description = "Patient will rotate head >=60 degrees to check over shoulder while seated within 3 weeks.",
                            Category = "Mobility",
                            Status = GoalStatus.Active
                        }
                    ]
                },
                Plan = new WorkspacePlanV2
                {
                    TreatmentFrequencyDaysPerWeek = [2],
                    TreatmentDurationWeeks = [6],
                    TreatmentFocuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Cervical joint mobility",
                        "Deep neck flexor activation"
                    },
                    SelectedCptCodes =
                    [
                        new PlannedCptCodeV2
                        {
                            Code = "97110",
                            Description = "Therapeutic exercises",
                            Units = 2
                        }
                    ]
                }
            }
        };

        var saved = await _service.SaveAsync(saveRequest);
        var reloaded = await _service.LoadAsync(patient.Id, saved.NoteId);

        Assert.NotNull(reloaded);
        Assert.NotEqual(Guid.Empty, saved.NoteId);
        Assert.Single(_context.OutcomeMeasureResults.Where(result => result.NoteId == saved.NoteId));
        Assert.Single(_context.PatientGoals.Where(goal => goal.PatientId == patient.Id));

        var persistedNote = await _context.ClinicalNotes.FirstAsync(note => note.Id == saved.NoteId);
        Assert.Contains("97110", persistedNote.CptCodesJson);
        Assert.Contains("\"schemaVersion\":2", persistedNote.ContentJson);
        Assert.False(string.IsNullOrWhiteSpace(saved.Payload.Plan.PlanOfCareNarrative));

        var currentMetric = Assert.Single(reloaded!.Payload.Objective.Metrics);
        Assert.Equal("40 degrees", currentMetric.PreviousValue);
        Assert.Equal(2, reloaded.Payload.Plan.TreatmentFrequencyDaysPerWeek.Single());
        Assert.Equal(6, reloaded.Payload.Plan.TreatmentDurationWeeks.Single());

        var outcomeMeasure = Assert.Single(reloaded.Payload.Objective.OutcomeMeasures);
        Assert.Equal(OutcomeMeasureType.NeckDisabilityIndex, outcomeMeasure.MeasureType);
        Assert.Equal(5d, outcomeMeasure.MinimumDetectableChange);
    }

    [Fact]
    public async Task SaveAsync_RejectsPendingNote()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Pending",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        _context.Patients.Add(patient);

        var pendingNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.PendingCoSign,
            DateOfService = new DateTime(2026, 4, 1),
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(pendingNote);

        await _context.SaveChangesAsync();

        var request = new NoteWorkspaceV2SaveRequest
        {
            NoteId = pendingNote.Id,
            PatientId = patient.Id,
            DateOfService = pendingNote.DateOfService,
            NoteType = NoteType.ProgressNote,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote
            }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(request));

        Assert.Equal("Only draft notes can be modified through the workspace API.", exception.Message);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private sealed class TestIdentityContextAccessor : IIdentityContextAccessor
    {
        private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000111");

        public Guid GetCurrentUserId() => UserId;
        public Guid? TryGetCurrentUserId() => UserId;
        public string GetCurrentUsername() => "tester";
        public string? GetCurrentUserRole() => Roles.PT;
    }

    private sealed class TestTenantContextAccessor : ITenantContextAccessor
    {
        public Guid? GetCurrentClinicId() => null;
    }
}
