namespace PTDoc.Core.Models;

/// <summary>
/// Represents a single objective measurement recorded within a clinical note.
/// Used for ROM, MMT, and other quantified assessments.
/// Per TDD §5.4 — links to a ClinicalNote and records body-part-specific metrics.
/// </summary>
public class ObjectiveMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Foreign key to ClinicalNote
    public Guid NoteId { get; set; }

    // The anatomical region being measured
    public BodyPart BodyPart { get; set; }

    // The type of measurement (e.g. ROM, MMT)
    public MetricType MetricType { get; set; }

    // The measured value (degrees for ROM, score for MMT, etc.)
    public string Value { get; set; } = string.Empty;

    // Laterality (e.g. Left, Right, Bilateral)
    public string? Side { get; set; }

    // Unit of measurement (e.g. degrees, grade, sec)
    public string? Unit { get; set; }

    // Whether the measurement is Within Normal Limits
    public bool IsWNL { get; set; }

    // Audit timestamp
    public DateTime LastModifiedUtc { get; set; }

    // Navigation property
    public ClinicalNote? Note { get; set; }
}

/// <summary>
/// Body parts used in objective metrics per TDD §5.4.
/// </summary>
public enum BodyPart
{
    Knee = 0,
    Shoulder = 1,
    Hip = 2,
    Ankle = 3,
    Elbow = 4,
    Wrist = 5,
    Cervical = 6,
    Lumbar = 7,
    Thoracic = 8,
    Foot = 9,
    Hand = 10,
    PelvicFloor = 11,
    Other = 99
}

/// <summary>
/// Types of objective metrics per TDD §5.4.
/// </summary>
public enum MetricType
{
    ROM = 0,    // Range of Motion
    MMT = 1,    // Manual Muscle Test
    Girth = 2,
    Pain = 3,
    Functional = 4,
    Other = 99
}
