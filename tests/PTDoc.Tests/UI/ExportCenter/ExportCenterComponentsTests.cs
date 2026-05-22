using Bunit;
using Microsoft.AspNetCore.Authorization;
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
    public void ExportCenterRoute_RequiresNoteExportPolicy()
    {
        var authorizeAttribute = typeof(global::PTDoc.UI.Pages.ExportCenter)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthorizationPolicies.NoteExport, authorizeAttribute.Policy);
    }

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
    public void FiltersPanel_ReportsIncludesProgressSummary()
    {
        var cut = RenderComponent<FiltersPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.Reports)
            .Add(component => component.State, new ExportDraftState())
            .Add(component => component.Patients, Array.Empty<ExportCenterSelectableItem>())
            .Add(component => component.Providers, Array.Empty<ExportCenterSelectableItem>()));

        Assert.Contains("Progress Summary", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FiltersPanel_DateRangeChange_NotifiesParentStateChanged()
    {
        var state = new ExportDraftState();
        var changeCount = 0;

        var cut = RenderComponent<FiltersPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.State, state)
            .Add(component => component.Patients, Array.Empty<ExportCenterSelectableItem>())
            .Add(component => component.Providers, Array.Empty<ExportCenterSelectableItem>())
            .Add(component => component.StateChanged, () => changeCount++));

        cut.FindAll("input[type='date']")[0].Input("2026-05-02");

        Assert.Equal(new DateTime(2026, 5, 2), state.DateRangeStart);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void ExportFormatSelector_DisablesUnsupportedFormats()
    {
        var cut = RenderComponent<ExportFormatSelector>(parameters => parameters
            .Add(component => component.SelectedFormat, ExportFormat.PDF));

        var radios = cut.FindAll("input[type='radio']");
        Assert.Equal(4, radios.Count);
        Assert.False(radios[0].HasAttribute("disabled"));
        Assert.All(radios.Skip(1), radio =>
        {
            Assert.True(radio.HasAttribute("disabled"));
            Assert.False(string.IsNullOrWhiteSpace(radio.GetAttribute("aria-describedby")));
        });
        Assert.Contains("Only SOAP Notes PDF export is currently available.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportOptionsPanel_DisablesIgnoredPasswordProtection()
    {
        var state = new ExportDraftState
        {
            IsPasswordProtected = true,
            PasswordValue = "secret-value"
        };

        var cut = RenderComponent<ExportOptionsPanel>(parameters => parameters
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, state));

        var passwordToggle = cut.Find("input[type='checkbox']");
        Assert.True(passwordToggle.HasAttribute("disabled"));
        Assert.False(state.IsPasswordProtected);
        Assert.Equal(string.Empty, state.PasswordValue);
        Assert.Contains("Password protection is not available", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DateRangePicker_InvalidRange_MarksInputsInvalid()
    {
        var cut = RenderComponent<DateRangePicker>(parameters => parameters
            .Add(component => component.StartDate, new DateTime(2026, 5, 2))
            .Add(component => component.EndDate, new DateTime(2026, 5, 1)));

        var inputs = cut.FindAll("input[type='date']");
        Assert.All(inputs, input =>
        {
            Assert.NotEqual("False", input.GetAttribute("aria-invalid"));
            Assert.False(string.IsNullOrWhiteSpace(input.GetAttribute("aria-describedby")));
        });
    }

    [Fact]
    public void ExportPreviewPanel_ProgressSummaryDoesNotFallbackToSoapPreview()
    {
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var state = new ExportDraftState
        {
            SelectedTab = ExportTab.Reports,
            SelectedReportTypes = ["progress-summary"]
        };

        var cut = RenderComponent<ExportPreviewPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.Reports)
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, state));

        Assert.Contains("Progress Summary export is selected", cut.Markup, StringComparison.OrdinalIgnoreCase);
        var unavailableMessage = cut.Find(".record-count-disabled__text");
        var unavailableMessageId = unavailableMessage.GetAttribute("id");
        Assert.False(string.IsNullOrWhiteSpace(unavailableMessageId));
        var actionButtons = cut.FindAll(".export-preview-panel__actions button");
        Assert.All(actionButtons, button =>
        {
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal(unavailableMessageId, button.GetAttribute("aria-describedby"));
        });
        noteService.Verify(
            service => service.ResolveExportPreviewTargetAsync(It.IsAny<ExportPreviewTargetRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ExportPreviewPanel_InvalidDateRange_DoesNotResolvePreviewTarget()
    {
        var noteService = new Mock<INoteService>(MockBehavior.Strict);
        var noteWorkspaceService = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var state = new ExportDraftState
        {
            DateRangeStart = new DateTime(2026, 5, 2),
            DateRangeEnd = new DateTime(2026, 5, 1)
        };

        var cut = RenderComponent<ExportPreviewPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, state));

        Assert.Contains("Start date must be before or equal to end date", cut.Markup, StringComparison.OrdinalIgnoreCase);
        var unavailableMessageId = cut.Find(".record-count-disabled__text").GetAttribute("id");
        Assert.False(string.IsNullOrWhiteSpace(unavailableMessageId));
        Assert.All(cut.FindAll(".export-preview-panel__actions button"), button =>
        {
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal(unavailableMessageId, button.GetAttribute("aria-describedby"));
        });
        noteService.Verify(
            service => service.ResolveExportPreviewTargetAsync(It.IsAny<ExportPreviewTargetRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ExportPreviewPanel_UnsignedTarget_DisablesPreviewAndDownload()
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
                Title = "Draft Daily Note",
                Subtitle = "Draft",
                NoteStatus = NoteStatus.Draft,
                CanDownloadPdf = false
            });

        Services.AddSingleton(noteService.Object);
        Services.AddSingleton(noteWorkspaceService.Object);

        var cut = RenderComponent<ExportPreviewPanel>(parameters => parameters
            .Add(component => component.SelectedTab, ExportTab.SoapNotes)
            .Add(component => component.SelectedFormat, ExportFormat.PDF)
            .Add(component => component.State, new ExportDraftState()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Only finalized signed notes can be previewed or downloaded", cut.Markup, StringComparison.Ordinal);
            Assert.All(cut.FindAll(".export-preview-panel__actions button"), button => Assert.True(button.HasAttribute("disabled")));
        });

        noteWorkspaceService.Verify(
            service => service.GetDocumentHierarchyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
