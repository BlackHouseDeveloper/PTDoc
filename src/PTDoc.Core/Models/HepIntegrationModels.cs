namespace PTDoc.Core.Models;

public enum HepProgramStatus
{
    Draft = 0,
    Queued = 1,
    Synced = 2,
    Conflict = 3,
    Failed = 4,
    Archived = 5
}

public enum HepRevisionSource
{
    PTDoc = 0,
    Wibbi = 1
}

public sealed class HepProgram
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public string? ProviderProgramId { get; set; }
    public string? ProviderEpisodeId { get; set; }
    public HepProgramStatus Status { get; set; } = HepProgramStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncedAtUtc { get; set; }
    public DateTime? LastTrackingSyncAtUtc { get; set; }
    public string? LastFailureCode { get; set; }

    public Clinic? Clinic { get; set; }
    public IntegrationConnection? IntegrationConnection { get; set; }
    public Patient? Patient { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<HepProgramRevision> Revisions { get; set; } = new List<HepProgramRevision>();
    public ICollection<HepTrackingObservation> TrackingObservations { get; set; } = new List<HepTrackingObservation>();
}

public sealed class HepProgramRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HepProgramId { get; set; }
    public int Version { get; set; }
    public HepRevisionSource Source { get; set; } = HepRevisionSource.PTDoc;
    public string Title { get; set; } = string.Empty;
    public string? TherapistNotes { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAtUtc { get; set; }
    public string? ProviderVersion { get; set; }

    public HepProgram? HepProgram { get; set; }
    public User? CreatedByUser { get; set; }
    public ICollection<HepPrescriptionExercise> Exercises { get; set; } = new List<HepPrescriptionExercise>();
}

public sealed class HepPrescriptionExercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HepProgramRevisionId { get; set; }
    public int SortOrder { get; set; }
    public string ExternalExerciseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? DescriptionOverride { get; set; }
    public string? Sets { get; set; }
    public string? Repetitions { get; set; }
    public string? Weight { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
    public string? Hold { get; set; }
    public string? Tempo { get; set; }
    public string? Rest { get; set; }
    public string? Level { get; set; }
    public string? Other { get; set; }
    public bool IsHomeExercise { get; set; } = true;
    public bool Mirror { get; set; }
    public bool Flip { get; set; }

    public HepProgramRevision? HepProgramRevision { get; set; }
}

public sealed class HepTrackingObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid HepProgramId { get; set; }
    public string ProviderObservationId { get; set; } = string.Empty;
    public string? ExternalExerciseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? UnitOfMeasure { get; set; }
    public DateTime ActivityAtUtc { get; set; }
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;

    public HepProgram? HepProgram { get; set; }
}
