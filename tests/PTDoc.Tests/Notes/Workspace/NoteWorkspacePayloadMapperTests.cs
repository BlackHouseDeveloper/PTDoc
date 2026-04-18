using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspacePayloadMapperTests
{
    private readonly NoteWorkspacePayloadMapper _mapper = new(new IntakeReferenceDataCatalogService());

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
                        Notes = "Grade III"
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
        Assert.Single(result.Assessment.DiagnosisCodes);
        Assert.Equal("M25.561", result.Assessment.DiagnosisCodes[0].Code);
        Assert.Equal([2], result.Plan.TreatmentFrequencyDaysPerWeek);
        Assert.Equal([6], result.Plan.TreatmentDurationWeeks);
        Assert.Single(result.Plan.GeneralInterventions);
        Assert.Equal("Joint mobilization", result.Plan.GeneralInterventions[0].Name);
        Assert.Single(result.Plan.SelectedCptCodes);
        Assert.Equal("97110", result.Plan.SelectedCptCodes[0].Code);
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
        Assert.Equal("Gluteal region", result.DryNeedling.Location);
        Assert.Equal("Deep dry needling", result.DryNeedling.NeedlingType);
        Assert.Equal(7, result.DryNeedling.PainBefore);
        Assert.Equal(3, result.DryNeedling.PainAfter);
        Assert.Equal("Reduced pain after treatment", result.DryNeedling.ResponseDescription);
        Assert.Equal("No adverse response", result.DryNeedling.AdditionalNotes);
    }
}
