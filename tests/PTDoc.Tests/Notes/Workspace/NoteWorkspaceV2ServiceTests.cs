using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Application.Intake;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Content;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;
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
        var intakeReferenceData = new IntakeReferenceDataCatalogService();
        var intakeBodyPartMapper = new IntakeBodyPartMapper(intakeReferenceData);
        var intakeDraftCanonicalizer = new IntakeDraftCanonicalizer(registry, intakeBodyPartMapper);
        var catalogs = new WorkspaceReferenceCatalogService(registry);
        var auditService = new AuditService(_context);
        var rulesEngine = new RulesEngine(_context, auditService);
        var validationService = new NoteSaveValidationService(_context, rulesEngine, catalogs);
        var carryForwardService = new CarryForwardService(_context);
        _service = new NoteWorkspaceV2Service(
            _context,
            new TestIdentityContextAccessor(),
            new TestTenantContextAccessor(),
            validationService,
            new PlanOfCareCalculator(),
            new AssessmentCompositionService(),
            new GoalManagementService(catalogs),
            registry,
            intakeReferenceData,
            intakeBodyPartMapper,
            intakeDraftCanonicalizer,
            carryForwardService);
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
            IsReEvaluation = true,
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
                            Units = 2,
                            Modifiers = ["GP"],
                            ModifierOptions = ["GP", "KX", "CQ"],
                            SuggestedModifiers = ["GP"],
                            ModifierSource = "Commonly used CPT codes and modifiers.md"
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
        Assert.Contains("\"GP\"", persistedNote.CptCodesJson);
        Assert.Contains("\"schemaVersion\":2", persistedNote.ContentJson);
        Assert.True(persistedNote.IsReEvaluation);
        Assert.False(string.IsNullOrWhiteSpace(workspace.Payload.Plan.PlanOfCareNarrative));
        Assert.True(reloaded.IsReEvaluation);

        var currentMetric = Assert.Single(reloaded!.Payload.Objective.Metrics);
        Assert.Equal("40 degrees", currentMetric.PreviousValue);
        Assert.Equal(2, reloaded.Payload.Plan.TreatmentFrequencyDaysPerWeek.Single());
        Assert.Equal(6, reloaded.Payload.Plan.TreatmentDurationWeeks.Single());

        var outcomeMeasure = Assert.Single(reloaded.Payload.Objective.OutcomeMeasures);
        Assert.Equal(OutcomeMeasureType.NeckDisabilityIndex, outcomeMeasure.MeasureType);
        Assert.Equal(5d, outcomeMeasure.MinimumDetectableChange);
        var cptEntry = Assert.Single(reloaded.Payload.Plan.SelectedCptCodes);
        Assert.Equal(["GP"], cptEntry.Modifiers);
        Assert.Equal(["CQ", "GP", "KX"], cptEntry.ModifierOptions.OrderBy(value => value).ToArray());
        Assert.Equal(["GP"], cptEntry.SuggestedModifiers);
        Assert.Equal("Commonly used CPT codes and modifiers.md", cptEntry.ModifierSource);
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

    [Fact]
    public async Task SaveAsync_MoreThanFourDiagnosisCodes_ReturnsValidationError()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Diagnosis",
            LastName = "Limit",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var result = await _service.SaveAsync(new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            DateOfService = new DateTime(2026, 4, 2),
            NoteType = NoteType.Evaluation,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation,
                Assessment = new WorkspaceAssessmentV2
                {
                    DiagnosisCodes =
                    [
                        new DiagnosisCodeV2 { Code = "M25.561", Description = "Pain in right knee" },
                        new DiagnosisCodeV2 { Code = "M25.562", Description = "Pain in left knee" },
                        new DiagnosisCodeV2 { Code = "M54.5", Description = "Low back pain" },
                        new DiagnosisCodeV2 { Code = "M54.2", Description = "Cervicalgia" },
                        new DiagnosisCodeV2 { Code = "R26.2", Description = "Difficulty in walking" }
                    ]
                }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains("Maximum of 4 ICD-10 diagnosis codes allowed.", result.Errors);
        Assert.Null(result.Workspace);
    }

    [Fact]
    public async Task SaveAsync_RejectsHistoricalOnlyVasAsNewOutcomeEntry()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Outcome",
            LastName = "Validation",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var result = await _service.SaveAsync(new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            DateOfService = new DateTime(2026, 4, 3),
            NoteType = NoteType.ProgressNote,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote,
                Objective = new WorkspaceObjectiveV2
                {
                    OutcomeMeasures =
                    [
                        new OutcomeMeasureEntryV2
                        {
                            MeasureType = OutcomeMeasureType.VAS,
                            Score = 5,
                            RecordedAtUtc = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc)
                        }
                    ]
                }
            }
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("VAS", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.Workspace);
        Assert.Empty(_context.OutcomeMeasureResults);
    }

    [Fact]
    public async Task SaveAsync_AllowsUnchangedHistoricalVasRowsOnExistingNote()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Outcome",
            LastName = "History",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            DateOfService = new DateTime(2026, 4, 3),
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote
            }),
            LastModifiedUtc = DateTime.UtcNow
        };
        var recordedAtUtc = new DateTime(2026, 4, 3, 12, 0, 0, DateTimeKind.Utc);
        var persistedRecordedAt = DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Unspecified);

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        _context.OutcomeMeasureResults.Add(new OutcomeMeasureResult
        {
            PatientId = patient.Id,
            NoteId = note.Id,
            MeasureType = OutcomeMeasureType.VAS,
            Score = 5,
            ClinicianId = Guid.NewGuid(),
            DateRecorded = persistedRecordedAt,
            ClinicId = patient.ClinicId
        });
        await _context.SaveChangesAsync();

        var result = await _service.SaveAsync(new NoteWorkspaceV2SaveRequest
        {
            PatientId = patient.Id,
            NoteId = note.Id,
            DateOfService = note.DateOfService,
            NoteType = note.NoteType,
            Payload = new NoteWorkspaceV2Payload
            {
                NoteType = note.NoteType,
                Objective = new WorkspaceObjectiveV2
                {
                    OutcomeMeasures =
                    [
                        new OutcomeMeasureEntryV2
                        {
                            MeasureType = OutcomeMeasureType.VAS,
                            Score = 5,
                            RecordedAtUtc = recordedAtUtc
                        }
                    ]
                }
            }
        });

        Assert.True(result.IsValid);
        var persisted = Assert.Single(_context.OutcomeMeasureResults.Where(entry => entry.NoteId == note.Id));
        Assert.Equal(OutcomeMeasureType.VAS, persisted.MeasureType);
        Assert.Equal(5, persisted.Score);
        Assert.Equal(persistedRecordedAt.Ticks, persisted.DateRecorded.Ticks);
    }

    [Fact]
    public async Task GetEvaluationSeedAsync_PrefersSubmittedIntakeAndKeepsRecommendedMeasuresSeparate()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Seed",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var submittedDraft = new IntakeResponseDraft
        {
            PatientId = patient.Id,
            PainSeverityScore = 6,
            UsesAssistiveDevices = true,
            MedicalHistoryNotes = "History of recurrent knee pain.",
            SelectedComorbidities = ["Hypertension (High Blood Pressure)"],
            SelectedAssistiveDevices = ["Cane"],
            SelectedLivingSituations = ["Lives alone"],
            SelectedHouseLayoutOptions = ["Single-Story Home: Bedroom and bathroom on main floor"],
            RecommendedOutcomeMeasures = ["LEFS", "KOOS"]
        };

        var newerDraft = new IntakeResponseDraft
        {
            PatientId = patient.Id,
            PainSeverityScore = 2,
            RecommendedOutcomeMeasures = ["DASH"]
        };

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(CreateIntakeForm(
            patient.Id,
            submittedDraft,
            new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                MedicationIds = ["zestril-lisinopril"],
                PainDescriptorIds = ["aching"],
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee",
                        Lateralities = ["left"]
                    }
                ]
            },
            selectedBodyRegion: "LeftKneeFront",
            submittedAt: new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc),
            isLocked: false,
            lastModifiedUtc: new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc)));
        _context.IntakeForms.Add(CreateIntakeForm(
            patient.Id,
            newerDraft,
            new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "shoulder",
                        Lateralities = ["right"]
                    }
                ]
            },
            selectedBodyRegion: "RightShoulderFront",
            submittedAt: null,
            isLocked: false,
            lastModifiedUtc: new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc)));

        await _context.SaveChangesAsync();

        var seed = await _service.GetEvaluationSeedAsync(patient.Id);

        Assert.NotNull(seed);
        Assert.True(seed!.FromLockedSubmittedIntake);
        Assert.Equal(WorkspaceSeedKind.IntakePrefill, seed.Payload.SeedContext.Kind);
        Assert.Equal(seed.SourceIntakeId, seed.Payload.SeedContext.SourceIntakeId);
        Assert.True(seed.Payload.SeedContext.FromLockedSubmittedIntake);
        Assert.Equal(6, seed.Payload.Subjective.CurrentPainScore);
        Assert.Contains("Lives alone", seed.Payload.Subjective.LivingSituation);
        Assert.Equal(
            "Single-Story Home: Bedroom and bathroom on main floor",
            seed.Payload.Subjective.OtherLivingSituation);
        Assert.Contains("Hypertension (High Blood Pressure)", seed.Payload.Subjective.Comorbidities);
        Assert.Contains("Cane", seed.Payload.Subjective.AssistiveDevice.Devices);
        Assert.True(seed.Payload.Subjective.TakingMedications);
        Assert.Equal("Zestril / Lisinopril", Assert.Single(seed.Payload.Subjective.Medications).Name);
        Assert.Contains("Left leg", seed.Payload.Subjective.Locations);
        Assert.Equal(BodyPart.Knee, seed.Payload.Objective.PrimaryBodyPart);
        Assert.Equal(["LEFS", "NPRS", "PSFS"], seed.Payload.Objective.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
        Assert.Empty(seed.Payload.Objective.OutcomeMeasures);
        Assert.Empty(_context.OutcomeMeasureResults);
    }

    [Fact]
    public async Task GetEvaluationSeedAsync_FallsBackToLatestUnlockedDraftWhenNoSubmittedIntakeExists()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Draft",
            LastName = "Only",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var draft = new IntakeResponseDraft
        {
            PatientId = patient.Id,
            PainSeverityScore = 3,
            RecommendedOutcomeMeasures = ["DASH"],
            SelectedLivingSituations = ["Lives with spouse/partner"]
        };

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(CreateIntakeForm(
            patient.Id,
            draft,
            new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "shoulder",
                        Lateralities = ["right"]
                    }
                ]
            },
            selectedBodyRegion: "RightShoulderFront",
            submittedAt: null,
            isLocked: false,
            lastModifiedUtc: new DateTime(2026, 4, 6, 12, 0, 0, DateTimeKind.Utc)));

        await _context.SaveChangesAsync();

        var seed = await _service.GetEvaluationSeedAsync(patient.Id);

        Assert.NotNull(seed);
        Assert.False(seed!.FromLockedSubmittedIntake);
        Assert.Equal(WorkspaceSeedKind.IntakePrefill, seed.Payload.SeedContext.Kind);
        Assert.False(seed.Payload.SeedContext.FromLockedSubmittedIntake);
        Assert.Equal(3, seed.Payload.Subjective.CurrentPainScore);
        Assert.Contains("Right shoulder", seed.Payload.Subjective.Locations);
        Assert.Equal(BodyPart.Shoulder, seed.Payload.Objective.PrimaryBodyPart);
        Assert.Equal(["DASH", "NPRS", "PSFS", "QuickDASH"], seed.Payload.Objective.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
    }

    [Fact]
    public async Task GetEvaluationSeedAsync_ReturnsNull_WhenNewerEvaluationAlreadyExists()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Consumed",
            LastName = "Seed",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var intakeTimestamp = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc);
        var draft = new IntakeResponseDraft
        {
            PatientId = patient.Id,
            PainSeverityScore = 5,
            RecommendedOutcomeMeasures = ["LEFS"]
        };

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(CreateIntakeForm(
            patient.Id,
            draft,
            new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee",
                        Lateralities = ["left"]
                    }
                ]
            },
            selectedBodyRegion: "LeftKneeFront",
            submittedAt: intakeTimestamp,
            isLocked: true,
            lastModifiedUtc: intakeTimestamp));
        _context.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = intakeTimestamp.Date.AddDays(1),
            ContentJson = "{}",
            CreatedUtc = intakeTimestamp.AddHours(2),
            LastModifiedUtc = intakeTimestamp.AddHours(2)
        });

        await _context.SaveChangesAsync();

        var seed = await _service.GetEvaluationSeedAsync(patient.Id);

        Assert.Null(seed);
    }

    [Fact]
    public async Task GetCarryForwardSeedAsync_UsesLatestSignedEligibleSource_AndClearsVisitSpecificData()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Carry",
            LastName = "Forward",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var sourceNote = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            DateOfService = new DateTime(2026, 4, 1),
            CreatedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            SignatureHash = "signed",
            SignedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            NoteStatus = NoteStatus.Signed,
            ContentJson = JsonSerializer.Serialize(new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation,
                Subjective = new WorkspaceSubjectiveV2
                {
                    Problems = ["Pain"],
                    Locations = ["Left knee"],
                    CurrentPainScore = 6,
                    Comorbidities = ["Hypertension"],
                    Medications = [new MedicationEntryV2 { Name = "Ibuprofen" }]
                },
                Objective = new WorkspaceObjectiveV2
                {
                    PrimaryBodyPart = BodyPart.Knee,
                    Metrics =
                    [
                        new ObjectiveMetricInputV2
                        {
                            BodyPart = BodyPart.Knee,
                            MetricType = MetricType.ROM,
                            Value = "100"
                        }
                    ],
                    RecommendedOutcomeMeasures = ["LEFS"],
                    OutcomeMeasures =
                    [
                        new OutcomeMeasureEntryV2
                        {
                            MeasureType = OutcomeMeasureType.LEFS,
                            Score = 44,
                            RecordedAtUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
                        }
                    ],
                    ClinicalObservationNotes = "Prior observation"
                },
                Assessment = new WorkspaceAssessmentV2
                {
                    AssessmentNarrative = "Prior assessment",
                    DiagnosisCodes = [new DiagnosisCodeV2 { Code = "M25.562", Description = "Pain in left knee" }],
                    Goals = [new WorkspaceGoalEntryV2 { Description = "Return to stairs", Status = GoalStatus.Active }]
                },
                Plan = new WorkspacePlanV2
                {
                    TreatmentFrequencyDaysPerWeek = [2],
                    TreatmentDurationWeeks = [6],
                    TreatmentFocuses = ["Strength"],
                    SelectedCptCodes = [new PlannedCptCodeV2 { Code = "97110", Units = 2 }],
                    ClinicalSummary = "Prior plan summary"
                }
            })
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(sourceNote);
        _context.PatientGoals.Add(new PatientGoal
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ClinicId = patient.ClinicId,
            Description = "Return to stairs",
            Category = "Mobility",
            Timeframe = GoalTimeframe.ShortTerm,
            Status = GoalStatus.Active,
            Source = GoalSource.ClinicianAuthored,
            OriginatingNoteId = sourceNote.Id,
            CreatedUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        });
        _context.OutcomeMeasureResults.Add(new OutcomeMeasureResult
        {
            PatientId = patient.Id,
            NoteId = sourceNote.Id,
            MeasureType = OutcomeMeasureType.LEFS,
            Score = 44,
            ClinicianId = Guid.NewGuid(),
            DateRecorded = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc),
            ClinicId = patient.ClinicId
        });
        await _context.SaveChangesAsync();

        var seed = await _service.GetCarryForwardSeedAsync(patient.Id, NoteType.ProgressNote);

        Assert.NotNull(seed);
        Assert.Equal(sourceNote.Id, seed!.SourceNoteId);
        Assert.Equal(NoteType.Evaluation, seed.SourceNoteType);
        Assert.Equal(NoteType.ProgressNote, seed.TargetNoteType);
        Assert.Equal(NoteType.ProgressNote, seed.Payload.NoteType);
        Assert.Equal(WorkspaceSeedKind.SignedCarryForward, seed.Payload.SeedContext.Kind);
        Assert.Equal(sourceNote.Id, seed.Payload.SeedContext.SourceNoteId);
        Assert.Equal(NoteType.Evaluation, seed.Payload.SeedContext.SourceNoteType);
        Assert.Contains("Pain", seed.Payload.Subjective.Problems);
        Assert.Equal(6, seed.Payload.Subjective.CurrentPainScore);
        Assert.Equal("Ibuprofen", Assert.Single(seed.Payload.Subjective.Medications).Name);
        Assert.Equal(BodyPart.Knee, seed.Payload.Objective.PrimaryBodyPart);
        Assert.Equal(["LEFS"], seed.Payload.Objective.RecommendedOutcomeMeasures);
        Assert.Empty(seed.Payload.Objective.Metrics);
        Assert.Empty(seed.Payload.Objective.OutcomeMeasures);
        Assert.Null(seed.Payload.Objective.ClinicalObservationNotes);
        Assert.Empty(seed.Payload.Plan.SelectedCptCodes);
        Assert.Null(seed.Payload.Plan.ClinicalSummary);
        Assert.Single(seed.Payload.Assessment.DiagnosisCodes);
        Assert.Single(seed.Payload.Assessment.Goals);
        Assert.Equal(string.Empty, seed.Payload.Assessment.AssessmentNarrative);
    }

    [Fact]
    public async Task LoadAsync_DraftLegacyNote_BackfillsCanonicalWorkspacePayload()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "Draft",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 10),
            ContentJson = """
                          {
                            "subjective": {
                              "currentPainScore": 4,
                              "functionalLimitations": ["Difficulty walking"]
                            },
                            "assessment": {
                              "assessmentNarrative": "Legacy narrative"
                            }
                          }
                          """,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.Equal(4, loaded!.Payload.Subjective.CurrentPainScore);
        Assert.Equal("Legacy narrative", loaded.Payload.Assessment.AssessmentNarrative);

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        using var storedJson = JsonDocument.Parse(stored.ContentJson);
        Assert.Equal(WorkspaceSchemaVersions.EvalReevalProgressV2, storedJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(4, storedJson.RootElement.GetProperty("subjective").GetProperty("currentPainScore").GetInt32());
    }

    [Fact]
    public async Task LoadAsync_DraftLegacyTypedEvaluationContent_BackfillsCanonicalWorkspacePayload()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "TypedEval",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Evaluation,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 12),
            ContentJson = JsonSerializer.Serialize(new EvaluationContent
            {
                SubjectiveComplaints = "Right shoulder pain after tennis",
                FunctionalLimitations = "Unable to lift overhead",
                Assessment = "Presentation consistent with shoulder impingement",
                PlanOfCare = new PlanOfCareContent
                {
                    FrequencyDuration = "2x/week for 6 weeks",
                    SkilledInterventions = "Therapeutic exercise",
                    ShortTermGoals = ["Reach overhead without pain"]
                }
            }),
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Right shoulder pain after tennis", loaded!.Payload.Subjective.NarrativeContext.ChiefComplaint);
        Assert.Equal("Presentation consistent with shoulder impingement", loaded.Payload.Assessment.AssessmentNarrative);
        Assert.Contains("2x/week for 6 weeks", loaded.Payload.Plan.PlanOfCareNarrative);
        Assert.Contains(loaded.Payload.Assessment.Goals, goal => goal.Description == "Reach overhead without pain");

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        using var storedJson = JsonDocument.Parse(stored.ContentJson);
        Assert.Equal(WorkspaceSchemaVersions.EvalReevalProgressV2, storedJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "Right shoulder pain after tennis",
            storedJson.RootElement.GetProperty("subjective").GetProperty("narrativeContext").GetProperty("chiefComplaint").GetString());
    }

    [Fact]
    public async Task LoadAsync_DraftLegacyDryNeedlingNote_BackfillsCanonicalWorkspacePayload()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "DryNeedling",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var legacyContent = """
                            {
                              "workspaceNoteType": "Dry Needling Note",
                              "dryNeedling": {
                                "dateOfTreatment": "2026-04-11T00:00:00Z",
                                "location": "Upper trapezius",
                                "needlingType": "Deep dry needling",
                                "painBefore": 6,
                                "painAfter": 2,
                                "responseDescription": "Improved cervical rotation",
                                "additionalNotes": "No adverse response"
                              }
                            }
                            """;

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 11),
            ContentJson = legacyContent,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.Payload.DryNeedling);
        Assert.Equal("Upper trapezius", loaded.Payload.DryNeedling!.Location);
        Assert.Equal("Deep dry needling", loaded.Payload.DryNeedling.NeedlingType);
        Assert.Equal(6, loaded.Payload.DryNeedling.PainBefore);
        Assert.Equal(2, loaded.Payload.DryNeedling.PainAfter);

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        using var storedJson = JsonDocument.Parse(stored.ContentJson);
        Assert.Equal(WorkspaceSchemaVersions.EvalReevalProgressV2, storedJson.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Upper trapezius", storedJson.RootElement.GetProperty("dryNeedling").GetProperty("location").GetString());
    }

    [Fact]
    public async Task LoadAsync_SignedLegacyNote_DoesNotBackfillCanonicalWorkspacePayload()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "Signed",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        var legacyContent = """
                            {
                              "subjective": {
                                "currentPainScore": 6
                              }
                            }
                            """;

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Signed,
            SignatureHash = "signed-hash",
            SignedUtc = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc),
            DateOfService = new DateTime(2026, 4, 10),
            ContentJson = legacyContent,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.Equal(6, loaded!.Payload.Subjective.CurrentPainScore);

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        Assert.Equal(legacyContent, stored.ContentJson);
    }

    [Fact]
    public async Task LoadAsync_InvalidLegacyJson_DoesNotOverwriteOriginalContent()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "Invalid",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        const string invalidContent = """{"subjective": }""";

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 10),
            ContentJson = invalidContent,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.Equal(NoteType.ProgressNote, loaded!.Payload.NoteType);

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        Assert.Equal(invalidContent, stored.ContentJson);
    }

    [Fact]
    public async Task LoadAsync_UnrecognizedJsonObject_DoesNotTranslateOrBackfill()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Legacy",
            LastName = "Unknown",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = Guid.NewGuid()
        };

        const string unrelatedContent = """{"foo":"bar"}""";

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            NoteType = NoteType.ProgressNote,
            NoteStatus = NoteStatus.Draft,
            DateOfService = new DateTime(2026, 4, 10),
            ContentJson = unrelatedContent,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        _context.ClinicalNotes.Add(note);
        await _context.SaveChangesAsync();

        var loaded = await _service.LoadAsync(patient.Id, note.Id);

        Assert.NotNull(loaded);
        Assert.Equal(NoteType.ProgressNote, loaded!.Payload.NoteType);
        Assert.Equal(0, loaded.Payload.Subjective.CurrentPainScore);
        Assert.Empty(loaded.Payload.Assessment.AssessmentNarrative);

        var stored = await _context.ClinicalNotes
            .AsNoTracking()
            .FirstAsync(existing => existing.Id == note.Id);
        Assert.Equal(unrelatedContent, stored.ContentJson);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private static IntakeForm CreateIntakeForm(
        Guid patientId,
        IntakeResponseDraft draft,
        IntakeStructuredDataDto structuredData,
        string selectedBodyRegion,
        DateTime? submittedAt,
        bool isLocked,
        DateTime lastModifiedUtc)
    {
        draft.SelectedBodyRegion = selectedBodyRegion;
        draft.StructuredData = structuredData;

        return new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            TemplateVersion = "1.0",
            AccessToken = Guid.NewGuid().ToString("N"),
            ResponseJson = JsonSerializer.Serialize(draft),
            StructuredDataJson = IntakeStructuredDataJson.Serialize(structuredData),
            PainMapData = "{}",
            Consents = "{}",
            SubmittedAt = submittedAt,
            IsLocked = isLocked,
            LastModifiedUtc = lastModifiedUtc
        };
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
