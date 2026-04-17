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
        var noteType = NoteWorkspacePayloadMapper.ToApiNoteType(draft.WorkspaceNoteType);
        var canonicalPayload = NoteWorkspacePayloadMapper.MapToV2Payload(draft.Payload, noteType);

        localDraft.ServerId = draft.NoteId ?? Guid.Empty;
        localDraft.PatientServerId = draft.PatientId;
        localDraft.NoteType = noteType.ToString();
        localDraft.IsReEvaluation = draft.IsReEvaluation;
        localDraft.DateOfService = draft.DateOfService.Date;
        localDraft.ContentJson = JsonSerializer.Serialize(canonicalPayload, SerializerOptions);
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
                    localDraft.IsReEvaluation,
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
            // marked Pending so EnsureQueueItemsForPendingEntitiesAsync can recreate the
            // missing queue item during the next sync scan.
        }

        return new NoteWorkspaceSaveResult
        {
            Success = true,
            NoteId = localDraft.ServerId,
            LocalDraftId = localDraft.LocalId,
            IsReEvaluation = localDraft.IsReEvaluation,
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
