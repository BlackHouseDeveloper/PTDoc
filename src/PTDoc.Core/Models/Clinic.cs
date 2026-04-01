namespace PTDoc.Core.Models;

/// <summary>
/// Represents a clinic (tenant) in the PTDoc multi-clinic architecture.
/// All patient and clinical data is scoped to a clinic for data isolation.
/// </summary>
public class Clinic
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Display name of the clinic.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly identifier used for routing (e.g. "westside-pt").
    /// Must be unique across clinics.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Whether the clinic is currently active. Inactive clinics cannot be logged into.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when the clinic was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Patient> Patients { get; set; } = new List<Patient>();
}
