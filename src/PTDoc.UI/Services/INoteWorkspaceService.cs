using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Pdf;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Services;

public interface INoteWorkspaceService
{
    Task<NoteWorkspaceLoadResult> LoadAsync(Guid patientId, Guid noteId, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceEvaluationSeedResult> GetEvaluationSeedAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceCarryForwardSeedResult> GetCarryForwardSeedAsync(Guid patientId, string workspaceNoteType, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceSaveResult> SaveDraftAsync(NoteWorkspaceDraft draft, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceSubmitResult> SubmitAsync(Guid noteId, bool consentAccepted, bool intentConfirmed, CancellationToken cancellationToken = default);
    Task<NoteWorkspaceAiAcceptanceResult> AcceptAiSuggestionAsync(
        Guid noteId,
        string section,
        string generatedText,
        string generationType,
        CancellationToken cancellationToken = default);
    Task<NoteWorkspaceDocumentHierarchyResult> GetDocumentHierarchyAsync(Guid noteId, CancellationToken cancellationToken = default);
    Task<NoteWorkspacePdfExportResult> ExportPdfAsync(Guid noteId, CancellationToken cancellationToken = default);
    Task<BodyRegionCatalog> GetBodyRegionCatalogAsync(BodyPart bodyPart, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeLookupEntry>> SearchCptAsync(string? query, int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CodeLookupEntry>> SearchIcd10Async(string? query, int take = 20, CancellationToken cancellationToken = default);
}

public sealed class NoteWorkspaceDraft
{
    public Guid? NoteId { get; init; }
    public int? LocalDraftId { get; init; }
    public Guid PatientId { get; init; }
    public string WorkspaceNoteType { get; init; } = "Evaluation Note";
    public DateTime DateOfService { get; init; }
    public bool IsExistingNote { get; init; }
    public OverrideSubmission? Override { get; init; }
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceLoadResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid NoteId { get; init; }
    public string WorkspaceNoteType { get; init; } = "Evaluation Note";
    public DateTime DateOfService { get; init; }
    public NoteStatus Status { get; init; } = NoteStatus.Draft;
    public bool IsSubmitted => Status != NoteStatus.Draft;
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceEvaluationSeedResult
{
    public bool Success { get; init; }
    public bool HasSeed { get; init; }
    public string? ErrorMessage { get; init; }
    public bool FromLockedSubmittedIntake { get; init; }
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceCarryForwardSeedResult
{
    public bool Success { get; init; }
    public bool HasSeed { get; init; }
    public string? ErrorMessage { get; init; }
    public string SourceNoteType { get; init; } = string.Empty;
    public DateTime? SourceNoteDateOfService { get; init; }
    public NoteWorkspacePayload Payload { get; init; } = new();
}

public sealed class NoteWorkspaceSaveResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid NoteId { get; init; }
    public int? LocalDraftId { get; init; }
    public NoteStatus Status { get; init; } = NoteStatus.Draft;
    public bool IsSubmitted => Status != NoteStatus.Draft;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool RequiresOverride { get; init; }
    public ComplianceRuleType? RuleType { get; init; }
    public bool IsOverridable { get; init; }
    public IReadOnlyList<OverrideRequirement> OverrideRequirements { get; init; } = Array.Empty<OverrideRequirement>();
    public ComplianceWarning? ComplianceWarning { get; init; }
    public NoteWorkspacePayload? Payload { get; init; }
}

public sealed class NoteWorkspaceSubmitResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public bool RequiresCoSign { get; init; }
    public NoteStatus Status { get; init; } = NoteStatus.Draft;
    public bool IsSubmitted => Status != NoteStatus.Draft;
    public IReadOnlyList<RuleEvaluationResult> ValidationFailures { get; init; } = Array.Empty<RuleEvaluationResult>();
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

public sealed class NoteWorkspaceDocumentHierarchyResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public ClinicalDocumentHierarchy? Hierarchy { get; init; }
}
