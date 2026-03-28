using PTDoc.Core.Models;

namespace PTDoc.Application.LocalData.Entities;

/// <summary>
/// Lightweight cached clinical note record for offline-first MAUI access.
/// Stores SOAP drafts locally so clinicians can author notes while offline;
/// the draft is pushed to the server when reconnected.
/// Only draft (unsigned) notes are editable offline. Signed notes are pulled
/// read-only and cannot be modified locally.
/// </summary>
public class LocalClinicalNoteDraft : ILocalEntity
{
    /// <inheritdoc/>
    public int LocalId { get; set; }

    /// <inheritdoc/>
    public Guid ServerId { get; set; }

    /// <summary>Server-side ID of the associated patient.</summary>
    public Guid PatientServerId { get; set; }

    /// <summary>Type of clinical note (e.g. "Evaluation", "Daily", "ProgressNote", "Discharge").</summary>
    public string NoteType { get; set; } = string.Empty;

    /// <summary>Date of service for this note (date only, stored as UTC midnight).</summary>
    public DateTime DateOfService { get; set; }

    /// <summary>SOAP content as a JSON blob (keys: subjective, objective, assessment, plan, goals, billing).</summary>
    public string ContentJson { get; set; } = "{}";

    /// <summary>CPT codes as a JSON array. Empty array when no codes have been added.</summary>
    public string CptCodesJson { get; set; } = "[]";

    /// <summary>
    /// Non-null when the note has been signed on the server.
    /// A signed note is immutable; local edits are not allowed once this is populated.
    /// </summary>
    public string? SignatureHash { get; set; }

    /// <summary>UTC timestamp when the note was signed. Null for drafts.</summary>
    public DateTime? SignedUtc { get; set; }

    /// <inheritdoc/>
    public SyncState SyncState { get; set; }

    /// <inheritdoc/>
    public DateTime LastModifiedUtc { get; set; }

    /// <inheritdoc/>
    public DateTime? LastSyncedUtc { get; set; }
}
