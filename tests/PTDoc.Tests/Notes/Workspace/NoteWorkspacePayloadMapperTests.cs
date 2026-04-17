using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspacePayloadMapperTests
{
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

        var result = NoteWorkspacePayloadMapper.MapToV2Payload(payload, NoteType.Daily);

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

        var noteType = NoteWorkspacePayloadMapper.ToApiNoteType(payload.WorkspaceNoteType);
        var result = NoteWorkspacePayloadMapper.MapToV2Payload(payload, noteType);

        Assert.Equal(NoteType.Daily, noteType);
        Assert.NotNull(result.DryNeedling);
        Assert.Equal("Gluteal region", result.DryNeedling!.Location);
        Assert.Equal("Deep dry needling", result.DryNeedling.NeedlingType);
        Assert.Equal(7, result.DryNeedling.PainBefore);
        Assert.Equal(3, result.DryNeedling.PainAfter);
        Assert.Equal("Reduced pain after treatment", result.DryNeedling.ResponseDescription);
    }
}
