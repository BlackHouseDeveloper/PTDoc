namespace PTDoc.Application.Integrations;

/// <summary>
/// Fax service interface for sending documents to external providers.
/// Implementation lives in PTDoc.Integrations project.
/// </summary>
public interface IFaxService
{
    /// <summary>
    /// Send a fax to a recipient.
    /// </summary>
    Task<FaxResult> SendFaxAsync(FaxRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get fax transmission status.
    /// </summary>
    Task<FaxResult> GetFaxStatusAsync(string faxId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for sending a fax.
/// </summary>
public class FaxRequest
{
    /// <summary>
    /// Recipient fax number (format: +1-555-555-5555).
    /// </summary>
    public string RecipientNumber { get; set; } = string.Empty;

    /// <summary>
    /// Recipient name for cover page.
    /// </summary>
    public string? RecipientName { get; set; }

    /// <summary>
    /// PDF document content to fax.
    /// </summary>
    public byte[] PdfContent { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Internal patient ID for mapping and audit trail.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Document type (e.g., "Progress Note", "Plan of Care").
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// Cover page message.
    /// </summary>
    public string? CoverPageMessage { get; set; }
}

/// <summary>
/// Result of a fax operation.
/// </summary>
public class FaxResult
{
    public bool Success { get; set; }
    public string? FaxId { get; set; }
    public string? Status { get; set; } // "Queued", "Sending", "Sent", "Failed"
    public string? ErrorMessage { get; set; }
    public DateTime? SentAt { get; set; }
    public int? PageCount { get; set; }
}
