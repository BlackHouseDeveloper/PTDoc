using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
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
        var auditService = new AuditService(_context);
        var rulesEngine = new RulesEngine(_context, auditService);
        var validationService = new NoteSaveValidationService(_context, rulesEngine);
        _service = new NoteWorkspaceV2Service(
            _context,
            new TestIdentityContextAccessor(),
            new TestTenantContextAccessor(),
            validationService,
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
        Assert.True(saved.IsValid);
        Assert.NotNull(saved.Workspace);

        var workspace = saved.Workspace!;
        var reloaded = await _service.LoadAsync(patient.Id, workspace.NoteId);

        Assert.NotNull(reloaded);
        Assert.NotEqual(Guid.Empty, workspace.NoteId);
        Assert.Single(_context.OutcomeMeasureResults.Where(result => result.NoteId == workspace.NoteId));
        Assert.Single(_context.PatientGoals.Where(goal => goal.PatientId == patient.Id));

        var persistedNote = await _context.ClinicalNotes.FirstAsync(note => note.Id == workspace.NoteId);
        Assert.Contains("97110", persistedNote.CptCodesJson);
        Assert.Contains("\"schemaVersion\":2", persistedNote.ContentJson);
        Assert.False(string.IsNullOrWhiteSpace(workspace.Payload.Plan.PlanOfCareNarrative));

        var currentMetric = Assert.Single(reloaded!.Payload.Objective.Metrics);
        Assert.Equal("40 degrees", currentMetric.PreviousValue);
        Assert.Equal(2, reloaded.Payload.Plan.TreatmentFrequencyDaysPerWeek.Single());
        Assert.Equal(6, reloaded.Payload.Plan.TreatmentDurationWeeks.Single());

        var outcomeMeasure = Assert.Single(reloaded.Payload.Objective.OutcomeMeasures);
        Assert.Equal(OutcomeMeasureType.NeckDisabilityIndex, outcomeMeasure.MeasureType);
        Assert.Equal(5d, outcomeMeasure.MinimumDetectableChange);
    }

    [Fact]
    public async Task SaveAsync_SignedNote_ThrowsImmutableError()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Locked",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            DateOfService = new DateTime(2026, 3, 30),
            ContentJson = "{}",
            LastModifiedUtc = DateTime.UtcNow,
            SignatureHash = "SIGNED_HASH",
            SignedUtc = DateTime.UtcNow,
            NoteStatus = NoteStatus.Signed
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var request = new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            NoteId = note.Id,
            DateOfService = note.DateOfService,
            NoteType = note.NoteType,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = note.NoteType
            }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(request));

        Assert.Equal("Signed notes cannot be modified. Create addendum.", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_MissingTimedMinutes_ReturnsWarningAndPersistsWorkspace()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid(),
            PayerInfoJson = """{"PayerType":"Medicare"}"""
        };
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var result = await _service.SaveAsync(new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            DateOfService = new DateTime(2026, 3, 30),
            NoteType = NoteType.ProgressNote,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote,
                Plan = new WorkspacePlanV2
                {
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
        });

        Assert.True(result.IsValid);
        Assert.NotNull(result.Workspace);
        Assert.Contains(result.Warnings, warning => warning.Contains("Timed CPT minutes are missing", StringComparison.OrdinalIgnoreCase));
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
