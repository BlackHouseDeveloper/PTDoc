using Bunit;
using AngleSharp.Dom;
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

    [Fact]
    public void NoteCard_RendersSemanticArticleWithExplicitActionButtons()
    {
        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, CreateNote(canEdit: true, readOnlyReason: null)));

        var article = cut.Find("article");
        Assert.Null(article.GetAttribute("role"));
        Assert.Null(article.GetAttribute("tabindex"));

        var buttons = cut.FindAll("button");
        Assert.Equal(4, buttons.Count);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("Open Progress Note", StringComparison.Ordinal) == true);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("View Progress Note", StringComparison.Ordinal) == true);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("Edit Progress Note", StringComparison.Ordinal) == true);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("Open PDF tools", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void NeedsAttentionBanner_RendersReasonWithoutDecorativeWarningGlyph()
    {
        var cut = RenderComponent<NotesNeedsAttentionBanner>(parameters => parameters
            .Add(component => component.Items, new[]
            {
                new NoteListItemVm
                {
                    Id = Guid.NewGuid().ToString(),
                    PatientId = Guid.NewGuid().ToString(),
                    PatientName = "Alex Patient",
                    NoteType = "Progress Note",
                    Status = "Pending Co-Sign",
                    CreatedDate = new DateTime(2026, 4, 1),
                    LastModified = new DateTime(2026, 4, 1),
                    IsPendingCoSign = true,
                    AttentionReason = "Awaiting PT co-signature before the note can be fully finalized."
                }
            }));

        Assert.Contains("Awaiting PT co-signature", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("⚠", cut.Markup, StringComparison.Ordinal);
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
