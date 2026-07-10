using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Components.Notes.Completion;

public static class NoteCompletionEvaluator
{
    public static NoteCompletionState Evaluate(
        SoapNoteVm note,
        DryNeedlingVm dryNeedling,
        IReadOnlyList<SoapSection> sections,
        bool isEditable)
    {
        if (!isEditable)
        {
            return NoteCompletionState.Complete(sections);
        }

        var missing = new List<MissingRequiredItem>();

        if (IsDryNeedling(note.NoteType))
        {
            AddDryNeedlingRequirements(missing, dryNeedling);
            return new NoteCompletionState(
                missing,
                NoteCompletionState.BuildSectionStates(sections, missing));
        }

        if (IsEvaluation(note.NoteType))
        {
            if (string.IsNullOrWhiteSpace(note.Plan.TreatmentFrequency))
            {
                missing.Add(new MissingRequiredItem(
                    "plan-treatment-frequency",
                    SoapSection.Plan,
                    "Treatment frequency",
                    "Select the planned visit frequency before leaving this incomplete note.",
                    "[data-note-field-key='plan-treatment-frequency']"));
            }

            if (string.IsNullOrWhiteSpace(note.Plan.TreatmentDuration))
            {
                missing.Add(new MissingRequiredItem(
                    "plan-treatment-duration",
                    SoapSection.Plan,
                    "Treatment duration",
                    "Select the planned treatment duration before leaving this incomplete note.",
                    "[data-note-field-key='plan-treatment-duration']"));
            }
        }

        if (note.Assessment.DiagnosisCodes.Count > 4)
        {
            missing.Add(new MissingRequiredItem(
                "assessment-diagnosis-code-limit",
                SoapSection.Assessment,
                "ICD-10 diagnosis code limit",
                "Reduce diagnosis codes to four or fewer before submit.",
                "[data-testid='icd10-card']"));
        }

        if (IsDischarge(note.NoteType))
        {
            if (string.IsNullOrWhiteSpace(note.Plan.PrimaryDischargeReason))
            {
                missing.Add(new MissingRequiredItem(
                    "discharge-primary-reason",
                    SoapSection.Plan,
                    "Primary discharge reason",
                    "Select the primary discharge reason before leaving this incomplete note.",
                    "[data-note-field-key='discharge-primary-reason']"));
            }

            if (string.IsNullOrWhiteSpace(note.Plan.DischargeRecommendations))
            {
                missing.Add(new MissingRequiredItem(
                    "discharge-recommendations",
                    SoapSection.Plan,
                    "Discharge recommendations",
                    "Document discharge recommendations before leaving this incomplete note.",
                    "[data-note-field-key='discharge-recommendations']"));
            }

            if (note.Plan.CompletedDischargeChecklistItems.Count == 0)
            {
                missing.Add(new MissingRequiredItem(
                    "discharge-end-of-care-checklist",
                    SoapSection.Plan,
                    "End-of-care checklist",
                    "Complete at least one end-of-care checklist item before submit.",
                    "[data-testid='end-of-care-checklist-card']"));
            }
        }

        return new NoteCompletionState(
            missing,
            NoteCompletionState.BuildSectionStates(sections, missing));
    }

    private static void AddDryNeedlingRequirements(List<MissingRequiredItem> missing, DryNeedlingVm dryNeedling)
    {
        if (dryNeedling.DateOfTreatment is null)
        {
            missing.Add(new MissingRequiredItem(
                "dry-date",
                SoapSection.Subjective,
                "Date of treatment",
                "Enter the dry needling treatment date before leaving this incomplete note.",
                "[data-note-field-key='dry-date']"));
        }

        if (string.IsNullOrWhiteSpace(dryNeedling.Location))
        {
            missing.Add(new MissingRequiredItem(
                "dry-location",
                SoapSection.Subjective,
                "Location of needling",
                "Select the dry needling treatment location before leaving this incomplete note.",
                "[data-note-field-key='dry-location']"));
        }

        if (string.IsNullOrWhiteSpace(dryNeedling.NeedlingType))
        {
            missing.Add(new MissingRequiredItem(
                "dry-type",
                SoapSection.Subjective,
                "Type of needling",
                "Select the dry needling type before leaving this incomplete note.",
                "[data-note-field-key='dry-type']"));
        }
    }

    private static bool IsEvaluation(string noteType) =>
        string.Equals(noteType, "Evaluation Note", StringComparison.OrdinalIgnoreCase);

    private static bool IsDischarge(string noteType) =>
        string.Equals(noteType, "Discharge Note", StringComparison.OrdinalIgnoreCase);

    private static bool IsDryNeedling(string noteType) =>
        string.Equals(noteType, "Dry Needling Note", StringComparison.OrdinalIgnoreCase);
}
