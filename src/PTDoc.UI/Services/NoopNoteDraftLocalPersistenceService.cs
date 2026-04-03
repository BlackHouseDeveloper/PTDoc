namespace PTDoc.UI.Services;

public sealed class NoopNoteDraftLocalPersistenceService : INoteDraftLocalPersistenceService
{
    public bool IsEnabled => false;

    public Task<NoteWorkspaceSaveResult> SaveDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Local note persistence is not available on this platform.");
    }
}
