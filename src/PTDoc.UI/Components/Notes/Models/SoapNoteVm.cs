using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// Top-level view model for the SOAP note workspace.
/// Holds all cross-section state: mode, active section, save state,
/// and per-section view models.
/// </summary>
public class SoapNoteVm
{
    public Guid? NoteId { get; set; }
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientDob { get; set; }

    public string NoteType { get; set; } = "Evaluation Note";
    public NoteMode Mode { get; set; } = NoteMode.New;
    public SoapSection ActiveSection { get; set; } = SoapSection.Subjective;
    public NoteSaveState SaveState { get; set; } = NoteSaveState.Unsaved;
    public NoteStatus Status { get; set; } = NoteStatus.Draft;
    public int? LocalDraftId { get; set; }
    public NoteWorkspaceV2Payload? StructuredWorkspacePayload { get; set; }

    /// <summary>True when any section contains unsaved edits.</summary>
    public bool IsDirty { get; set; }

    /// <summary>True once the note has been finalized (submitted). Blocks further edits.</summary>
    public bool IsSubmitted => Status != NoteStatus.Draft;
    public bool IsEditable => Status == NoteStatus.Draft;

    public SubjectiveVm Subjective { get; set; } = new();
    public ObjectiveVm Objective { get; set; } = new();
    public AssessmentWorkspaceVm Assessment { get; set; } = new();
    public PlanVm Plan { get; set; } = new();

    public void MoveTo(SoapSection section)
    {
        ActiveSection = section;
    }

    public bool MoveNextSection()
    {
        var sections = Enum.GetValues<SoapSection>();
        var index = Array.IndexOf(sections, ActiveSection);
        if (index < 0 || index >= sections.Length - 1)
        {
            return false;
        }

        ActiveSection = sections[index + 1];
        return true;
    }

    public bool MovePreviousSection()
    {
        var sections = Enum.GetValues<SoapSection>();
        var index = Array.IndexOf(sections, ActiveSection);
        if (index <= 0)
        {
            return false;
        }

        ActiveSection = sections[index - 1];
        return true;
    }

    public void MarkDirty()
    {
        if (!IsEditable)
        {
            return;
        }

        IsDirty = true;
        SaveState = NoteSaveState.Unsaved;
    }

    public void StartSaving()
    {
        SaveState = NoteSaveState.Saving;
    }

    public void MarkSaved()
    {
        SaveState = NoteSaveState.Saved;
        IsDirty = false;
    }

    public void MarkSaveError()
    {
        SaveState = NoteSaveState.Error;
    }

    public bool CanSubmit(out string? message)
    {
        if (!IsEditable)
        {
            message = "Only draft notes can be submitted from the workspace.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Plan.TreatmentFrequency))
        {
            message = "Treatment Frequency is required before submit.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Plan.TreatmentDuration))
        {
            message = "Treatment Duration is required before submit.";
            return false;
        }

        if (Assessment.DiagnosisCodes.Count > 4)
        {
            message = "Maximum of 4 ICD-10 codes allowed.";
            return false;
        }

        message = null;
        return true;
    }
}
