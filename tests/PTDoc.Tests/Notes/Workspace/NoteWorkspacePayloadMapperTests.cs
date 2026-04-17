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
}
