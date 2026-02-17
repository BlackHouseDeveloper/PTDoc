namespace PTDoc.Application.Integrations;

/// <summary>
/// Interface for Home Exercise Program service (Wibbi).
/// Enforces no duplicate patient creation via external system mapping.
/// </summary>
public interface IHomeExerciseProgramService
{
    /// <summary>
    /// Assigns a home exercise program to a patient.
    /// Ensures patient mapping exists before creating in external system.
    /// </summary>
    Task<HEPAssignmentResult> AssignProgramAsync(HEPAssignmentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the status/progress of a patient's HEP.
    /// </summary>
    Task<HEPStatus?> GetProgramStatusAsync(Guid patientId, string programId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets or creates external patient mapping for HEP integration.
    /// Prevents duplicate patient creation.
    /// </summary>
    Task<ExternalMappingResult> EnsurePatientMappingAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to assign a home exercise program.
/// </summary>
public class HEPAssignmentRequest
{
    public Guid PatientId { get; set; }
    public string ProgramId { get; set; } = string.Empty;
    public List<string> ExerciseIds { get; set; } = new();
    public int FrequencyPerWeek { get; set; }
    public int DurationWeeks { get; set; }
    public string? Instructions { get; set; }
    public Guid AssignedByUserId { get; set; }
}

/// <summary>
/// Result of HEP assignment.
/// </summary>
public class HEPAssignmentResult
{
    public bool IsSuccessful { get; set; }
    public string? ExternalProgramId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AssignedUtc { get; set; }
}

/// <summary>
/// HEP status/progress.
/// </summary>
public class HEPStatus
{
    public string ProgramId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Active, Completed, Expired
    public double CompletionPercentage { get; set; }
    public DateTime LastAccessedUtc { get; set; }
    public int SessionsCompleted { get; set; }
}

/// <summary>
/// Result of external patient mapping operation.
/// </summary>
public class ExternalMappingResult
{
    public bool IsSuccessful { get; set; }
    public string? ExternalId { get; set; }
    public bool IsNewMapping { get; set; }
    public string? ErrorMessage { get; set; }
}
