using PTDoc.Core.Models;

namespace PTDoc.Application.Services;

public sealed record NoteHandoffClinician(Guid Id, string Name, string Role);
public sealed record NoteHandoffContext(Guid NoteId, DateTime NoteVersionUtc, long Sequence,
    NoteHandoffClinician? ResponsibleClinician, IReadOnlyList<NoteHandoffClinician> Recipients);
public sealed record SendNoteHandoffRequest(Guid RequestId, Guid RecipientUserId, NoteHandoffKind Kind,
    DateTime ExpectedNoteVersionUtc, long ExpectedSequence);
public sealed record NoteHandoffReceipt(Guid RequestId, long Sequence, Guid RecipientUserId, NoteHandoffKind Kind);
public enum NoteHandoffStatus { Succeeded, Forbidden, NotFound, Conflict, Invalid }
public sealed record NoteHandoffResult<T>(NoteHandoffStatus Status, T? Value = default);

/// <summary>Handoff never grants edit/sign permission and never changes the clinical note.</summary>
public interface INoteHandoffService
{
    Task<NoteHandoffResult<NoteHandoffContext>> GetAsync(Guid noteId, CancellationToken cancellationToken = default);
    Task<NoteHandoffResult<NoteHandoffReceipt>> SendAsync(Guid noteId, SendNoteHandoffRequest request,
        CancellationToken cancellationToken = default);
}
