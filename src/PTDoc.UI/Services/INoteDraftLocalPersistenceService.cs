namespace PTDoc.UI.Services;

public interface INoteDraftLocalPersistenceService
{
    bool IsEnabled { get; }

    Task<NoteWorkspaceSaveResult> SaveDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken = default);
}
