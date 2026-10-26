namespace PTDoc.Core.Models;

public enum NoteHandoffKind { Collaboration = 0, Reassignment = 1 }

/// <summary>Append-only workflow metadata, separate from the immutable clinical record and signatures.</summary>
public sealed class NoteHandoff
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid ClinicalNoteId { get; set; }
    public long Sequence { get; set; }
    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public NoteHandoffKind Kind { get; set; }
    public DateTime NoteVersionUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
}
