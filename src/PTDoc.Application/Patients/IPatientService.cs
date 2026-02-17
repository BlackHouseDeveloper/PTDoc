namespace PTDoc.Application.Patients;

/// <summary>
/// Interface for patient management operations including deduplication and merging.
/// </summary>
public interface IPatientService
{
    /// <summary>
    /// Searches for potential duplicate patients before creating a new one.
    /// </summary>
    Task<List<PatientDuplicateMatch>> FindPotentialDuplicatesAsync(PatientSearchCriteria criteria, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Merges two patient records, transferring all associated data.
    /// </summary>
    Task<PatientMergeResult> MergePatientsAsync(Guid sourcePatientId, Guid targetPatientId, Guid mergedByUserId, string reason, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new patient with duplicate check.
    /// </summary>
    Task<PatientCreationResult> CreatePatientAsync(CreatePatientRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Patient search criteria for deduplication.
/// </summary>
public class PatientSearchCriteria
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? MedicalRecordNumber { get; set; }
}

/// <summary>
/// Potential duplicate match with confidence score.
/// </summary>
public class PatientDuplicateMatch
{
    public Guid PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public double ConfidenceScore { get; set; }
    public List<string> MatchReasons { get; set; } = new();
}

/// <summary>
/// Result of patient merge operation.
/// </summary>
public class PatientMergeResult
{
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid SurvivingPatientId { get; set; }
    public int NotesTransferred { get; set; }
    public int AppointmentsTransferred { get; set; }
    public int ExternalMappingsRemapped { get; set; }
    public DateTime MergedUtc { get; set; }
}

/// <summary>
/// Request to create a new patient.
/// </summary>
public class CreatePatientRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public bool SkipDuplicateCheck { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>
/// Result of patient creation.
/// </summary>
public class PatientCreationResult
{
    public bool IsSuccessful { get; set; }
    public Guid? PatientId { get; set; }
    public string? ErrorMessage { get; set; }
    public List<PatientDuplicateMatch> PotentialDuplicates { get; set; } = new();
    public bool RequiresDuplicateResolution { get; set; }
}
