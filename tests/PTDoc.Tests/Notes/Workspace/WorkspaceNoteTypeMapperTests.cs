using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;

namespace PTDoc.Tests.Notes.Workspace;

[Trait("Category", "CoreCi")]
public sealed class WorkspaceNoteTypeMapperTests
{
    [Theory]
    [InlineData(WorkspaceNoteTypeMapper.EvaluationNote, NoteType.Evaluation)]
    [InlineData(WorkspaceNoteTypeMapper.ProgressNote, NoteType.ProgressNote)]
    [InlineData(WorkspaceNoteTypeMapper.DischargeNote, NoteType.Discharge)]
    [InlineData(WorkspaceNoteTypeMapper.DailyTreatmentNote, NoteType.Daily)]
    [InlineData(WorkspaceNoteTypeMapper.DryNeedlingNote, NoteType.Daily)]
    public void ToApiNoteType_MapsWorkspaceLabels(string workspaceNoteType, NoteType expected)
    {
        var result = WorkspaceNoteTypeMapper.ToApiNoteType(workspaceNoteType);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveWorkspaceNoteType_UsesDryNeedlingLabelWhenPayloadContainsDryNeedlingSection()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Daily,
            DryNeedling = new WorkspaceDryNeedlingV2
            {
                Location = "Gluteal region"
            }
        };

        var result = WorkspaceNoteTypeMapper.ResolveWorkspaceNoteType(payload);

        Assert.Equal(WorkspaceNoteTypeMapper.DryNeedlingNote, result);
    }

    [Fact]
    public void ToWorkspaceNoteType_MapsDailyNoteTypeToDailyTreatmentLabel()
    {
        var result = WorkspaceNoteTypeMapper.ToWorkspaceNoteType(NoteType.Daily);

        Assert.Equal(WorkspaceNoteTypeMapper.DailyTreatmentNote, result);
    }
}
