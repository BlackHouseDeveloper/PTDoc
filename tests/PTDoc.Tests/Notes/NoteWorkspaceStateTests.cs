using PTDoc.UI.Components.Notes.Models;
using Xunit;

namespace PTDoc.Tests.Notes;

[Trait("Category", "Unit")]
public class NoteWorkspaceStateTests
{
    [Fact]
    public void MoveBetweenSections_PreservesSectionData()
    {
        var note = new SoapNoteVm();
        note.Subjective.Problems.Add("Pain");
        note.Objective.PrimaryGaitPattern = "antalgic";
        note.Assessment.AssessmentNarrative = "Initial assessment narrative";
        note.Plan.TreatmentFrequency = "2x/week";

        note.MoveTo(SoapSection.Objective);
        note.MoveTo(SoapSection.Assessment);
        note.MoveTo(SoapSection.Plan);

        Assert.Contains("Pain", note.Subjective.Problems);
        Assert.Equal("antalgic", note.Objective.PrimaryGaitPattern);
        Assert.Equal("Initial assessment narrative", note.Assessment.AssessmentNarrative);
        Assert.Equal("2x/week", note.Plan.TreatmentFrequency);
    }

    [Fact]
    public void AiSuggestionDraftData_DoesNotChangeSaveState()
    {
        var note = new SoapNoteVm { SaveState = NoteSaveState.Unsaved };

        note.Assessment.Goals.Add(new SmartGoalEntry
        {
            Description = "Improve shoulder flexion to 160 degrees",
            IsAiSuggested = true,
            IsAccepted = false
        });

        Assert.Equal(NoteSaveState.Unsaved, note.SaveState);
    }

    [Fact]
    public void TryAddDiagnosisCode_RejectsWhenCountIsAtMax()
    {
        var vm = new AssessmentWorkspaceVm();

        Assert.True(vm.TryAddDiagnosisCode(new Icd10Entry { Code = "M25.511", Description = "Pain in right shoulder" }));
        Assert.True(vm.TryAddDiagnosisCode(new Icd10Entry { Code = "M62.81", Description = "Muscle weakness" }));
        Assert.True(vm.TryAddDiagnosisCode(new Icd10Entry { Code = "R26.2", Description = "Difficulty in walking" }));
        Assert.True(vm.TryAddDiagnosisCode(new Icd10Entry { Code = "M54.2", Description = "Cervicalgia" }));

        var addedFifth = vm.TryAddDiagnosisCode(new Icd10Entry
        {
            Code = "G89.29",
            Description = "Other chronic pain"
        });

        Assert.False(addedFifth);
        Assert.Equal(4, vm.DiagnosisCodes.Count);
    }

    [Fact]
    public void SaveStateTransitions_FollowExpectedSequence()
    {
        var note = new SoapNoteVm();

        note.MarkDirty();
        Assert.True(note.IsDirty);
        Assert.Equal(NoteSaveState.Unsaved, note.SaveState);

        note.StartSaving();
        Assert.Equal(NoteSaveState.Saving, note.SaveState);

        note.MarkSaved();
        Assert.False(note.IsDirty);
        Assert.Equal(NoteSaveState.Saved, note.SaveState);

        note.MarkSaveError();
        Assert.Equal(NoteSaveState.Error, note.SaveState);
    }

    [Fact]
    public void CanSubmit_FailsWhenRequiredPlanFieldsMissing()
    {
        var note = new SoapNoteVm();

        var validWithNoFrequency = note.CanSubmit(out var messageNoFrequency);
        Assert.False(validWithNoFrequency);
        Assert.Equal("Treatment Frequency is required before submit.", messageNoFrequency);

        note.Plan.TreatmentFrequency = "2x/week";
        var validWithNoDuration = note.CanSubmit(out var messageNoDuration);
        Assert.False(validWithNoDuration);
        Assert.Equal("Treatment Duration is required before submit.", messageNoDuration);
    }

    [Fact]
    public void CanSubmit_SucceedsWhenRequiredFieldsPresentAndIcdCountValid()
    {
        var note = new SoapNoteVm();
        note.Plan.TreatmentFrequency = "2x/week";
        note.Plan.TreatmentDuration = "8 weeks";

        note.Assessment.TryAddDiagnosisCode(new Icd10Entry { Code = "M25.511", Description = "Pain in right shoulder" });
        note.Assessment.TryAddDiagnosisCode(new Icd10Entry { Code = "M62.81", Description = "Muscle weakness" });

        var canSubmit = note.CanSubmit(out var message);

        Assert.True(canSubmit);
        Assert.Null(message);
    }
}
