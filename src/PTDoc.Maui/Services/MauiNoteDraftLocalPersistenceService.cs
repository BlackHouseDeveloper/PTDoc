using System.Text.Json;
using PTDoc.Application.LocalData;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Services;

namespace PTDoc.Maui.Services;

public sealed class MauiNoteDraftLocalPersistenceService(
    ILocalRepository<LocalClinicalNoteDraft> localRepository,
    ILocalSyncOrchestrator localSyncOrchestrator) : INoteDraftLocalPersistenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public bool IsEnabled => true;

    public async Task<NoteWorkspaceSaveResult> SaveDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken = default)
    {
        var localDraft = await ResolveLocalDraftAsync(draft, cancellationToken) ?? new LocalClinicalNoteDraft();

        localDraft.ServerId = draft.NoteId ?? Guid.Empty;
        localDraft.PatientServerId = draft.PatientId;
        localDraft.NoteType = ToApiNoteType(draft.WorkspaceNoteType).ToString();
        localDraft.DateOfService = draft.DateOfService.Date;
        localDraft.ContentJson = JsonSerializer.Serialize(draft.Payload, SerializerOptions);
        localDraft.CptCodesJson = BuildCptCodesJson(draft.Payload.Plan);
        localDraft.SignatureHash = null;
        localDraft.SignedUtc = null;
        localDraft.LastModifiedUtc = DateTime.UtcNow;
        localDraft.SyncState = SyncState.Pending;

        localDraft = await localRepository.UpsertAsync(localDraft, cancellationToken);
        try
        {
            await localSyncOrchestrator.EnqueueChangeAsync(
                "ClinicalNote",
                localDraft.ServerId,
                localDraft.LocalId,
                localDraft.ServerId == Guid.Empty ? SyncOperation.Create : SyncOperation.Update,
                JsonSerializer.Serialize(new
                {
                    localDraft.ServerId,
                    patientId = localDraft.PatientServerId,
                    localDraft.NoteType,
                    localDraft.DateOfService,
                    localDraft.ContentJson,
                    localDraft.CptCodesJson,
                    localDraft.LastModifiedUtc
                }, SerializerOptions),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort queueing: the draft is already persisted locally and remains
            // marked pending so a later sync scan can recreate missing queue items.
        }

        return new NoteWorkspaceSaveResult
        {
            Success = true,
            NoteId = localDraft.ServerId,
            LocalDraftId = localDraft.LocalId,
            Status = NoteStatus.Draft
        };
    }

    private async Task<LocalClinicalNoteDraft?> ResolveLocalDraftAsync(
        NoteWorkspaceDraft draft,
        CancellationToken cancellationToken)
    {
        if (draft.LocalDraftId.HasValue)
        {
            var byLocalId = await localRepository.GetByLocalIdAsync(draft.LocalDraftId.Value, cancellationToken);
            if (byLocalId is not null)
            {
                return byLocalId;
            }
        }

        if (draft.NoteId.HasValue && draft.NoteId.Value != Guid.Empty)
        {
            return await localRepository.GetByServerIdAsync(draft.NoteId.Value, cancellationToken);
        }

        return null;
    }

    private static NoteType ToApiNoteType(string workspaceNoteType) =>
        workspaceNoteType switch
        {
            "Evaluation Note" => NoteType.Evaluation,
            "Progress Note" => NoteType.ProgressNote,
            "Discharge Note" => NoteType.Discharge,
            _ => NoteType.Daily
        };

    private static string BuildCptCodesJson(PlanVm plan)
    {
        var cptEntries = plan.SelectedCptCodes
            .Where(code => !string.IsNullOrWhiteSpace(code.Code))
            .Select(code => new
            {
                code.Code,
                code.Units,
                IsTimed = false
            })
            .ToList();

        return JsonSerializer.Serialize(cptEntries, SerializerOptions);
    }
}
