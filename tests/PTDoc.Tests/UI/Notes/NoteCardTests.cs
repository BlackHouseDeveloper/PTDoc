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

        Assert.DoesNotContain(">Continue Draft<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Read-only", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableNote_ShowsContinueDraftAction()
    {
        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, CreateNote(canEdit: true, readOnlyReason: null)));

        Assert.Contains(">Continue Draft<", cut.Markup, StringComparison.Ordinal);
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
        Assert.Equal(2, buttons.Count);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("Continue Draft Progress Note", StringComparison.Ordinal) == true);
        Assert.Contains(buttons, button => button.GetAttribute("aria-label")?.StartsWith("Open PDF tools", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void PendingCoSignNote_ShowsFinalizeAction()
    {
        var note = CreateNote(canEdit: false, readOnlyReason: "Co-sign pending");
        note.Status = "Pending Co-Sign";
        note.IsPendingCoSign = true;
        note.CanResolveAttention = true;

        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, note));

        Assert.Contains(">Finalize<", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Co-sign pending", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingCoSignNote_WithoutResolvePermission_ShowsViewAction()
    {
        var note = CreateNote(canEdit: false, readOnlyReason: "Co-sign pending");
        note.Status = "Pending Co-Sign";
        note.IsPendingCoSign = true;
        note.CanResolveAttention = false;

        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, note));

        Assert.Contains(">View<", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">Finalize<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingCoSignNote_WithoutResolvePermission_InvokesViewAction()
    {
        var viewed = false;
        var edited = false;
        var note = CreateNote(canEdit: false, readOnlyReason: "Co-sign pending");
        note.Status = "Pending Co-Sign";
        note.IsPendingCoSign = true;
        note.CanResolveAttention = false;

        var cut = RenderComponent<NoteCard>(parameters => parameters
            .Add(component => component.Note, note)
            .Add(component => component.OnView, _ => viewed = true)
            .Add(component => component.OnEdit, _ => edited = true));

        cut.Find("button").Click();

        Assert.True(viewed);
        Assert.False(edited);
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

    [Fact]
    public void NeedsAttentionBanner_ReadOnlyPendingCoSignShowsViewPrimaryAction()
    {
        var cut = RenderComponent<NotesNeedsAttentionBanner>(parameters => parameters
            .Add(component => component.Items, new[]
            {
                CreatePendingCoSignAttentionItem(canResolveAttention: false)
            }));

        Assert.Contains(">View<", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">Finalize<", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void NeedsAttentionBanner_ResolvablePendingCoSignShowsFinalizePrimaryAction()
    {
        var cut = RenderComponent<NotesNeedsAttentionBanner>(parameters => parameters
            .Add(component => component.Items, new[]
            {
                CreatePendingCoSignAttentionItem(canResolveAttention: true)
            }));

        Assert.Contains(">Finalize<", cut.Markup, StringComparison.Ordinal);
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

    private static NoteListItemVm CreatePendingCoSignAttentionItem(bool canResolveAttention) => new()
    {
        Id = Guid.NewGuid().ToString(),
        PatientId = Guid.NewGuid().ToString(),
        PatientName = "Alex Patient",
        NoteType = "Progress Note",
        Status = "Pending Co-Sign",
        CreatedDate = new DateTime(2026, 4, 1),
        LastModified = new DateTime(2026, 4, 1),
        IsPendingCoSign = true,
        CanResolveAttention = canResolveAttention,
        AttentionReason = "Awaiting PT co-signature before the note can be fully finalized."
    };
}
