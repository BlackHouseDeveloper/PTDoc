namespace PTDoc.Models;

/// <summary>
/// Represents an immutable audit log entry capturing compliance-relevant events.
/// Audit logs must never contain PHI in the Details field.
/// </summary>
public sealed class AuditLog
{
    /// <summary>
    /// Gets or sets the unique identifier for this log entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the UTC timestamp when the event occurred.
    /// </summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the clinic (tenant) identifier associated with this event.
    /// </summary>
    public Guid ClinicId { get; set; }

    /// <summary>
    /// Gets or sets the type of event (e.g. NoteEdited, NoteSigned, NoteExported).
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entity type affected by this event (e.g. SOAPNote, Patient).
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Gets or sets the unique identifier of the affected entity.
    /// </summary>
    public Guid? EntityId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who performed the action.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets non-PHI details about the event (metadata only).
    /// </summary>
    public string? Details { get; set; }
}
