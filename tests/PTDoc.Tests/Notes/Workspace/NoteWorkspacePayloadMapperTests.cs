using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspacePayloadMapperTests
{
    private const string CptSource = "docs/clinicrefdata/Commonly used CPT codes and modifiers.md";

    private readonly NoteWorkspacePayloadMapper _mapper =
        new(new IntakeReferenceDataCatalogService(), new OutcomeMeasureRegistry());

    [Fact]
    public void MapToUiPayload_SplitsCatalogSelectionsFromLegacySubjectiveValues()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Subjective = new WorkspaceSubjectiveV2
            {
                AssistiveDevice = new AssistiveDeviceDetailsV2
                {
                    UsesAssistiveDevice = true,
                    Devices = ["cane", "Legacy Walker"],
                    OtherDevice = "Custom hand rail"
                },
                LivingSituation = ["lives-alone"],
                OtherLivingSituation = "single-story-main-floor-bed-bath; Basement laundry",
                Comorbidities = ["hypertension"],
                TakingMedications = true,
                Medications =
                [
                    new MedicationEntryV2
                    {
                        Name = "zestril-lisinopril",
                        Dosage = "10 mg",
                        Frequency = "daily"
                    },
                    new MedicationEntryV2
                    {
                        Name = "Fish oil"
                    }
                ]
            }
        };

        var uiPayload = _mapper.MapToUiPayload(payload);

        Assert.Contains("Cane", uiPayload.Subjective.SelectedAssistiveDeviceLabels);
        Assert.Equal("Legacy Walker; Custom hand rail", uiPayload.Subjective.OtherAssistiveDevice);
        Assert.Contains("Lives alone", uiPayload.Subjective.LivingSituation);
        Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", uiPayload.Subjective.SelectedHouseLayoutLabels);
        Assert.Equal("Basement laundry", uiPayload.Subjective.OtherLivingSituation);
        Assert.Contains("Hypertension (High Blood Pressure)", uiPayload.Subjective.Comorbidities);
        Assert.Contains("Zestril / Lisinopril", uiPayload.Subjective.SelectedMedicationLabels);
        Assert.Equal("Fish oil", uiPayload.Subjective.MedicationDetails);
    }

    [Fact]
    public void MapToUiPayload_NormalizesCptModifierSource()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Plan = new WorkspacePlanV2
            {
                SelectedCptCodes =
                [
                    new PlannedCptCodeV2
                    {
                        Code = "97110",
                        Description = "Therapeutic exercise",
                        ModifierSource = "Commonly used CPT codes and modifiers.md"
                    }
                ]
            }
        };

        var uiPayload = _mapper.MapToUiPayload(payload);

        Assert.Equal(CptSource, Assert.Single(uiPayload.Plan.SelectedCptCodes).ModifierSource);
        Assert.Equal(CptSource, Assert.Single(uiPayload.StructuredPayload!.Plan.SelectedCptCodes).ModifierSource);
    }

    [Fact]
    public void MapToV2Payload_NormalizesCptModifierSource()
    {
        var uiPayload = new NoteWorkspacePayload
        {
            StructuredPayload = new NoteWorkspaceV2Payload
            {
                Plan = new WorkspacePlanV2
                {
                    SelectedCptCodes =
                    [
                        new PlannedCptCodeV2
                        {
                            Code = "97110",
                            ModifierSource = "Commonly used CPT codes and modifiers.md"
                        }
                    ]
                }
            },
            Plan = new PlanVm
            {
                SelectedCptCodes =
                [
                    new CptCodeEntry
                    {
                        Code = "97110",
                        Description = "Therapeutic exercise",
                        Units = 1,
                        ModifierSource = "Commonly used CPT codes and modifiers.md"
                    }
                ]
            }
        };

        var result = _mapper.MapToV2Payload(uiPayload, NoteType.Evaluation);

        Assert.Equal(CptSource, Assert.Single(result.Plan.SelectedCptCodes).ModifierSource);
    }

    [Fact]
    public void MapToV2Payload_RoundTripsAssistiveDevicesHomeLayoutAndMedications()
    {
        var uiPayload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Evaluation Note",
            StructuredPayload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation,
                Subjective = new WorkspaceSubjectiveV2
                {
                    AssistiveDevice = new AssistiveDeviceDetailsV2
                    {
                        UsesAssistiveDevice = true,
                        Devices = ["Cane"],
                        OtherDevice = "Custom hand rail"
                    },
                    Medications =
                    [
                        new MedicationEntryV2
                        {
                            Name = "Zestril / Lisinopril",
                            Dosage = "10 mg",
                            Frequency = "daily"
                        }
                    ]
                }
            },
            Subjective = new SubjectiveVm
            {
                UsesAssistiveDevice = true,
                SelectedAssistiveDeviceLabels = ["Cane"],
                OtherAssistiveDevice = "Custom hand rail",
                LivingSituation = ["Lives alone"],
                Comorbidities = ["Hypertension (High Blood Pressure)"],
                SelectedHouseLayoutLabels = ["Single-Story Home: Bedroom and bathroom on main floor"],
                OtherLivingSituation = "Basement laundry",
                TakingMedications = true,
                SelectedMedicationLabels = ["Zestril / Lisinopril"],
                MedicationDetails = "Fish oil"
            },
            Objective = new ObjectiveVm(),
            Assessment = new AssessmentWorkspaceVm(),
            Plan = new PlanVm()
        };

        var structuredPayload = _mapper.MapToV2Payload(uiPayload, NoteType.Evaluation);

        Assert.True(structuredPayload.Subjective.AssistiveDevice.UsesAssistiveDevice);
        Assert.Contains("Cane", structuredPayload.Subjective.AssistiveDevice.Devices);
        Assert.Equal("Custom hand rail", structuredPayload.Subjective.AssistiveDevice.OtherDevice);
        Assert.Contains("Lives alone", structuredPayload.Subjective.LivingSituation);
        Assert.Contains("Hypertension (High Blood Pressure)", structuredPayload.Subjective.Comorbidities);
        Assert.Equal(
            "Single-Story Home: Bedroom and bathroom on main floor; Basement laundry",
            structuredPayload.Subjective.OtherLivingSituation);

        var lisinopril = Assert.Single(structuredPayload.Subjective.Medications.Where(entry =>
            string.Equals(entry.Name, "Zestril / Lisinopril", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("10 mg", lisinopril.Dosage);
        Assert.Equal("daily", lisinopril.Frequency);
        Assert.Contains(structuredPayload.Subjective.Medications, entry =>
            string.Equals(entry.Name, "Fish oil", StringComparison.OrdinalIgnoreCase));

        var roundTrippedUiPayload = _mapper.MapToUiPayload(structuredPayload);

        Assert.Contains("Cane", roundTrippedUiPayload.Subjective.SelectedAssistiveDeviceLabels);
        Assert.Equal("Custom hand rail", roundTrippedUiPayload.Subjective.OtherAssistiveDevice);
        Assert.Contains("Hypertension (High Blood Pressure)", roundTrippedUiPayload.Subjective.Comorbidities);
        Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", roundTrippedUiPayload.Subjective.SelectedHouseLayoutLabels);
        Assert.Equal("Basement laundry", roundTrippedUiPayload.Subjective.OtherLivingSituation);
        Assert.Contains("Zestril / Lisinopril", roundTrippedUiPayload.Subjective.SelectedMedicationLabels);
        Assert.Equal("Fish oil", roundTrippedUiPayload.Subjective.MedicationDetails);
    }

    [Fact]
    public void MapToV2Payload_RoundTripsEvaluationCleanupFields()
    {
        var uiPayload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Evaluation Note",
            StructuredPayload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation
            },
            Subjective = new SubjectiveVm
            {
                SelectedBodyPart = "Knee",
                Problems = ["Pain"],
                PainDescriptors = ["Achy", "Sharp"],
                OtherPainDescriptor = "Radiating",
                PainFrequency = "Intermittent",
                SymptomFrequencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Pain"] = "Only with specific activities"
                },
                SymptomTimeOfDay = ["Morning", "Night"],
                HasImaging = true,
                ImagingModalities = ["MRI", "Other"],
                OtherImagingModality = "Diagnostic ultrasound report",
                ImagingFindings = "Mild tendinopathy."
            },
            Objective = new ObjectiveVm
            {
                SelectedBodyPart = "Knee",
                IsGaitUnremarkable = true,
                IsPostureUnremarkable = true,
                IsPalpationUnremarkable = true,
                PalpationComments = "No focal tenderness.",
                Metrics =
                [
                    new ObjectiveMetricRowEntry
                    {
                        Name = "Knee flexion 0-135",
                        BodyPart = "Knee",
                        MetricType = MetricType.ROM,
                        Value = "120",
                        PreviousValue = "110",
                        NormValue = "0-135"
                    }
                ]
            },
            Assessment = new AssessmentWorkspaceVm
            {
                AssessmentNarrative = "Evaluation assessment.",
                FindingsSummary = "Key findings summary.",
                OverallPrognosis = "Good - significant improvement expected",
                PrognosisNarrative = "Expected to progress with plan of care."
            },
            Plan = new PlanVm(),
            BillingSettings = new BillingModifierSettingsVm
            {
                ModifierWorkflowEnabled = true,
                AutoApplySuggestedModifiers = false,
                RequireSuggestedModifierReview = true
            }
        };

        var structuredPayload = _mapper.MapToV2Payload(uiPayload, NoteType.Evaluation);

        Assert.Contains("Achy", structuredPayload.Subjective.PainDescriptors);
        Assert.Equal("Radiating", structuredPayload.Subjective.OtherPainDescriptor);
        Assert.Equal("Only with specific activities", structuredPayload.Subjective.SymptomFrequencies["Pain"]);
        Assert.Contains("Morning", structuredPayload.Subjective.SymptomTimeOfDay);
        Assert.True(structuredPayload.Subjective.Imaging.HasImaging);
        Assert.Contains("MRI", structuredPayload.Subjective.Imaging.Modalities);
        Assert.Equal("Diagnostic ultrasound report", structuredPayload.Subjective.Imaging.OtherModality);
        Assert.Equal("Mild tendinopathy.", structuredPayload.Subjective.Imaging.Findings);
        Assert.True(structuredPayload.Objective.GaitObservation.IsNormal);
        Assert.True(structuredPayload.Objective.PostureObservation.IsNormal);
        Assert.True(structuredPayload.Objective.PalpationObservation.IsNormal);
        Assert.Equal("No focal tenderness.", structuredPayload.Objective.PalpationObservation.Other);
        var metric = Assert.Single(structuredPayload.Objective.Metrics);
        Assert.Equal(BodyPart.Knee, metric.BodyPart);
        Assert.Equal("110", metric.PreviousValue);
        Assert.Equal("0-135", metric.NormValue);
        Assert.Equal("Key findings summary.", structuredPayload.Assessment.FindingsSummary);
        Assert.Equal("Expected to progress with plan of care.", structuredPayload.Assessment.PrognosisNarrative);
        Assert.False(structuredPayload.BillingSettings.AutoApplySuggestedModifiers);

        var roundTrippedUiPayload = _mapper.MapToUiPayload(structuredPayload);

        Assert.Contains("Sharp", roundTrippedUiPayload.Subjective.PainDescriptors);
        Assert.Equal("Only with specific activities", roundTrippedUiPayload.Subjective.SymptomFrequencies["Pain"]);
        Assert.Contains("Night", roundTrippedUiPayload.Subjective.SymptomTimeOfDay);
        Assert.Contains("Other", roundTrippedUiPayload.Subjective.ImagingModalities);
        Assert.Equal("Mild tendinopathy.", roundTrippedUiPayload.Subjective.ImagingFindings);
        Assert.True(roundTrippedUiPayload.Objective.IsGaitUnremarkable);
        Assert.True(roundTrippedUiPayload.Objective.IsPostureUnremarkable);
        Assert.True(roundTrippedUiPayload.Objective.IsPalpationUnremarkable);
        Assert.Equal("No focal tenderness.", roundTrippedUiPayload.Objective.PalpationComments);
        Assert.Equal("Knee", Assert.Single(roundTrippedUiPayload.Objective.Metrics).BodyPart);
        Assert.Equal("Key findings summary.", roundTrippedUiPayload.Assessment.FindingsSummary);
        Assert.Equal("Expected to progress with plan of care.", roundTrippedUiPayload.Assessment.PrognosisNarrative);
        Assert.False(roundTrippedUiPayload.BillingSettings.AutoApplySuggestedModifiers);
    }

    [Fact]
    public void MapToUiPayload_NormalizesRecommendedOutcomeMeasures()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Objective = new WorkspaceObjectiveV2
            {
                RecommendedOutcomeMeasures = ["LEFS", "KOOS", "VAS/NPRS"]
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal(["LEFS", "NPRS"], result.Objective.RecommendedOutcomeMeasures);
    }

    [Fact]
    public void MapToV2Payload_NormalizesRecommendationsWithoutRetypingVasScores()
    {
        var uiPayload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Evaluation Note",
            StructuredPayload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.Evaluation
            },
            Subjective = new SubjectiveVm(),
            Objective = new ObjectiveVm
            {
                RecommendedOutcomeMeasures = ["KOOS", "QuickDASH", "VAS"],
                OutcomeMeasures =
                [
                    new OutcomeMeasureEntry
                    {
                        Name = "VAS",
                        Score = "5",
                        Date = new DateTime(2026, 4, 5, 12, 0, 0, DateTimeKind.Utc)
                    }
                ]
            },
            Assessment = new AssessmentWorkspaceVm(),
            Plan = new PlanVm()
        };

        var result = _mapper.MapToV2Payload(uiPayload, NoteType.Evaluation);

        Assert.Equal(["NPRS", "QuickDASH"], result.Objective.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
        Assert.Equal(OutcomeMeasureType.VAS, Assert.Single(result.Objective.OutcomeMeasures).MeasureType);
    }

    [Fact]
    public void MapToV2Payload_BlankMmtRow_PreservesStructuredMetricEntry()
    {
        var uiPayload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Progress Note",
            StructuredPayload = new NoteWorkspaceV2Payload
            {
                NoteType = NoteType.ProgressNote,
                Objective = new WorkspaceObjectiveV2
                {
                    PrimaryBodyPart = BodyPart.Knee
                }
            },
            Subjective = new SubjectiveVm(),
            Objective = new ObjectiveVm
            {
                SelectedBodyPart = BodyPart.Knee.ToString(),
                Metrics =
                [
                    new ObjectiveMetricRowEntry
                    {
                        MetricType = MetricType.MMT,
                        Name = string.Empty,
                        Value = string.Empty
                    }
                ]
            },
            Assessment = new AssessmentWorkspaceVm(),
            Plan = new PlanVm()
        };

        var result = _mapper.MapToV2Payload(uiPayload, NoteType.ProgressNote);

        var metric = Assert.Single(result.Objective.Metrics);
        Assert.Equal(MetricType.MMT, metric.MetricType);
        Assert.Equal("MMT", metric.Name);
        Assert.Equal(string.Empty, metric.Value);
        Assert.Equal(BodyPart.Knee, metric.BodyPart);
    }

    [Fact]
    public void MapToUiPayload_WhenPrimaryBodyPartIsOther_UsesSubjectiveStructuredBodyPartForBothTabs()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Subjective = new WorkspaceSubjectiveV2
            {
                FunctionalLimitations =
                [
                    new FunctionalLimitationEntryV2
                    {
                        BodyPart = BodyPart.Knee,
                        Category = "Mobility",
                        Description = "Difficulty with stairs"
                    }
                ]
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = BodyPart.Other
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal(BodyPart.Knee.ToString(), result.Subjective.SelectedBodyPart);
        Assert.Equal(BodyPart.Knee.ToString(), result.Objective.SelectedBodyPart);
    }

    [Fact]
    public void MapToUiPayload_WhenPrimaryBodyPartConflictsWithSubjectiveBodyPart_PrefersPrimaryForBothTabs()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Subjective = new WorkspaceSubjectiveV2
            {
                FunctionalLimitations =
                [
                    new FunctionalLimitationEntryV2
                    {
                        BodyPart = BodyPart.Knee,
                        Category = "Mobility",
                        Description = "Difficulty with stairs"
                    }
                ]
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = BodyPart.Shoulder
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal(BodyPart.Shoulder.ToString(), result.Subjective.SelectedBodyPart);
        Assert.Equal(BodyPart.Shoulder.ToString(), result.Objective.SelectedBodyPart);
    }

    [Fact]
    public void MapToUiPayload_WhenPrimaryAndSubjectiveBodyPartAreMissing_UsesObjectiveMetricBodyPartForBothTabs()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = BodyPart.Other,
                Metrics =
                [
                    new ObjectiveMetricInputV2
                    {
                        Name = "ROM",
                        BodyPart = BodyPart.Knee,
                        MetricType = MetricType.ROM,
                        Value = "110"
                    }
                ]
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal(BodyPart.Knee.ToString(), result.Subjective.SelectedBodyPart);
        Assert.Equal(BodyPart.Knee.ToString(), result.Objective.SelectedBodyPart);
    }

    [Fact]
    public void MapToUiPayload_WhenAllBodyPartSignalsAreMissing_LeavesBothTabsUnselected()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.ProgressNote,
            Subjective = new WorkspaceSubjectiveV2
            {
                FunctionalLimitations = []
            },
            Objective = new WorkspaceObjectiveV2
            {
                PrimaryBodyPart = BodyPart.Other,
                Metrics = []
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Null(result.Subjective.SelectedBodyPart);
        Assert.Null(result.Objective.SelectedBodyPart);
    }

    [Fact]
    public void MapToV2Payload_BuildsCanonicalWorkspacePayload_FromUiDraftState()
    {
        var payload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Daily Treatment Note",
            Subjective = new SubjectiveVm
            {
                SelectedBodyPart = BodyPart.Knee.ToString(),
                Problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pain" },
                FunctionalLimitations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Stairs" },
                TakingMedications = true,
                MedicationDetails = "Ibuprofen"
            },
            Objective = new ObjectiveVm
            {
                SelectedBodyPart = BodyPart.Knee.ToString(),
                ExerciseRows =
                [
                    new ExerciseRowEntry
                    {
                        SuggestedExercise = "Heel slides",
                        ActualExercisePerformed = "Heel slides",
                        SetsRepsDuration = "2x10",
                        CptCode = "97110",
                        CptDescription = "Exercise",
                        TimeMinutes = 15,
                        AssistanceLevel = "Min Assist",
                        Cueing = "Verbal cueing",
                        IncludeInHomeExerciseProgram = true,
                        IsSourceBacked = true
                    }
                ]
            },
            Assessment = new AssessmentWorkspaceVm
            {
                AssessmentNarrative = "Knee pain improving.",
                DiagnosisCodes =
                [
                    new Icd10Entry
                    {
                        Code = "M25.561",
                        Description = "Pain in right knee"
                    }
                ]
            },
            Plan = new PlanVm
            {
                TreatmentFrequency = "2x/week",
                TreatmentDuration = "6 weeks",
                GeneralInterventions =
                [
                    new GeneralInterventionEntry
                    {
                        Name = "Joint mobilization",
                        Category = "Manual therapy",
                        IsSourceBacked = true,
                        Notes = "Grade III",
                        CptCode = "97140",
                        CptDescription = "Manual Therapy",
                        TimeMinutes = 12,
                        AssistanceLevel = "Contact Guard",
                        Cueing = "Tactile cueing",
                        Response = "Improved mobility",
                        IncludeInHomeExerciseProgram = true
                    }
                ],
                SelectedCptCodes =
                [
                    new CptCodeEntry
                    {
                        Code = "97110",
                        Description = "Therapeutic exercise",
                        Units = 1
                    }
                ]
            },
            DailyTreatment = new DailyTreatmentVm
            {
                ChangesSinceLastVisit = "Reports easier stair negotiation.",
                PainLevelChanges = "Pain decreased from 6/10 to 3/10.",
                SubjectiveUpdate = "Mild soreness after last visit resolved within a day.",
                HepAdherence = "Good (70-90%)",
                HepUpdateNotes = "Added heel raises.",
                FunctionalImprovements = "Walking longer distances.",
                NewOrChangedSymptoms = "No new symptoms.",
                BarriersToProgress = "Limited home equipment.",
                PreviousTreatment = "Prior visit focused on mobility.",
                AssociatedSymptoms = ["Stiffness"],
                ResponseToTreatment = "Tolerated strengthening without symptom flare."
            }
        };

        var result = _mapper.MapToV2Payload(payload, NoteType.Daily);

        Assert.Equal(WorkspaceSchemaVersions.EvalReevalProgressV2, result.SchemaVersion);
        Assert.Equal(NoteType.Daily, result.NoteType);
        Assert.Equal(BodyPart.Knee, result.Objective.PrimaryBodyPart);
        Assert.Single(result.Subjective.FunctionalLimitations);
        Assert.Equal("Stairs", result.Subjective.FunctionalLimitations[0].Description);
        Assert.Single(result.Subjective.Medications);
        Assert.Equal("Ibuprofen", result.Subjective.Medications[0].Name);
        Assert.Single(result.Objective.ExerciseRows);
        Assert.Equal("Heel slides", result.Objective.ExerciseRows[0].SuggestedExercise);
        Assert.Equal("97110", result.Objective.ExerciseRows[0].CptCode);
        Assert.Equal("Min Assist", result.Objective.ExerciseRows[0].AssistanceLevel);
        Assert.Equal("Verbal cueing", result.Objective.ExerciseRows[0].Cueing);
        Assert.True(result.Objective.ExerciseRows[0].IncludeInHomeExerciseProgram);
        Assert.Single(result.Assessment.DiagnosisCodes);
        Assert.Equal("M25.561", result.Assessment.DiagnosisCodes[0].Code);
        Assert.Equal([2], result.Plan.TreatmentFrequencyDaysPerWeek);
        Assert.Equal([6], result.Plan.TreatmentDurationWeeks);
        Assert.Single(result.Plan.GeneralInterventions);
        Assert.Equal("Joint mobilization", result.Plan.GeneralInterventions[0].Name);
        Assert.Equal("97140", result.Plan.GeneralInterventions[0].CptCode);
        Assert.Equal("Contact Guard", result.Plan.GeneralInterventions[0].AssistanceLevel);
        Assert.Equal("Tactile cueing", result.Plan.GeneralInterventions[0].Cueing);
        Assert.Equal("Improved mobility", result.Plan.GeneralInterventions[0].Response);
        Assert.True(result.Plan.GeneralInterventions[0].IncludeInHomeExerciseProgram);
        Assert.Contains(result.Plan.SelectedCptCodes, code => code.Code == "97110");
        Assert.Contains(result.Plan.SelectedCptCodes, code => code.Code == "97140");
        Assert.Equal("Reports easier stair negotiation.", result.DailyTreatment.ChangesSinceLastVisit);
        Assert.Equal("Pain decreased from 6/10 to 3/10.", result.DailyTreatment.PainLevelChanges);
        Assert.Equal("Mild soreness after last visit resolved within a day.", result.DailyTreatment.SubjectiveUpdate);
        Assert.Equal("Good (70-90%)", result.DailyTreatment.HepAdherence);
        Assert.Equal("Added heel raises.", result.DailyTreatment.HepUpdateNotes);
        Assert.Equal("Walking longer distances.", result.DailyTreatment.FunctionalImprovements);
        Assert.Equal("No new symptoms.", result.DailyTreatment.NewOrChangedSymptoms);
        Assert.Equal("Limited home equipment.", result.DailyTreatment.BarriersToProgress);
        Assert.Equal("Prior visit focused on mobility.", result.DailyTreatment.PreviousTreatment);
        Assert.Contains("Stiffness", result.DailyTreatment.AssociatedSymptoms);
        Assert.Equal("Tolerated strengthening without symptom flare.", result.DailyTreatment.ResponseToTreatment);
    }

    [Fact]
    public void MapToV2Payload_PersistsDischargeSpecificSubjectiveAndPlanFields()
    {
        var payload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Discharge Note",
            Subjective = new SubjectiveVm
            {
                CurrentPainScore = 2,
                BestPainScore = 0,
                WorstPainScore = 4,
                IsPainScoreDocumented = true
            },
            DischargeSubjective = new DischargeSubjectiveVm
            {
                GoalsMetStatus = "Walking and stairs goals met.",
                RemainingDifficulty = "Mild difficulty with kneeling.",
                PercentImproved = 90,
                PatientReportedOutcome = "Patient reports confidence with independent HEP."
            },
            Plan = new PlanVm
            {
                DischargeDocumentationMode = "Patient self-discharge",
                IsNonBillableDischarge = true,
                PrimaryDischargeReason = "Other",
                OtherDischargeReasonExplanation = "Relocating out of area.",
                DischargeRecommendations = "Continue HEP three times weekly.",
                PostDischargeInstructions = "Follow up with PCP for new symptoms.",
                FullDischargeSummary = "Discharged to independent self-management.",
                CompletedDischargeChecklistItems =
                [
                    "All goals reviewed and final status documented",
                    "Home Exercise Program provided to patient"
                ]
            }
        };

        var result = _mapper.MapToV2Payload(payload, NoteType.Discharge);

        Assert.Equal(NoteType.Discharge, result.NoteType);
        Assert.Equal("Walking and stairs goals met.", result.Discharge.GoalsMetStatus);
        Assert.Equal("Mild difficulty with kneeling.", result.Discharge.RemainingDifficulty);
        Assert.Equal(90, result.Discharge.PercentImproved);
        Assert.Equal("Patient reports confidence with independent HEP.", result.Discharge.PatientReportedOutcome);
        Assert.Equal("Patient self-discharge", result.Plan.DischargeDocumentationMode);
        Assert.True(result.Plan.IsNonBillableDischarge);
        Assert.Equal("Other", result.Plan.PrimaryDischargeReason);
        Assert.Equal("Relocating out of area.", result.Plan.OtherDischargeReasonExplanation);
        Assert.Equal("Continue HEP three times weekly.", result.Plan.DischargeRecommendations);
        Assert.Equal("Follow up with PCP for new symptoms.", result.Plan.PostDischargeInstructions);
        Assert.Equal("Discharged to independent self-management.", result.Plan.FullDischargeSummary);
        Assert.Equal(2, result.Plan.CompletedDischargeChecklistItems.Count);
    }

    [Fact]
    public void MapToUiPayload_RestoresDischargeSpecificSubjectiveAndPlanFields()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Discharge,
            Discharge = new WorkspaceDischargeV2
            {
                GoalsMetStatus = "Transfers goal met.",
                RemainingDifficulty = "Mild balance limitation.",
                PercentImproved = 75,
                PatientReportedOutcome = "Ready for independent maintenance."
            },
            Plan = new WorkspacePlanV2
            {
                DischargeDocumentationMode = "Patient unreachable",
                IsNonBillableDischarge = true,
                PrimaryDischargeReason = "Authorization ended",
                DischargeRecommendations = "Continue independent program.",
                PostDischargeInstructions = "Return to PT with functional regression.",
                FullDischargeSummary = "Discharged with HEP.",
                CompletedDischargeChecklistItems =
                [
                    "Discharge reason and recommendations documented"
                ]
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal("Transfers goal met.", result.DischargeSubjective.GoalsMetStatus);
        Assert.Equal("Mild balance limitation.", result.DischargeSubjective.RemainingDifficulty);
        Assert.Equal(75, result.DischargeSubjective.PercentImproved);
        Assert.Equal("Ready for independent maintenance.", result.DischargeSubjective.PatientReportedOutcome);
        Assert.Equal("Patient unreachable", result.Plan.DischargeDocumentationMode);
        Assert.True(result.Plan.IsNonBillableDischarge);
        Assert.Equal("Authorization ended", result.Plan.PrimaryDischargeReason);
        Assert.Equal("Continue independent program.", result.Plan.DischargeRecommendations);
        Assert.Equal("Return to PT with functional regression.", result.Plan.PostDischargeInstructions);
        Assert.Equal("Discharged with HEP.", result.Plan.FullDischargeSummary);
        Assert.Equal("Discharge reason and recommendations documented", Assert.Single(result.Plan.CompletedDischargeChecklistItems));
    }

    [Fact]
    public void MapToV2Payload_DryNeedlingWorkspace_PopulatesCanonicalDryNeedlingBlock()
    {
        var payload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Dry Needling Note",
            DryNeedling = new DryNeedlingVm
            {
                DateOfTreatment = new DateTime(2026, 4, 16),
                BillingDesignation = "Non-billable",
                Location = "Gluteal region",
                NeedlingType = "Deep dry needling",
                PainBefore = 7,
                PainAfter = 3,
                ResponseDescription = "Reduced pain after treatment",
                AdditionalNotes = "No adverse response"
            }
        };

        var result = _mapper.MapToV2Payload(payload, NoteType.Daily);

        Assert.NotNull(result.DryNeedling);
        Assert.Equal("Non-billable", result.DryNeedling!.BillingDesignation);
        Assert.Equal("Gluteal region", result.DryNeedling!.Location);
        Assert.Equal("Deep dry needling", result.DryNeedling.NeedlingType);
        Assert.Equal(7, result.DryNeedling.PainBefore);
        Assert.Equal(3, result.DryNeedling.PainAfter);
        Assert.Equal("Reduced pain after treatment", result.DryNeedling.ResponseDescription);
    }

    [Fact]
    public void MapToUiPayload_DryNeedlingWorkspace_PopulatesDryNeedlingVm()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            DryNeedling = new WorkspaceDryNeedlingV2
            {
                DateOfTreatment = new DateTime(2026, 4, 16),
                BillingDesignation = "Non-billable",
                Location = "Gluteal region",
                NeedlingType = "Deep dry needling",
                PainBefore = 7,
                PainAfter = 3,
                ResponseDescription = "Reduced pain after treatment",
                AdditionalNotes = "No adverse response"
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal("Dry Needling Note", result.WorkspaceNoteType);
        Assert.Equal("Non-billable", result.DryNeedling.BillingDesignation);
        Assert.Equal("Gluteal region", result.DryNeedling.Location);
        Assert.Equal("Deep dry needling", result.DryNeedling.NeedlingType);
        Assert.Equal(7, result.DryNeedling.PainBefore);
        Assert.Equal(3, result.DryNeedling.PainAfter);
        Assert.Equal("Reduced pain after treatment", result.DryNeedling.ResponseDescription);
        Assert.Equal("No adverse response", result.DryNeedling.AdditionalNotes);
    }

    [Fact]
    public void MapToUiPayload_DailyTreatmentWorkspace_PopulatesDailyTreatmentVm()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            DailyTreatment = new WorkspaceDailyTreatmentV2
            {
                ChangesSinceLastVisit = "Patient reports better transfers.",
                PainLevelChanges = "Pain improved by two points.",
                SubjectiveUpdate = "No adverse soreness after prior visit.",
                HepAdherence = "Excellent (>90%)",
                HepUpdateNotes = "Progressed bridge dosage.",
                FunctionalImprovements = "Improved sit to stand.",
                NewOrChangedSymptoms = "No new symptoms.",
                BarriersToProgress = "Work schedule limited HEP time.",
                PreviousTreatment = "Prior visit included gait training.",
                AssociatedSymptoms = ["Stiffness"],
                ResponseToTreatment = "Responded well to manual therapy."
            }
        };

        var result = _mapper.MapToUiPayload(payload);

        Assert.Equal("Patient reports better transfers.", result.DailyTreatment.ChangesSinceLastVisit);
        Assert.Equal("Pain improved by two points.", result.DailyTreatment.PainLevelChanges);
        Assert.Equal("No adverse soreness after prior visit.", result.DailyTreatment.SubjectiveUpdate);
        Assert.Equal("Excellent (>90%)", result.DailyTreatment.HepAdherence);
        Assert.Equal("Progressed bridge dosage.", result.DailyTreatment.HepUpdateNotes);
        Assert.Equal("Improved sit to stand.", result.DailyTreatment.FunctionalImprovements);
        Assert.Equal("No new symptoms.", result.DailyTreatment.NewOrChangedSymptoms);
        Assert.Equal("Work schedule limited HEP time.", result.DailyTreatment.BarriersToProgress);
        Assert.Equal("Prior visit included gait training.", result.DailyTreatment.PreviousTreatment);
        Assert.Contains("Stiffness", result.DailyTreatment.AssociatedSymptoms);
        Assert.Equal("Responded well to manual therapy.", result.DailyTreatment.ResponseToTreatment);
    }

    [Fact]
    public void ProgressQuestionnaire_RoundTripsThroughWorkspaceMapper()
    {
        var payload = new NoteWorkspacePayload
        {
            WorkspaceNoteType = "Progress Note",
            Subjective = new SubjectiveVm
            {
                CurrentPainScore = 4,
                BestPainScore = 2,
                WorstPainScore = 7,
                PainFrequency = "Intermittent",
                IsPainScoreDocumented = true
            },
            ProgressSubjective = new ProgressSubjectiveVm
            {
                OverallCondition = "Improved slightly",
                GoalProgress = "Somewhat",
                PainChange = "Decreased",
                DailyActivityEase = "More ease",
                ImprovedActivities = ["Walking"],
                SameActivities = ["Stairs"],
                WorseActivities = ["Lifting"],
                NewDifficultyActivities = ["Household chores"],
                ImpactedAreas = ["Work"],
                ReturnedToActivities = "Yes - Partially",
                HepAdherence = "A few times per week",
                HepResponse = "Helpful",
                HasSetbacksOrNewSymptoms = true,
                SetbackDetails = "Brief symptom flare after travel.",
                HasMedicalChanges = false,
                AdditionalInformation = "Patient wants to review lifting mechanics."
            }
        };

        var v2 = _mapper.MapToV2Payload(payload, NoteType.ProgressNote);
        var result = _mapper.MapToUiPayload(v2);

        Assert.Equal("Improved slightly", v2.ProgressQuestionnaire.OverallCondition);
        Assert.Contains("Walking", v2.ProgressQuestionnaire.ImprovedActivities);
        Assert.Contains("Stairs", v2.ProgressQuestionnaire.SameActivities);
        Assert.Contains("Lifting", v2.ProgressQuestionnaire.WorseActivities);
        Assert.Contains("Household chores", v2.ProgressQuestionnaire.NewDifficultyActivities);
        Assert.Equal("Patient wants to review lifting mechanics.", v2.ProgressQuestionnaire.AdditionalInformation);
        Assert.Equal(4, result.Subjective.CurrentPainScore);
        Assert.Equal("Improved slightly", result.ProgressSubjective.OverallCondition);
        Assert.Contains("Work", result.ProgressSubjective.ImpactedAreas);
        Assert.Equal(false, result.ProgressSubjective.HasMedicalChanges);
    }
}
