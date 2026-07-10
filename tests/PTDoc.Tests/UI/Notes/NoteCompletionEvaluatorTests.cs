using PTDoc.UI.Components.Notes.Completion;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteCompletionEvaluatorTests
{
    private static readonly IReadOnlyList<SoapSection> Sections =
    [
        SoapSection.Subjective,
        SoapSection.Objective,
        SoapSection.Assessment,
        SoapSection.Plan,
        SoapSection.Review
    ];

    [Fact]
    public void EvaluationNote_MissingPlanOfCareFields_IsIncomplete()
    {
        var note = new SoapNoteVm
        {
            NoteType = "Evaluation Note"
        };

        var state = NoteCompletionEvaluator.Evaluate(note, new DryNeedlingVm(), Sections, isEditable: true);

        Assert.False(state.IsComplete);
        Assert.Equal(2, state.MissingCount);
        Assert.Contains(state.MissingItems, item => item.Key == "plan-treatment-frequency");
        Assert.Contains(state.MissingItems, item => item.Key == "plan-treatment-duration");
        Assert.Equal(2, state.SectionStates[SoapSection.Plan].MissingCount);
    }

    [Fact]
    public void EvaluationNote_WithPlanOfCareFields_IsComplete()
    {
        var note = new SoapNoteVm
        {
            NoteType = "Evaluation Note",
            Plan = new PlanVm
            {
                TreatmentFrequency = "2x/week",
                TreatmentDuration = "6 weeks"
            }
        };

        var state = NoteCompletionEvaluator.Evaluate(note, new DryNeedlingVm(), Sections, isEditable: true);

        Assert.True(state.IsComplete);
    }

    [Fact]
    public void DischargeNote_MissingDischargeFields_IsIncomplete()
    {
        var note = new SoapNoteVm
        {
            NoteType = "Discharge Note"
        };

        var state = NoteCompletionEvaluator.Evaluate(note, new DryNeedlingVm(), Sections, isEditable: true);

        Assert.False(state.IsComplete);
        Assert.Contains(state.MissingItems, item => item.Key == "discharge-primary-reason");
        Assert.Contains(state.MissingItems, item => item.Key == "discharge-recommendations");
        Assert.Contains(state.MissingItems, item => item.Key == "discharge-end-of-care-checklist");
    }

    [Fact]
    public void DryNeedlingNote_MissingTreatmentDetails_IsIncomplete()
    {
        var note = new SoapNoteVm
        {
            NoteType = "Dry Needling Note"
        };

        var state = NoteCompletionEvaluator.Evaluate(note, new DryNeedlingVm(), Sections, isEditable: true);

        Assert.False(state.IsComplete);
        Assert.Contains(state.MissingItems, item => item.Key == "dry-date");
        Assert.Contains(state.MissingItems, item => item.Key == "dry-location");
        Assert.Contains(state.MissingItems, item => item.Key == "dry-type");
    }
}
