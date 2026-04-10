using System.Text.Json.Serialization;
using PTDoc.Application.Notes.Workspace;

namespace PTDoc.UI.Components.Notes.Models;

/// <summary>
/// Persisted content model for note workspace data across SOAP and Dry Needling flows.
/// </summary>
public class NoteWorkspacePayload
{
    public string WorkspaceNoteType { get; set; } = "Evaluation Note";
    [JsonIgnore] public NoteWorkspaceV2Payload? StructuredPayload { get; set; }
    public SubjectiveVm Subjective { get; set; } = new();
    public ObjectiveVm Objective { get; set; } = new();
    public AssessmentWorkspaceVm Assessment { get; set; } = new();
    public PlanVm Plan { get; set; } = new();
    public DryNeedlingVm DryNeedling { get; set; } = new();
}
