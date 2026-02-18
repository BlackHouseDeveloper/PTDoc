namespace PTDoc.Application.Integrations;

/// <summary>
/// Home Exercise Program (HEP) service interface for assigning exercise programs to patients.
/// Implementation lives in PTDoc.Integrations project.
/// </summary>
public interface IHomeExerciseProgramService
{
    /// <summary>
    /// Assign a HEP to a patient.
    /// </summary>
    Task<HepAssignmentResult> AssignProgramAsync(HepAssignmentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get patient's current HEP status.
    /// </summary>
    Task<HepAssignmentResult> GetPatientProgramAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for assigning a home exercise program.
/// </summary>
public class HepAssignmentRequest
{
    /// <summary>
    /// Internal patient ID.
    /// </summary>
    public Guid PatientId { get; set; }
    
    /// <summary>
    /// Patient email for HEP delivery.
    /// </summary>
    public string PatientEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// Patient first name.
    /// </summary>
    public string PatientFirstName { get; set; } = string.Empty;
    
    /// <summary>
    /// Patient last name.
    /// </summary>
    public string PatientLastName { get; set; } = string.Empty;
    
    /// <summary>
    /// Program ID from external system (e.g., Wibbi program ID).
    /// </summary>
    public string ProgramId { get; set; } = string.Empty;
    
    /// <summary>
    /// Program name/description.
    /// </summary>
    public string? ProgramName { get; set; }
    
    /// <summary>
    /// Duration in days.
    /// </summary>
    public int? DurationDays { get; set; }
}

/// <summary>
/// Result of a HEP assignment operation.
/// </summary>
public class HepAssignmentResult
{
    public bool Success { get; set; }
    public string? AssignmentId { get; set; }
    public string? PatientPortalUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? AssignedAt { get; set; }
}
