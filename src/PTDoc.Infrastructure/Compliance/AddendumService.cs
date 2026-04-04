using System.Text.Json;
using PTDoc.Application.Compliance;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Compliance;

public sealed class AddendumService(
    ApplicationDbContext context,
    IAuditService auditService,
    ISyncEngine syncEngine) : IAddendumService
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
        await syncEngine.EnqueueAsync("ClinicalNote", addendum.Id, SyncOperation.Create, ct);
        await auditService.LogAddendumCreatedAsync(AuditEvent.AddendumCreated(note.Id, addendum.Id, userId), ct);

        return new AddendumResult
        {
            Success = true,
            AddendumId = addendum.Id
        };
    }

    private static bool ContentIsEmpty(JsonElement content) =>
        content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ||
        (content.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(content.GetString()));

    private static string SerializeContent(JsonElement content) =>
        content.ValueKind == JsonValueKind.String
            ? JsonSerializer.Serialize(content.GetString())
            : content.GetRawText();
}
