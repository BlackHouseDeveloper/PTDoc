using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspacePageTests : TestContext
{
    public NoteWorkspacePageTests()
    {
        Services.AddLogging();
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void NewNotePage_AutoStartsEvaluation_WhenApplicableIntakeSeedExists()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Taylor",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = true,
                FromLockedSubmittedIntake = true,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.IntakePrefill,
                            SourceIntakeId = Guid.NewGuid(),
                            FromLockedSubmittedIntake = true,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc)
                        },
                        Subjective = new WorkspaceSubjectiveV2
                        {
                            Problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pain" },
                            Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left knee" },
                            CurrentPainScore = 7,
                            CurrentLevelOfFunction = "Independent in home with rest breaks for prolonged walking.",
                            AssistiveDevice = new AssistiveDeviceDetailsV2
                            {
                                UsesAssistiveDevice = true,
                                Devices = ["cane"]
                            },
                            LivingSituation = ["lives-alone"],
                            OtherLivingSituation = "single-story-main-floor-bed-bath; Basement laundry",
                            Comorbidities = ["hypertension"],
                            Medications = [new MedicationEntryV2 { Name = "zestril-lisinopril" }]
                        },
                        Objective = new WorkspaceObjectiveV2
                        {
                            PrimaryBodyPart = BodyPart.Knee,
                            RecommendedOutcomeMeasures = ["LEFS"]
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        Problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pain" },
                        Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left knee" },
                        CurrentPainScore = 7,
                        CurrentLevelOfFunction = "Independent in home with rest breaks for prolonged walking."
                    },
                    Objective = new ObjectiveVm
                    {
                        RecommendedOutcomeMeasures = ["LEFS"]
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            var noteTypeSelect = cut.Find("[data-testid='note-type-select']");
            Assert.Equal("Evaluation Note", noteTypeSelect.GetAttribute("value"));
            Assert.Single(cut.FindAll("[data-testid='soap-tab-interventions']"));
            Assert.Contains("Started a new Evaluation note with the latest submitted intake prefill.", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("7", cut.Find("#q3-current").GetAttribute("value"));
            Assert.Contains("Submitted intake", cut.Find("[data-testid='note-workspace-seed-source']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Editable draft", cut.Find("[data-testid='note-workspace-seed-state']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Assistive device: Cane", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Living: Lives alone", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Home layout: Single-Story Home: Bedroom and bathroom on main floor", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Other living/home: Basement laundry", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Comorbidity: Hypertension (High Blood Pressure)", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Medication: Zestril / Lisinopril", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Outcome measure: LEFS", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(
                "Independent in home with rest breaks for prolonged walking.",
                cut.Find("#current-level-of-function").GetAttribute("value"));
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ExistingDischargeNote_DoesNotRenderInterventionsTab()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Drew",
                LastName = "Discharge",
                DateOfBirth = new DateTime(1980, 6, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Discharge Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    WorkspaceNoteType = "Discharge Note",
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Discharge
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm(),
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm(),
                    DailyTreatment = new DailyTreatmentVm(),
                    DischargeSubjective = new DischargeSubjectiveVm()
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Discharge Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
            Assert.Empty(cut.FindAll("[data-testid='soap-tab-interventions']"));
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ExistingEvaluationNote_InterventionsRoute_RendersEvaluationInterventionsTab()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Eva",
                LastName = "Evaluation",
                DateOfBirth = new DateTime(1984, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Evaluation Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    WorkspaceNoteType = "Evaluation Note",
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm(),
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm(),
                    BillingSettings = new BillingModifierSettingsVm()
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString())
            .Add(component => component.RequestedSection, "interventions"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Evaluation Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
            Assert.Single(cut.FindAll("[data-testid='soap-tab-interventions']"));
            Assert.NotEmpty(cut.FindAll("[data-testid='evaluation-interventions-section']"));
            Assert.NotEmpty(cut.FindAll("[data-testid='evaluation-cpt-card']"));
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NewNotePage_SeededEvaluation_RendersCanonicalSuggestedOutcomeChipsOnObjectiveTab()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Jamie",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = true,
                FromLockedSubmittedIntake = true,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.IntakePrefill,
                            SourceIntakeId = Guid.NewGuid(),
                            FromLockedSubmittedIntake = true,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc)
                        },
                        Objective = new WorkspaceObjectiveV2
                        {
                            PrimaryBodyPart = BodyPart.Shoulder,
                            RecommendedOutcomeMeasures = ["QuickDASH", "NPRS"]
                        }
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm
                    {
                        SelectedBodyPart = BodyPart.Shoulder.ToString(),
                        RecommendedOutcomeMeasures = ["QuickDASH", "NPRS"]
                    }
                }
            });

        noteWorkspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Shoulder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Shoulder,
                OutcomeMeasureOptions =
                [
                    "DASH - Disabilities of the Arm, Shoulder and Hand",
                    "QuickDASH - Quick Disabilities of the Arm, Shoulder and Hand",
                    "NPRS - Numeric Pain Rating Scale",
                    "PSFS - Patient-Specific Functional Scale"
                ]
            });

        Services.AddAuthorizationCore();
        Services.AddLogging();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Evaluation Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-next']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Suggested from Intake", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("QuickDASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VAS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void NewNotePage_AppliesSignedCarryForward_ForDefaultProgressNote_WhenNoEvaluationSeedExists()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Jordan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = true,
                SourceNoteType = "Evaluation Note",
                SourceNoteDateOfService = new DateTime(2026, 4, 1),
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.SignedCarryForward,
                            SourceNoteId = Guid.NewGuid(),
                            SourceNoteType = NoteType.Evaluation,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 1)
                        },
                        Subjective = new WorkspaceSubjectiveV2
                        {
                            CurrentPainScore = 4,
                            Medications = [new MedicationEntryV2 { Name = "Ibuprofen" }]
                        },
                        Objective = new WorkspaceObjectiveV2
                        {
                            PrimaryBodyPart = BodyPart.Knee,
                            RecommendedOutcomeMeasures = ["LEFS"]
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        CurrentPainScore = 4,
                        TakingMedications = true,
                        MedicationDetails = "Ibuprofen"
                    },
                    Objective = new ObjectiveVm
                    {
                        SelectedBodyPart = BodyPart.Knee.ToString(),
                        RecommendedOutcomeMeasures = ["LEFS"]
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            var noteTypeSelect = cut.Find("[data-testid='note-type-select']");
            Assert.Equal("Progress Note", noteTypeSelect.GetAttribute("value"));
            Assert.Single(cut.FindAll("[data-testid='soap-tab-interventions']"));
            Assert.Contains("Started a new Progress Note with carry-forward from the latest signed Evaluation Note.", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("4", cut.Find("#progress-current-pain").GetAttribute("value"));
            Assert.Contains("Signed Evaluation Note", cut.Find("[data-testid='note-workspace-seed-source']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Medication: Ibuprofen", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NewNotePage_ShowsMedicationFallbackChip_WhenSeedOnlyFlagsTakingMedications()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Jordan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = true,
                FromLockedSubmittedIntake = true,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.IntakePrefill,
                            SourceIntakeId = Guid.NewGuid(),
                            FromLockedSubmittedIntake = true,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc)
                        },
                        Subjective = new WorkspaceSubjectiveV2
                        {
                            TakingMedications = true
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        TakingMedications = true
                    },
                    Objective = new ObjectiveVm()
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Taking medications", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void NewNotePage_AppliesSignedCarryForward_ForPtaDefaultDailyNote()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Casey",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = true,
                SourceNoteType = "Progress Note",
                SourceNoteDateOfService = new DateTime(2026, 4, 2),
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Daily,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.SignedCarryForward,
                            SourceNoteId = Guid.NewGuid(),
                            SourceNoteType = NoteType.ProgressNote,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 2)
                        },
                        Subjective = new WorkspaceSubjectiveV2
                        {
                            CurrentPainScore = 3,
                            Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Right shoulder" }
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        CurrentPainScore = 3,
                        Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Right shoulder" }
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PTA));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            var noteTypeSelect = cut.Find("[data-testid='note-type-select']");
            Assert.Equal("Daily Treatment Note", noteTypeSelect.GetAttribute("value"));
            Assert.Single(cut.FindAll("[data-testid='soap-tab-interventions']"));
            Assert.Contains("Started a new Daily Treatment Note with carry-forward from the latest signed Progress Note.", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("3", cut.Find("#current-pain").GetAttribute("value"));
            Assert.False(cut.Find("#current-pain").HasAttribute("disabled"));
            Assert.Contains("Signed Progress Note", cut.Find("[data-testid='note-workspace-seed-source']").TextContent, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ExistingDailyNote_LoadsPersistedDailyTreatmentFields()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Riley",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Daily Treatment Note",
                DateOfService = new DateTime(2026, 4, 9),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    WorkspaceNoteType = "Daily Treatment Note",
                    DailyTreatment = new DailyTreatmentVm
                    {
                        ResponseToTreatment = "Tolerated gait training without symptom flare."
                    },
                    Plan = new PlanVm
                    {
                        TreatmentFrequency = "2x/week",
                        TreatmentDuration = "6 weeks",
                        HomeExerciseProgramNotes = "Continue HEP."
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString())
            .Add(component => component.RequestedSection, "interventions"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily Treatment Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
            var responseField = cut.Find("#daily-response-to-treatment");
            Assert.True(
                string.Equals("Tolerated gait training without symptom flare.", responseField.GetAttribute("value"), StringComparison.Ordinal) ||
                string.Equals("Tolerated gait training without symptom flare.", responseField.TextContent, StringComparison.Ordinal),
                "Expected persisted response-to-treatment text to render in the Daily/Progress Interventions field.");
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AppointmentDailyFallback_WithSignedCarryForward_KeepsDailyTreatmentNote()
    {
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Jordan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = true,
                SourceNoteType = "Progress Note",
                SourceNoteDateOfService = new DateTime(2026, 4, 2),
                Payload = new NoteWorkspacePayload
                {
                    WorkspaceNoteType = "Daily Treatment Note",
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Daily,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.SignedCarryForward,
                            SourceNoteId = Guid.NewGuid(),
                            SourceNoteType = NoteType.ProgressNote,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 2)
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        CurrentPainScore = 2
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.RequestedNoteType, "Daily Treatment Note")
            .Add(component => component.RequestedAppointmentId, appointmentId.ToString("D"))
            .Add(component => component.RequestedAllowEvaluationFallback, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily Treatment Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
            Assert.Contains("Started a new Daily Treatment Note with carry-forward from the latest signed Progress Note.", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void AppointmentDailyFallback_WithoutCarryForward_UsesEvaluationSeedAndPreservesAppointmentContext()
    {
        var patientId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var savedNoteId = Guid.NewGuid();
        var dateOfService = new DateTime(2026, 4, 9);
        var savedDrafts = new List<NoteWorkspaceDraft>();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Casey",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = true,
                FromLockedSubmittedIntake = true,
                Payload = new NoteWorkspacePayload
                {
                    WorkspaceNoteType = "Evaluation Note",
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.IntakePrefill,
                            SourceIntakeId = Guid.NewGuid(),
                            FromLockedSubmittedIntake = true,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 8, 12, 0, 0, DateTimeKind.Utc)
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        CurrentPainScore = 6,
                        CurrentLevelOfFunction = "Limited walking tolerance."
                    }
                }
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteWorkspaceDraft draft, CancellationToken _) =>
            {
                savedDrafts.Add(draft);
                return new NoteWorkspaceSaveResult
                {
                    Success = true,
                    NoteId = savedNoteId,
                    Status = NoteStatus.Draft
                };
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.RequestedNoteType, "Daily Treatment Note")
            .Add(component => component.RequestedAppointmentId, appointmentId.ToString("D"))
            .Add(component => component.RequestedDateOfService, dateOfService.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Add(component => component.RequestedAllowEvaluationFallback, true));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Evaluation Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
            Assert.Contains("Started a new Evaluation note from this appointment with the latest submitted intake prefill.", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("[data-testid='footer-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var savedDraft = Assert.Single(savedDrafts);
            Assert.Equal("Evaluation Note", savedDraft.WorkspaceNoteType);
            Assert.Equal(appointmentId, savedDraft.AppointmentId);
            Assert.Equal(dateOfService, savedDraft.DateOfService.Date);
        });
    }

    [Fact]
    public void ExplicitDailyNoteWithoutAppointmentFallback_DoesNotUseEvaluationSeed()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Parker",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.RequestedNoteType, "Daily Treatment Note"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Daily Treatment Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value"));
        });

        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ExistingSignedNote_ShowsLockedSeedSummary_WhenStructuredSeedContextIsPersisted()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 6),
                Status = NoteStatus.Signed,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote,
                        SeedContext = new WorkspaceSeedContextV2
                        {
                            Kind = WorkspaceSeedKind.SignedCarryForward,
                            SourceNoteId = Guid.NewGuid(),
                            SourceNoteType = NoteType.Evaluation,
                            SourceReferenceDateUtc = new DateTime(2026, 4, 1)
                        },
                        Subjective = new WorkspaceSubjectiveV2
                        {
                            CurrentPainScore = 5,
                            Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left knee" }
                        }
                    },
                    Subjective = new SubjectiveVm
                    {
                        CurrentPainScore = 5,
                        Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Left knee" }
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Locked after signature", cut.Find("[data-testid='note-workspace-seed-state']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Location: Left knee", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void PtCanApplySaveTimeOverride_FromWorkspacePanel()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var savedDrafts = new List<NoteWorkspaceDraft>();

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Avery",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 6),
                IsReEvaluation = true,
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm()
                }
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoteWorkspaceDraft draft, CancellationToken _) =>
            {
                savedDrafts.Add(draft);

                return savedDrafts.Count == 1
                    ? new NoteWorkspaceSaveResult
                    {
                        Success = false,
                        ErrorMessage = "Minutes fall below standard 8-minute threshold.",
                        Warnings = ["Minutes fall below standard 8-minute threshold."],
                        RequiresOverride = true,
                        RuleType = ComplianceRuleType.EightMinuteRule,
                        IsOverridable = true,
                        OverrideRequirements =
                        [
                            new OverrideRequirement
                            {
                                RuleType = ComplianceRuleType.EightMinuteRule,
                                Message = "Minutes fall below standard 8-minute threshold.",
                                AttestationText = "I acknowledge this override and attest that the justification is accurate and clinically necessary."
                            }
                        ]
                    }
                    : new NoteWorkspaceSaveResult
                    {
                        Success = true,
                        NoteId = noteId,
                        IsReEvaluation = true,
                        Status = NoteStatus.Draft
                    };
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("PT Compliance Override Required", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Eight-minute rule", cut.Find("[data-testid='note-workspace-override-rule']").TextContent, StringComparison.Ordinal);
        });

        cut.Find("[data-testid='note-workspace-override-reason']").Input("Clinical judgment supports billing this visit despite the 8-minute warning.");
        cut.Find("[data-testid='note-workspace-override-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Draft saved with PT override.", cut.Find("[data-testid='note-workspace-alert']").TextContent, StringComparison.Ordinal);
            Assert.Equal(2, savedDrafts.Count);
            Assert.NotNull(savedDrafts[1].Override);
            Assert.Equal(ComplianceRuleType.EightMinuteRule, savedDrafts[1].Override!.RuleType);
            Assert.Equal("Clinical judgment supports billing this visit despite the 8-minute warning.", savedDrafts[1].Override!.Reason);
            Assert.Equal(noteId, savedDrafts[1].NoteId);
            Assert.True(savedDrafts[1].IsExistingNote);
            Assert.True(savedDrafts[1].IsReEvaluation);
        });
    }

    [Fact]
    public void NewNoteFirstSave_CanonicalRoutePreservesActiveSection()
    {
        var patientId = Guid.NewGuid();
        var savedNoteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Jordan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceSaveResult
            {
                Success = true,
                NoteId = savedNoteId,
                Status = NoteStatus.Draft
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/patient/{patientId}/new-note");

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-next']").Click();
        cut.WaitForAssertion(() => Assert.Contains("soap-tab-nav__tab--active", cut.Find("[data-testid='soap-tab-objective']").GetAttribute("class") ?? string.Empty, StringComparison.Ordinal));

        cut.Find("[data-testid='footer-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.EndsWith(
                $"/patient/{patientId}/note/{savedNoteId:D}?section=objective",
                Services.GetRequiredService<NavigationManager>().Uri,
                StringComparison.Ordinal);
            Assert.Contains("Saved", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ManualSave_ShowsSavingStateWhilePersistenceIsInFlight()
    {
        var patientId = Guid.NewGuid();
        var savedNoteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);
        var saveCompletion = new TaskCompletionSource<NoteWorkspaceSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .Returns(() => saveCompletion.Task);

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        var clickTask = cut.Find("[data-testid='footer-save']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Saving", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        });

        saveCompletion.SetResult(new NoteWorkspaceSaveResult
        {
            Success = true,
            NoteId = savedNoteId,
            Status = NoteStatus.Draft
        });

        await clickTask;

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Saved", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ManualSaveFailure_ShowsFailedStateInlineBannerAndToast()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = "Treatment frequency is required before saving."
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='soap-tab-plan']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Plan for next visit", cut.Markup, StringComparison.Ordinal));
        cut.Find("textarea").Input("Manual save failure regression edit.");

        cut.Find("[data-testid='footer-save']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Treatment frequency is required before saving.", cut.Find("[data-testid='note-workspace-alert']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Failed", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Treatment frequency is required before saving.", toast.Message);
        });
    }

    [Fact]
    public void ManualSaveFailure_WithWarningsOnly_StillUsesErrorTone()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceEvaluationSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceCarryForwardSeedResult
            {
                Success = true,
                HasSeed = false
            });

        noteWorkspaceService
            .Setup(service => service.SaveDraftAsync(It.IsAny<NoteWorkspaceDraft>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceSaveResult
            {
                Success = false,
                ErrorMessage = "Draft save was rejected.",
                Warnings = ["Review treatment frequency."]
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='note-workspace-alert']");
            Assert.Contains("Draft save was rejected.", alert.TextContent, StringComparison.Ordinal);
            Assert.Contains("note-workspace__alert--error", alert.ClassList);
            Assert.Contains("Failed", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        });
    }


    [Fact]
    public void ExistingProgressNote_LoadSyncsSelectedBodyPartSoObjectiveCatalogLoads()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Loose);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm
                    {
                        SelectedBodyPart = BodyPart.Knee.ToString(),
                        CurrentPainScore = 4
                    },
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm()
                }
            });

        noteWorkspaceService
            .Setup(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                NormalRangeOfMotionOptions = ["Knee flexion 0-135"],
                SpecialTestsOptions = ["McMurray"],
                TenderMuscleOptions = ["Quadriceps"],
                ExerciseOptions = ["Heel slides"],
                MmtGradeOptions = ["4/5", "5/5"]
            });

        Services.AddAuthorizationCore();
        Services.AddLogging();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-next']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Knee", cut.Find("[data-testid='objective-body-part-select']").GetAttribute("value"));
            Assert.Contains("Knee flexion 0-135", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("McMurray", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Select a body part to load source-backed ROM options.", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetBodyRegionCatalogAsync(BodyPart.Knee, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ExistingDailyTreatmentNote_AssessmentRendersAdditionalNotesOnly()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Ella",
                LastName = "Adams",
                DateOfBirth = new DateTime(1971, 2, 14, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Daily Treatment Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Daily
                    },
                    Subjective = new SubjectiveVm
                    {
                        Locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Knee Pain" },
                        OtherLocation = "Right knee",
                        OtherProblem = "Pain with stairs"
                    },
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm
                    {
                        AssessmentNarrative = "Existing assessment"
                    },
                    Plan = new PlanVm(),
                    DailyTreatment = new DailyTreatmentVm
                    {
                        SubjectiveUpdate = "Reports symptoms improved after HEP.",
                        ChangesSinceLastVisit = "Better stair tolerance.",
                        NewOrChangedSymptoms = "No new symptoms."
                    }
                }
            });

        Services.AddAuthorizationCore();
        Services.AddLogging();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Daily Treatment Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='soap-tab-assessment']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='daily-progress-simple-assessment-section']"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Additional Notes", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Existing assessment", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Generate Assessment", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Deficits &amp; Impairments", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("ICD-10 Diagnosis Codes", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        aiService.Verify(service => service.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ExistingDailyTreatmentNote_AssessmentWithoutDailyComplaintStillRendersAdditionalNotes()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Ella",
                LastName = "Adams",
                DateOfBirth = new DateTime(1971, 2, 14, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Daily Treatment Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Daily
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm
                    {
                        AssessmentNarrative = "Existing assessment"
                    },
                    Plan = new PlanVm(),
                    DailyTreatment = new DailyTreatmentVm()
                }
            });

        Services.AddAuthorizationCore();
        Services.AddLogging();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton<IOutcomeMeasureRegistry>(new OutcomeMeasureRegistry());
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Daily Treatment Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='soap-tab-assessment']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='daily-progress-simple-assessment-section']"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Additional Notes", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Existing assessment", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Generate Assessment", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("A chief complaint is required before generating an assessment.", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        aiService.Verify(service => service.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ReviewPage_RegeneratedSummaryRequiresExplicitAcceptance()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm
                    {
                        ClinicalSummary = "Existing summary"
                    }
                }
            });

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Progress note",
                    SelectedBodyPart = "Knee"
                },
                Success = true
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-review']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='soap-review-page']"));

        cut.FindAll("button.soap-review-page__text-action")
            .First(button => button.TextContent.Contains("Regenerate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("AI summary draft", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Accept Summary", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button.soap-review-page__button")
            .First(button => button.TextContent.Contains("Discard", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Existing summary", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("AI summary draft", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        aiService.VerifyAll();
    }

    [Fact]
    public void ReviewPage_AcceptSummary_PersistsReviewDecisionAndMarksNoteUnsaved()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm
                    {
                        ClinicalSummary = "Existing summary"
                    }
                }
            });

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Progress note",
                    SelectedBodyPart = "Knee"
                },
                Success = true
            });

        noteWorkspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceAiAcceptanceResult
            {
                Success = true
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-review']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='soap-review-page']"));

        cut.FindAll("button.soap-review-page__text-action")
            .First(button => button.TextContent.Contains("Regenerate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("AI summary draft", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Accept Summary", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button.soap-review-page__button")
            .First(button => button.TextContent.Contains("Accept Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='note-workspace-alert']");
            Assert.Contains("Clinical summary accepted. Save the note to keep the updated summary.", alert.TextContent, StringComparison.Ordinal);
            Assert.Contains("AI summary draft", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Accept Summary", cut.Markup, StringComparison.Ordinal);
        });

        cut.FindAll("button.soap-review-page__text-action")
            .Last(button => button.TextContent.Contains("Edit", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("AI summary draft", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Unsaved", cut.Find("[data-testid='footer-state-label']").TextContent, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.VerifyAll();
        aiService.VerifyAll();
    }

    [Fact]
    public void ReviewPage_RegeneratedSummaryFailure_ShowsSharedErrorWithReferenceId()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm
                    {
                        ClinicalSummary = "Existing summary"
                    }
                }
            });

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Progress note",
                    SelectedBodyPart = "Knee"
                },
                Success = false,
                ErrorMessage = "AI generation failed. Please try again or contact support. Reference ID: ai-ref-123"
            });

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-review']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='soap-review-page']"));

        cut.FindAll("button.soap-review-page__text-action")
            .First(button => button.TextContent.Contains("Regenerate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='note-workspace-alert']");
            Assert.Contains("AI generation failed. Please try again or contact support. Reference ID: ai-ref-123", alert.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Accept Summary", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Existing summary", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        aiService.VerifyAll();
    }

    [Fact]
    public void ReviewPage_RegeneratedSummaryException_ShowsSharedErrorAndKeepsExistingSummary()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm
                    {
                        ClinicalSummary = "Existing summary"
                    }
                }
            });

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network timeout"));

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-review']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='soap-review-page']"));

        cut.FindAll("button.soap-review-page__text-action")
            .First(button => button.TextContent.Contains("Regenerate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='note-workspace-alert']");
            Assert.Contains("Unable to generate a clinical summary.", alert.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Accept Summary", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Existing summary", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        aiService.VerifyAll();
    }

    [Fact]
    public void ReviewPage_AcceptSummaryException_ShowsSharedErrorAndKeepsPendingSummaryForRetry()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var aiService = new Mock<IAiClinicalGenerationService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientResponse
            {
                Id = patientId,
                FirstName = "Morgan",
                LastName = "Patient",
                DateOfBirth = new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        noteWorkspaceService
            .Setup(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteWorkspaceLoadResult
            {
                Success = true,
                NoteId = noteId,
                WorkspaceNoteType = "Progress Note",
                DateOfService = new DateTime(2026, 4, 18),
                Status = NoteStatus.Draft,
                Payload = new NoteWorkspacePayload
                {
                    StructuredPayload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    },
                    Subjective = new SubjectiveVm(),
                    Objective = new ObjectiveVm { SelectedBodyPart = "Knee" },
                    Assessment = new AssessmentWorkspaceVm(),
                    Plan = new PlanVm
                    {
                        ClinicalSummary = "Existing summary"
                    }
                }
            });

        aiService
            .Setup(service => service.GeneratePlanOfCareAsync(It.IsAny<PlanOfCareGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanGenerationResult
            {
                GeneratedText = "AI summary draft",
                Confidence = 0.85,
                SourceInputs = new PlanOfCareGenerationRequest
                {
                    NoteId = noteId,
                    Diagnosis = "Progress note",
                    SelectedBodyPart = "Knee"
                },
                Success = true
            });

        noteWorkspaceService
            .Setup(service => service.AcceptAiSuggestionAsync(
                noteId,
                "plan",
                "AI summary draft",
                "ClinicalSummary",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("accept request failed"));

        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthenticationStateProvider(Roles.PT));
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);
        Services.AddSingleton(aiService.Object);
        Services.AddSingleton(new DraftAutosaveService());

        var cut = RenderComponent<global::PTDoc.UI.Pages.Patient.NoteWorkspacePage>(parameters => parameters
            .Add(component => component.PatientId, patientId.ToString())
            .Add(component => component.NoteId, noteId.ToString()));

        cut.WaitForAssertion(() => Assert.Equal("Progress Note", cut.Find("[data-testid='note-type-select']").GetAttribute("value")));

        cut.Find("[data-testid='footer-review']").Click();

        cut.WaitForAssertion(() => cut.Find("[data-testid='soap-review-page']"));

        cut.FindAll("button.soap-review-page__text-action")
            .First(button => button.TextContent.Contains("Regenerate", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() => Assert.Contains("Accept Summary", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button.soap-review-page__button")
            .First(button => button.TextContent.Contains("Accept Summary", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find("[data-testid='note-workspace-alert']");
            Assert.Contains("Unable to accept AI-generated summary content.", alert.TextContent, StringComparison.Ordinal);
            Assert.Contains("AI-generated clinical summary ready for review", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("AI summary draft", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Accept Summary", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Unsaved", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.LoadAsync(patientId, noteId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.VerifyAll();
        aiService.VerifyAll();
    }

    [Fact]
    public void GetUserFacingMessage_TrimsHttpRequestMessages()
    {
        var message = GetNoteWorkspaceUserFacingMessage(
            new HttpRequestException("  Connection refused (localhost:5170)  "),
            "Unable to save draft right now. Please retry.");

        Assert.Equal("Connection refused (localhost:5170)", message);
    }

    [Fact]
    public void GetUserFacingMessage_FallsBackForGenericStatusMessagesAfterTrim()
    {
        var message = GetNoteWorkspaceUserFacingMessage(
            new HttpRequestException("  Response status code does not indicate success: 500 (Internal Server Error).  "),
            "Unable to export the note as PDF.");

        Assert.Equal("Unable to export the note as PDF.", message);
    }

    private static string GetNoteWorkspaceUserFacingMessage(Exception exception, string fallback)
    {
        var method = typeof(global::PTDoc.UI.Pages.Patient.NoteWorkspacePage).GetMethod(
            "GetUserFacingMessage",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, new object[] { exception, fallback }));
    }

    private sealed class TestAuthenticationStateProvider(string role) : AuthenticationStateProvider
    {
        private readonly AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "test-user"),
            new Claim(ClaimTypes.Role, role)
        ], "TestAuth")));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
    }
}
