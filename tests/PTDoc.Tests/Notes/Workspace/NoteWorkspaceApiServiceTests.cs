using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspaceApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SaveDraftAsync_ProgressNote_UsesV2WorkspaceEndpoint()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v2/notes/workspace/", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            var response = new NoteWorkspaceV2SaveResponse
            {
                Workspace = new NoteWorkspaceV2LoadResponse
                {
                    NoteId = noteId,
                    PatientId = patientId,
                    DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
                    NoteType = NoteType.ProgressNote,
                    IsSigned = false,
                    Payload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.ProgressNote
                    }
                }
            };

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(response, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = patientId,
            WorkspaceNoteType = "Progress Note",
            DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Progress Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm
                {
                    AssessmentNarrative = "Clinician assessment"
                },
                Plan = new PlanVm
                {
                    TreatmentFrequency = "2x/week",
                    TreatmentDuration = "6 weeks"
                }
            }
        });

        Assert.True(result.Success);
        Assert.Equal(noteId, result.NoteId);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal(patientId, document.RootElement.GetProperty("patientId").GetGuid());
        Assert.Equal("Clinician assessment", document.RootElement.GetProperty("payload")
            .GetProperty("assessment")
            .GetProperty("assessmentNarrative")
            .GetString());
        var frequencyDays = document.RootElement.GetProperty("payload")
            .GetProperty("plan")
            .GetProperty("treatmentFrequencyDaysPerWeek")
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToArray();
        var durationWeeks = document.RootElement.GetProperty("payload")
            .GetProperty("plan")
            .GetProperty("treatmentDurationWeeks")
            .EnumerateArray()
            .Select(element => element.GetInt32())
            .ToArray();

        Assert.Equal(new[] { 2 }, frequencyDays);
        Assert.Equal(new[] { 6 }, durationWeeks);
    }

    [Fact]
    public async Task SaveDraftAsync_DischargeNote_UsesV2WorkspaceEndpoint()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var calledV2Endpoint = false;

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/notes/workspace/")
            {
                calledV2Endpoint = true;

                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2SaveResponse
                {
                    Workspace = new NoteWorkspaceV2LoadResponse
                    {
                        NoteId = noteId,
                        PatientId = patientId,
                        DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                        NoteType = NoteType.Discharge,
                        Payload = new NoteWorkspaceV2Payload
                        {
                            NoteType = NoteType.Discharge
                        }
                    }
                }, JsonOptions));
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = patientId,
            WorkspaceNoteType = "Discharge Note",
            DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Discharge Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm(),
                Plan = new PlanVm
                {
                    TreatmentFrequency = "1x/week",
                    TreatmentDuration = "2 weeks"
                }
            }
        });

        Assert.True(result.Success);
        Assert.True(calledV2Endpoint);
    }

    [Fact]
    public async Task LoadAsync_PreservesStructuredPayloadOnSubsequentSave()
    {
        var patientId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        string? saveRequestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/v1/patients/{patientId}/notes")
            {
                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new NoteResponse
                    {
                        Id = noteId,
                        PatientId = patientId,
                        NoteType = NoteType.ProgressNote,
                        ContentJson = """{"schemaVersion":2}""",
                        DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                        NoteStatus = NoteStatus.Draft
                    }
                }, JsonOptions));
            }

            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/v2/notes/workspace/{patientId}/{noteId}")
            {
                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2LoadResponse
                {
                    NoteId = noteId,
                    PatientId = patientId,
                    DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                    NoteType = NoteType.ProgressNote,
                    Payload = new NoteWorkspaceV2Payload
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
                            TakingMedications = true,
                            FunctionalLimitations =
                            [
                                new FunctionalLimitationEntryV2
                                {
                                    Id = "lim-1",
                                    BodyPart = BodyPart.Knee,
                                    Category = "Mobility",
                                    Description = "Difficulty squatting",
                                    IsSourceBacked = true,
                                    Notes = "Worse on stairs"
                                }
                            ],
                            Medications =
                            [
                                new MedicationEntryV2
                                {
                                    Name = "Lisinopril",
                                    Dosage = "10 mg",
                                    Frequency = "daily"
                                }
                            ]
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
                                    Value = "110",
                                    NormValue = "135"
                                }
                            ],
                            SpecialTests =
                            [
                                new SpecialTestResultV2
                                {
                                    Name = "McMurray",
                                    Result = "Positive"
                                }
                            ],
                            PalpationObservation = new PalpationObservationV2
                            {
                                TenderMuscles = ["Quadriceps"]
                            }
                        },
                        Assessment = new WorkspaceAssessmentV2
                        {
                            MotivationLevel = "Motivated — willing to participate with occasional prompting",
                            MotivatingFactors = ["Reduce pain", "Maintain independence"],
                            PatientPersonalGoals = "Walk without guarding",
                            MotivationNotes = "Patient is engaged in the session.",
                            SupportSystemLevel = "Moderate — some support available",
                            SupportSystemDetails = "Spouse available after work.",
                            SupportAdditionalNotes = "Needs transportation backup for late visits."
                        },
                        Plan = new WorkspacePlanV2
                        {
                            TreatmentFocuses = ["Mobility"],
                            SelectedCptCodes =
                            [
                                new PlannedCptCodeV2
                                {
                                    Code = "97110",
                                    Description = "Therapeutic exercise",
                                    Units = 2,
                                    Minutes = 30,
                                    Modifiers = ["GP"],
                                    ModifierOptions = ["GP", "KX", "CQ"],
                                    SuggestedModifiers = ["GP"],
                                    ModifierSource = "Commonly used CPT codes and modifiers.md"
                                }
                            ]
                        }
                    }
                }, JsonOptions));
            }

            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/notes/workspace/")
            {
                saveRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2SaveResponse
                {
                    Workspace = new NoteWorkspaceV2LoadResponse
                    {
                        NoteId = noteId,
                        PatientId = patientId,
                        DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                        NoteType = NoteType.ProgressNote,
                        Payload = new NoteWorkspaceV2Payload
                        {
                            NoteType = NoteType.ProgressNote
                        }
                    }
                }, JsonOptions));
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        });

        var service = CreateService(handler);
        var loaded = await service.LoadAsync(patientId, noteId);

        Assert.True(loaded.Success);
        Assert.NotNull(loaded.Payload.StructuredPayload);
        Assert.True(loaded.Payload.Subjective.TakingMedications);
        Assert.Equal("Motivated — willing to participate with occasional prompting", loaded.Payload.Assessment.MotivationLevel);
        Assert.Contains("Reduce pain", loaded.Payload.Assessment.MotivatingFactors);
        Assert.Equal("Needs transportation backup for late visits.", loaded.Payload.Assessment.SupportAdditionalNotes);

        loaded.Payload.Subjective.FunctionalLimitations = ["Difficulty squatting"];
        loaded.Payload.Subjective.MedicationDetails = "Lisinopril";
        loaded.Payload.Objective.SelectedBodyPart = BodyPart.Knee.ToString();
        loaded.Payload.Assessment.AssessmentNarrative = "Improving steadily";
        loaded.Payload.Assessment.MotivationLevel = "Highly motivated — eager to participate and comply";
        loaded.Payload.Assessment.MotivatingFactors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Reduce pain",
            "Return to work"
        };
        loaded.Payload.Assessment.SupportAdditionalNotes = "Family will help with transportation.";
        loaded.Payload.Plan.TreatmentFrequency = "2x/week";
        loaded.Payload.Plan.TreatmentDuration = "6 weeks";

        var saved = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            NoteId = noteId,
            PatientId = patientId,
            WorkspaceNoteType = "Progress Note",
            DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
            IsExistingNote = true,
            Payload = loaded.Payload
        });

        Assert.True(saved.Success);
        Assert.NotNull(saveRequestBody);

        using var document = JsonDocument.Parse(saveRequestBody!);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal((int)WorkspaceSeedKind.SignedCarryForward, payload.GetProperty("seedContext").GetProperty("kind").GetInt32());
        Assert.Equal("Mobility", payload.GetProperty("plan").GetProperty("treatmentFocuses")[0].GetString());
        Assert.Equal("10 mg", payload.GetProperty("subjective").GetProperty("medications")[0].GetProperty("dosage").GetString());
        Assert.Equal("Mobility", payload.GetProperty("subjective").GetProperty("functionalLimitations")[0].GetProperty("category").GetString());
        Assert.Equal("McMurray", payload.GetProperty("objective").GetProperty("specialTests")[0].GetProperty("name").GetString());
        Assert.Equal((int)MetricType.ROM, payload.GetProperty("objective").GetProperty("metrics")[0].GetProperty("metricType").GetInt32());
        Assert.Equal("Highly motivated — eager to participate and comply", payload.GetProperty("assessment").GetProperty("motivationLevel").GetString());
        var motivatingFactors = payload.GetProperty("assessment").GetProperty("motivatingFactors")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Contains("Reduce pain", motivatingFactors);
        Assert.Contains("Return to work", motivatingFactors);
        Assert.Equal("Family will help with transportation.", payload.GetProperty("assessment").GetProperty("supportAdditionalNotes").GetString());
        Assert.Equal("GP", payload.GetProperty("plan").GetProperty("selectedCptCodes")[0].GetProperty("modifiers")[0].GetString());
        Assert.Equal("Commonly used CPT codes and modifiers.md", payload.GetProperty("plan").GetProperty("selectedCptCodes")[0].GetProperty("modifierSource").GetString());
    }

    [Fact]
    public async Task SaveDraftAsync_PreservesExplicitMedicationDecisionWithoutMedicationEntries()
    {
        var patientId = Guid.NewGuid();
        string? saveRequestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v2/notes/workspace/")
            {
                saveRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2SaveResponse
                {
                    Workspace = new NoteWorkspaceV2LoadResponse
                    {
                        NoteId = Guid.NewGuid(),
                        PatientId = patientId,
                        DateOfService = new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc),
                        NoteType = NoteType.Evaluation,
                        Payload = new NoteWorkspaceV2Payload
                        {
                            NoteType = NoteType.Evaluation
                        }
                    }
                }, JsonOptions));
            }

            throw new InvalidOperationException($"Unexpected request to {request.RequestUri}");
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = patientId,
            WorkspaceNoteType = "Evaluation Note",
            DateOfService = new DateTime(2026, 4, 7, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Evaluation Note",
                Subjective = new SubjectiveVm
                {
                    TakingMedications = false,
                    MedicationDetails = null
                },
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm(),
                Plan = new PlanVm()
            }
        });

        Assert.True(result.Success);
        Assert.NotNull(saveRequestBody);

        using var document = JsonDocument.Parse(saveRequestBody!);
        var subjective = document.RootElement.GetProperty("payload").GetProperty("subjective");
        Assert.False(subjective.GetProperty("takingMedications").GetBoolean());
        Assert.Equal(0, subjective.GetProperty("medications").GetArrayLength());
    }

    [Fact]
    public async Task SaveDraftAsync_ComplianceFailure_ReturnsStructuredValidationPayload()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new NoteWorkspaceV2SaveResponse
            {
                IsValid = false,
                Errors = ["Progress Note required"],
                Warnings = ["Minutes fall below standard 8-minute threshold"],
                RequiresOverride = true,
                RuleType = ComplianceRuleType.EightMinuteRule,
                IsOverridable = true,
                OverrideRequirements =
                [
                    new OverrideRequirement
                    {
                        RuleType = ComplianceRuleType.EightMinuteRule,
                        Message = "Minutes fall below standard 8-minute threshold",
                        AttestationText = "Configured attestation text"
                    }
                ]
            };

            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = Guid.NewGuid(),
            WorkspaceNoteType = "Progress Note",
            DateOfService = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Progress Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm(),
                Plan = new PlanVm()
            }
        });

        Assert.False(result.Success);
        Assert.Equal("Progress Note required", result.ErrorMessage);
        Assert.Collection(result.Errors, error => Assert.Equal("Progress Note required", error));
        Assert.Collection(result.Warnings, warning => Assert.Equal("Minutes fall below standard 8-minute threshold", warning));
        Assert.True(result.RequiresOverride);
        Assert.Equal(ComplianceRuleType.EightMinuteRule, result.RuleType);
        Assert.True(result.IsOverridable);
        Assert.Equal("Configured attestation text", Assert.Single(result.OverrideRequirements).AttestationText);
    }

    [Fact]
    public async Task SaveDraftAsync_WithOverrideSubmission_SendsOverridePayload()
    {
        var patientId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2SaveResponse
            {
                Workspace = new NoteWorkspaceV2LoadResponse
                {
                    NoteId = Guid.NewGuid(),
                    PatientId = patientId,
                    DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                    NoteType = NoteType.Evaluation,
                    Payload = new NoteWorkspaceV2Payload
                    {
                        NoteType = NoteType.Evaluation
                    }
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);

        var result = await service.SaveDraftAsync(new NoteWorkspaceDraft
        {
            PatientId = patientId,
            WorkspaceNoteType = "Evaluation Note",
            DateOfService = new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
            Override = new OverrideSubmission
            {
                RuleType = ComplianceRuleType.EightMinuteRule,
                Reason = "Clinical judgment supports an override for this billing scenario."
            },
            Payload = new NoteWorkspacePayload
            {
                WorkspaceNoteType = "Evaluation Note",
                Subjective = new SubjectiveVm(),
                Objective = new ObjectiveVm(),
                Assessment = new AssessmentWorkspaceVm(),
                Plan = new PlanVm()
            }
        });

        Assert.True(result.Success);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        var overrideElement = document.RootElement.GetProperty("override");
        Assert.Equal("EightMinuteRule", overrideElement.GetProperty("ruleType").GetString());
        Assert.Equal("Clinical judgment supports an override for this billing scenario.", overrideElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task GetBodyRegionCatalogAsync_UsesV2CatalogEndpoint()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v2/notes/workspace/catalogs/body-regions/Knee", request.RequestUri!.AbsolutePath);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new BodyRegionCatalog
            {
                BodyPart = BodyPart.Knee,
                FunctionalLimitationCategories =
                [
                    new CatalogCategory
                    {
                        Name = "Mobility",
                        Items = ["Difficulty squatting"]
                    }
                ]
            }, JsonOptions));
        });

        var service = CreateService(handler);
        var catalog = await service.GetBodyRegionCatalogAsync(BodyPart.Knee);

        Assert.Equal(BodyPart.Knee, catalog.BodyPart);
        Assert.Equal("Mobility", Assert.Single(catalog.FunctionalLimitationCategories).Name);
    }

    [Fact]
    public async Task SearchCptAsync_UsesV2LookupEndpoint()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v2/notes/workspace/lookup/cpt", request.RequestUri!.AbsolutePath);
            Assert.Equal("?q=97110&take=10", request.RequestUri!.Query);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new[]
            {
                new CodeLookupEntry
                {
                    Code = "97110",
                    Description = "Therapeutic exercise",
                    Source = "Commonly used CPT codes and modifiers.md",
                    ModifierOptions = ["GP", "KX", "CQ"],
                    SuggestedModifiers = ["GP"],
                    ModifierSource = "Commonly used CPT codes and modifiers.md"
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);
        var results = await service.SearchCptAsync("97110", 10);

        var entry = Assert.Single(results);
        Assert.Equal("97110", entry.Code);
        Assert.Equal("Therapeutic exercise", entry.Description);
        Assert.Equal(["CQ", "GP", "KX"], entry.ModifierOptions.OrderBy(value => value).ToArray());
        Assert.Equal(["GP"], entry.SuggestedModifiers);
        Assert.Equal("Commonly used CPT codes and modifiers.md", entry.ModifierSource);
    }

    [Fact]
    public async Task GetEvaluationSeedAsync_MapsRecommendedMeasuresSeparatelyFromRecordedScores()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v2/notes/workspace/{patientId}/evaluation-seed", request.RequestUri!.AbsolutePath);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2EvaluationSeedResponse
            {
                PatientId = patientId,
                SourceIntakeId = Guid.NewGuid(),
                FromLockedSubmittedIntake = true,
                Payload = new NoteWorkspaceV2Payload
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
                        CurrentPainScore = 5
                    },
                    Objective = new WorkspaceObjectiveV2
                    {
                        PrimaryBodyPart = BodyPart.Knee,
                        RecommendedOutcomeMeasures = ["LEFS", "KOOS"],
                        OutcomeMeasures = []
                    }
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);
        var result = await service.GetEvaluationSeedAsync(patientId);

        Assert.True(result.Success);
        Assert.True(result.HasSeed);
        Assert.True(result.FromLockedSubmittedIntake);
        Assert.NotNull(result.Payload.StructuredPayload);
        Assert.Equal(WorkspaceSeedKind.IntakePrefill, result.Payload.StructuredPayload!.SeedContext.Kind);
        Assert.Equal(5, result.Payload.Subjective.CurrentPainScore);
        Assert.Equal(BodyPart.Knee.ToString(), result.Payload.Objective.SelectedBodyPart);
        Assert.Equal(["KOOS", "LEFS"], result.Payload.Objective.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
        Assert.Empty(result.Payload.Objective.OutcomeMeasures);
    }

    [Fact]
    public async Task GetCarryForwardSeedAsync_MapsStructuredPayloadAndSourceMetadata()
    {
        var patientId = Guid.NewGuid();

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v2/notes/workspace/{patientId}/carry-forward", request.RequestUri!.AbsolutePath);
            Assert.Equal("?noteType=ProgressNote", request.RequestUri!.Query);

            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(new NoteWorkspaceV2CarryForwardResponse
            {
                PatientId = patientId,
                SourceNoteId = Guid.NewGuid(),
                SourceNoteType = NoteType.Evaluation,
                SourceNoteDateOfService = new DateTime(2026, 4, 1),
                TargetNoteType = NoteType.ProgressNote,
                Payload = new NoteWorkspaceV2Payload
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
                        RecommendedOutcomeMeasures = ["LEFS"],
                        OutcomeMeasures = []
                    },
                    Plan = new WorkspacePlanV2
                    {
                        SelectedCptCodes = []
                    }
                }
            }, JsonOptions));
        });

        var service = CreateService(handler);
        var result = await service.GetCarryForwardSeedAsync(patientId, "Progress Note");

        Assert.True(result.Success);
        Assert.True(result.HasSeed);
        Assert.Equal("Evaluation Note", result.SourceNoteType);
        Assert.Equal(new DateTime(2026, 4, 1), result.SourceNoteDateOfService);
        Assert.NotNull(result.Payload.StructuredPayload);
        Assert.Equal(WorkspaceSeedKind.SignedCarryForward, result.Payload.StructuredPayload!.SeedContext.Kind);
        Assert.Equal(4, result.Payload.Subjective.CurrentPainScore);
        Assert.Equal("Ibuprofen", result.Payload.Subjective.MedicationDetails);
        Assert.Equal(BodyPart.Knee.ToString(), result.Payload.Objective.SelectedBodyPart);
        Assert.Equal(["LEFS"], result.Payload.Objective.RecommendedOutcomeMeasures);
        Assert.Empty(result.Payload.Objective.OutcomeMeasures);
    }

    [Fact]
    public async Task AcceptAiSuggestionAsync_PostsAcceptancePayload()
    {
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/accept-ai-suggestion", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse("""{"success":true}""");
        });

        var service = CreateService(handler);

        var result = await service.AcceptAiSuggestionAsync(noteId, "assessment", "Accepted AI text", "Assessment");

        Assert.True(result.Success);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.Equal("assessment", document.RootElement.GetProperty("section").GetString());
        Assert.Equal("Accepted AI text", document.RootElement.GetProperty("generatedText").GetString());
        Assert.Equal("Assessment", document.RootElement.GetProperty("generationType").GetString());
    }

    [Fact]
    public async Task SubmitAsync_PostsConsentAndIntentPayload()
    {
        var noteId = Guid.NewGuid();
        string? requestBody = null;

        var handler = new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/sign", request.RequestUri!.AbsolutePath);
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return StubHttpMessageHandler.JsonResponse("""{"success":true,"requiresCoSign":false}""");
        });

        var service = CreateService(handler);

        var result = await service.SubmitAsync(noteId, consentAccepted: true, intentConfirmed: true);

        Assert.True(result.Success);
        Assert.False(result.RequiresCoSign);
        Assert.NotNull(requestBody);

        using var document = JsonDocument.Parse(requestBody!);
        Assert.True(document.RootElement.GetProperty("consentAccepted").GetBoolean());
        Assert.True(document.RootElement.GetProperty("intentConfirmed").GetBoolean());
    }

    [Fact]
    public async Task ExportPdfAsync_ReturnsPdfBytesAndHeaders()
    {
        var noteId = Guid.NewGuid();
        var expectedBytes = Encoding.UTF8.GetBytes("pdf-bytes");

        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/v1/notes/{noteId}/export/pdf", request.RequestUri!.AbsolutePath);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(expectedBytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"signed-note.pdf\""
            };

            return response;
        });

        var service = CreateService(handler);

        var result = await service.ExportPdfAsync(noteId);

        Assert.True(result.Success);
        Assert.Equal("signed-note.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(expectedBytes, result.Content);
    }

    private static NoteWorkspaceApiService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new NoteWorkspaceApiService(client);
    }
}
