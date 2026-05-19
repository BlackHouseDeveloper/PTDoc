using Bunit;
using PTDoc.UI.Components.Notes;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteCardTests : TestContext
{
    [Fact]
    public void ReadOnlyNote_HidesEditActionAndShowsReadOnlyReason()
    {
        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, CreateNote(canEdit: false, readOnlyReason: "Read-only")));

        Assert.DoesNotContain(">Edit<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Read-only", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableNote_ShowsEditAction()
    {
        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, CreateNote(canEdit: true, readOnlyReason: null)));

        Assert.Contains(">Edit<", cut.Markup, StringComparison.Ordinal);
    }

    private static NoteListItemVm CreateNote(bool canEdit, string? readOnlyReason) => new()
    {
        Id = Guid.NewGuid().ToString(),
        PatientId = Guid.NewGuid().ToString(),
        PatientName = "Alex Patient",
        NoteType = "Progress Note",
        Status = "Draft",
        CreatedDate = new DateTime(2026, 4, 1),
        LastModified = new DateTime(2026, 4, 1),
        Summary = "Progress summary",
        CanEdit = canEdit,
        CanDownloadPdf = true,
        ReadOnlyReason = readOnlyReason
    };
}
