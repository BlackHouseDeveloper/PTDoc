namespace PTDoc.Application.Integrations;

/// <summary>
/// Interface for fax service (HumbleFax).
/// </summary>
public interface IFaxService
{
    /// <summary>
    /// Sends a PDF document via fax.
    /// </summary>
    Task<FaxResult> SendFaxAsync(FaxRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the status of a fax job.
    /// </summary>
    Task<FaxJobStatus?> GetFaxStatusAsync(string faxJobId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves fax send history for a patient.
    /// </summary>
    Task<List<FaxJobStatus>> GetPatientFaxHistoryAsync(Guid patientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fax send request.
/// </summary>
public class FaxRequest
{
    public Guid PatientId { get; set; }
    public string RecipientFaxNumber { get; set; } = string.Empty;
    public string RecipientName { get; set; } = string.Empty;
    public byte[] PdfData { get; set; } = Array.Empty<byte>();
    public string DocumentType { get; set; } = string.Empty;
    public Guid? ClinicalNoteId { get; set; }
    public Guid SentByUserId { get; set; }
}

/// <summary>
/// Result of fax send operation.
/// </summary>
public class FaxResult
{
    public bool IsSuccessful { get; set; }
    public string? FaxJobId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SubmittedUtc { get; set; }
}

/// <summary>
/// Fax job status.
/// </summary>
public class FaxJobStatus
{
    public string FaxJobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Queued, Sending, Sent, Failed
    public DateTime SubmittedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string? ErrorMessage { get; set; }
    public int? PagesSent { get; set; }
    public int AttemptCount { get; set; }
}
