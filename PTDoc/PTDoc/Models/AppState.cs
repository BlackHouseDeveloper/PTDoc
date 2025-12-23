namespace PTDoc.Models;

/// <summary>
/// Represents application state stored in the database (key-value pairs).
/// </summary>
public sealed class AppState
{
    /// <summary>
    /// Gets or sets the unique identifier for the app state entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the unique key for this app state entry.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the value for this app state entry.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this entry was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the timestamp when this entry was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets a description of this app state entry.
    /// </summary>
    public string? Description { get; set; }
}
