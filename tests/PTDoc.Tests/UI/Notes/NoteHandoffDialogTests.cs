using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Workspace;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteHandoffDialogTests : TestContext
{
    private readonly Mock<INoteHandoffService> service = new();
    private readonly Guid noteId = Guid.NewGuid(), recipient = Guid.NewGuid();
    public NoteHandoffDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(service.Object);
        service.Setup(s => s.GetAsync(noteId, It.IsAny<CancellationToken>())).ReturnsAsync(new NoteHandoffResult<NoteHandoffContext>(
            NoteHandoffStatus.Succeeded, new(noteId, DateTime.UtcNow, 0, null, [new(recipient, "Synthetic PTA", Roles.PTA)])));
    }
    private IRenderedComponent<NoteHandoffDialog> Open()
    {
        var cut = RenderComponent<NoteHandoffDialog>(p => p.Add(c => c.NoteId, noteId));
        cut.Find("button").Click(); cut.WaitForAssertion(() => Assert.Contains("Responsible clinician:", cut.Markup));
        cut.Find("select[id$='-recipient']").Change(recipient.ToString()); return cut;
    }
    [Fact]
    public void LostResponse_RetryKeepsIdentifierAndPayload_EvenAfterClosing()
    {
        var commands = new List<SendNoteHandoffRequest>();
        service.Setup(s => s.SendAsync(noteId, It.IsAny<SendNoteHandoffRequest>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, SendNoteHandoffRequest command, CancellationToken _) =>
            {
                commands.Add(command);
                return commands.Count == 1 ? Task.FromException<NoteHandoffResult<NoteHandoffReceipt>>(new HttpRequestException())
                    : Task.FromResult(new NoteHandoffResult<NoteHandoffReceipt>(NoteHandoffStatus.Succeeded,
                        new(command.RequestId, 1, recipient, command.Kind)));
            });
        var cut = Open(); cut.Find(".btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("Send result unknown", cut.Markup));
        Assert.All(cut.FindAll("select"), s => Assert.True(s.HasAttribute("disabled")));
        cut.FindAll("button").Single(b => b.TextContent == "Close").Click();
        cut.Find("button").Click(); cut.Find(".btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("Note sent.", cut.Markup));
        Assert.Equal(2, commands.Count); Assert.Equal(commands[0], commands[1]);
        service.Verify(s => s.GetAsync(noteId, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public void StaleResult_RequiresReloadAndFreshReview()
    {
        service.Setup(s => s.SendAsync(noteId, It.IsAny<SendNoteHandoffRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoteHandoffResult<NoteHandoffReceipt>(NoteHandoffStatus.Conflict));
        var cut = Open(); cut.Find(".btn--primary").Click();
        cut.WaitForAssertion(() => Assert.Contains("The note or handoff changed", cut.Markup));
        Assert.Empty(cut.FindAll(".btn--primary"));
        cut.FindAll("button").Single(b => b.TextContent == "Reload handoff").Click();
        Assert.True(cut.Find(".btn--primary").HasAttribute("disabled"));
    }
    [Fact]
    public async Task EscapeCallback_RendersClosedDialog()
    {
        var cut = Open();
        await cut.InvokeAsync(() => cut.Instance.CloseFromJs());
        Assert.Empty(cut.FindAll("[role='dialog']"));
        service.Verify(s => s.SendAsync(It.IsAny<Guid>(), It.IsAny<SendNoteHandoffRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void SaveFailure_DoesNotOpenOrSend()
    {
        var cut = RenderComponent<NoteHandoffDialog>(p => p.Add(c => c.NoteId, noteId).Add(c => c.BeforeOpen, () => Task.FromResult(false)));
        cut.Find("button").Click(); Assert.Empty(cut.FindAll("[role='dialog']"));
        service.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Verify(s => s.SendAsync(It.IsAny<Guid>(), It.IsAny<SendNoteHandoffRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
