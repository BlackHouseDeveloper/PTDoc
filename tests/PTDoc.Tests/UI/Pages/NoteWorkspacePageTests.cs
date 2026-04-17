using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
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
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
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
                        CurrentPainScore = 7
                    },
                    Objective = new ObjectiveVm
                    {
                        RecommendedOutcomeMeasures = ["LEFS"]
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
            .Add(component => component.PatientId, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            var noteTypeSelect = cut.Find("[data-testid='note-type-select']");
            Assert.Equal("Evaluation Note", noteTypeSelect.GetAttribute("value"));
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
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()), Times.Once);
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
            Assert.Contains("Started a new Progress Note with carry-forward from the latest signed Evaluation Note.", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("input[name='current-pain'][value='4']").HasAttribute("checked"));
            Assert.Contains("Signed Evaluation Note", cut.Find("[data-testid='note-workspace-seed-source']").TextContent, StringComparison.Ordinal);
            Assert.Contains("Medication: Ibuprofen", cut.Markup, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(patientId, It.IsAny<CancellationToken>()), Times.Once);
        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Progress Note", It.IsAny<CancellationToken>()), Times.Once);
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
            Assert.Contains("Started a new Daily Treatment Note with carry-forward from the latest signed Progress Note.", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("3", cut.Find("#current-pain").GetAttribute("value"));
            Assert.Contains("Signed Progress Note", cut.Find("[data-testid='note-workspace-seed-source']").TextContent, StringComparison.Ordinal);
        });

        noteWorkspaceService.Verify(service => service.GetEvaluationSeedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        noteWorkspaceService.Verify(service => service.GetCarryForwardSeedAsync(patientId, "Daily Treatment Note", It.IsAny<CancellationToken>()), Times.Once);
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
                    Objective = new ObjectiveVm(),
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
