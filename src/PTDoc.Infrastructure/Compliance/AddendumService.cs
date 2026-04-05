using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

public sealed class AddendumService(
    ApplicationDbContext context,
    IAuditService auditService) : IAddendumService
{
    public async Task<AddendumResult> CreateAddendumAsync(Guid noteId, JsonElement content, Guid userId, CancellationToken ct = default)
    {
        var note = await context.ClinicalNotes.FindAsync(new object[] { noteId }, ct);

        if (note is null)
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Note not found"
            };
        }

        if (!note.IsFinalized)
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Addendums can only be created for signed notes"
            };
        }

        if (note.IsAddendum)
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Cannot create addendum of addendum"
            };
        }

        if (ContentIsEmpty(content))
        {
            return new AddendumResult
            {
                Success = false,
                ErrorMessage = "Addendum content cannot be empty"
            };
        }

        var now = DateTime.UtcNow;
        var addendum = new ClinicalNote
        {
            PatientId = note.PatientId,
            AppointmentId = note.AppointmentId,
            ParentNoteId = note.Id,
            IsAddendum = true,
            NoteType = note.NoteType,
            IsReEvaluation = note.IsReEvaluation,
            NoteStatus = NoteStatus.Draft,
            TherapistNpi = note.TherapistNpi,
            ContentJson = SerializeContent(content),
            DateOfService = note.DateOfService,
            CptCodesJson = "[]",
            TotalTreatmentMinutes = null,
            ClinicId = note.ClinicId,
            CreatedUtc = now,
            LastModifiedUtc = now,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        context.ClinicalNotes.Add(addendum);
        await context.SaveChangesAsync(ct);

        // Enqueue directly to avoid a circular dependency between sync and signature services.
        var existingQueueItem = await context.SyncQueueItems
            .FirstOrDefaultAsync(
                q => q.EntityType == "ClinicalNote" &&
                     q.EntityId == addendum.Id &&
                     q.Status == SyncQueueStatus.Pending,
                ct);

        if (existingQueueItem is null)
        {
            context.SyncQueueItems.Add(new SyncQueueItem
            {
                EntityType = "ClinicalNote",
                EntityId = addendum.Id,
                Operation = SyncOperation.Create,
                EnqueuedAt = DateTime.UtcNow,
                Status = SyncQueueStatus.Pending
            });
        }
        else
        {
            existingQueueItem.Operation = SyncOperation.Create;
            existingQueueItem.EnqueuedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(ct);
        await auditService.LogAddendumCreatedAsync(AuditEvent.AddendumCreated(note.Id, addendum.Id, userId), ct);

        return new AddendumResult
        {
            Success = true,
            AddendumId = addendum.Id
        };
    }

    private static bool ContentIsEmpty(JsonElement content) =>
        content.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.Null => true,
            JsonValueKind.String => string.IsNullOrWhiteSpace(content.GetString()),
            JsonValueKind.Array => !content.EnumerateArray().MoveNext(),
            JsonValueKind.Object => !content.EnumerateObject().MoveNext(),
            _ => false
        };

    private static string SerializeContent(JsonElement content) =>
        content.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(content.GetString())
            : content.GetRawText();
}
