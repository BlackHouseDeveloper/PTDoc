using PTDoc.Application.DTOs;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Services;

public interface INoteWorkspaceService
{
    Task<NoteWorkspaceLoadResult> LoadAsync(Guid patientId, Guid noteId, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceSaveResult> SaveDraftAsync(NoteWorkspaceDraft draft, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceSubmitResult> SubmitAsync(Guid noteId, bool consentAccepted, bool intentConfirmed, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceAiAcceptanceResult> AcceptAiSuggestionAsync(
        Guid noteId,
        string section,
        string generatedText,
        string generationType,
        CancellationToken cancellationToken = default);
    Task<NoteWorkspacePdfExportResult> ExportPdfAsync(Guid noteId, CancellationToken cancellationToken = default);
}

public sealed class NoteWorkspaceDraft
{
    public Guid? NoteId { get; init; }
    public Guid PatientId { get; init; }
    public string WorkspaceNoteType { get; init; } = "Evaluation Note";
    public DateTime DateOfService { get; init; }
    public bool IsExistingNote { get; init; }
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceLoadResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid NoteId { get; init; }
    public string WorkspaceNoteType { get; init; } = "Evaluation Note";
    public DateTime DateOfService { get; init; }
    public bool IsSubmitted { get; init; }
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceSaveResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid NoteId { get; init; }
    public bool IsSubmitted { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool RequiresOverride { get; init; }
}

public sealed class NoteWorkspaceSubmitResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresCoSign { get; init; }
}

public sealed class NoteWorkspaceAiAcceptanceResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class NoteWorkspacePdfExportResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string FileName { get; init; } = "note.pdf";
    public string ContentType { get; init; } = "application/pdf";
    public byte[] Content { get; init; } = [];
}
