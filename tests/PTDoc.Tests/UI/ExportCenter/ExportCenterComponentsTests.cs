using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.ExportCenter;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.ExportCenter;

[Trait("Category", "CoreCi")]
public sealed class ExportCenterComponentsTests : TestContext
{
    [Fact]
    public void RecentActivityPanel_RendersTimeFromTimestampUtc()
    {
        var timestampUtc = DateTime.UtcNow.AddMinutes(-5);

        var cut = RenderComponent<RecentActivityPanel>(parameters => parameters
            .Add(component => component.ActivityItems, new[]
            {
                new ExportCenterActivityItem
                {
                    Id = "note-1",
                    Title = "Progress Note for Test Patient",
                    Meta = "Signed · Apr 1, 2026",
                    TimestampUtc = timestampUtc,
                    Kind = ExportCenterActivityKind.Note
                }
            }));

        Assert.Contains("5m ago", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiltersPanel_HidesUnsupportedSoapOptions()
    {
        var cut = RenderComponent<FiltersPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.State, new ExportDraftState())
            .Add(component => component.Patients, Array.Empty<ExportCenterSelectableItem>())
            .Add(component => component.Providers, Array.Empty<ExportCenterSelectableItem>())
            .Add(component => component.ShowSoapProviderFilter, false));

        Assert.DoesNotContain("Dry Needling", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Providers", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportPreviewPanel_ShowsInlineError_WhenPreviewGenerationFails()
    {
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();

        noteService
            .Setup(service => service.ResolveExportPreviewTargetAsync(
                It.IsAny<ExportPreviewTargetRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExportPreviewTargetResponse
            {
                NoteId = noteId,
                Title = "Signed Progress Note",
                Subtitle = "Signed",
                NoteStatus = NoteStatus.Signed,
                CanDownloadPdf = true
            });

        noteWorkspaceService
            .Setup(service => service.GetDocumentHierarchyAsync(noteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("preview exploded"));

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<ExportPreviewPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, new ExportDraftState()));

        cut.WaitForAssertion(() => Assert.Contains("Signed Progress Note", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains("preview exploded", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExportPreviewPanel_ShowsInlineError_WhenDownloadFails()
    {
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        var noteId = Guid.NewGuid();

        noteService
            .Setup(service => service.ResolveExportPreviewTargetAsync(
                It.IsAny<ExportPreviewTargetRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExportPreviewTargetResponse
            {
                NoteId = noteId,
                Title = "Signed Progress Note",
                Subtitle = "Signed",
                NoteStatus = NoteStatus.Signed,
                CanDownloadPdf = true
            });

        noteWorkspaceService
            .Setup(service => service.ExportPdfAsync(noteId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("download exploded"));

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<ExportPreviewPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, new ExportDraftState()));

        cut.WaitForAssertion(() => Assert.Contains("Signed Progress Note", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button")[1].Click();

        cut.WaitForAssertion(() => Assert.Contains("download exploded", cut.Markup, StringComparison.OrdinalIgnoreCase));
    }
}
