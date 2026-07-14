namespace PTDoc.Core.Models;

public enum FaxTransmissionStatus
{
    Draft = 0,
    Queued = 1,
    Submitting = 2,
    Accepted = 3,
    InProgress = 4,
    Delivered = 5,
    PartiallyDelivered = 6,
    Failed = 7,
    Cancelled = 8,
    NeedsReconciliation = 9
}

public enum FaxDirection
{
    Outbound = 0,
    Inbound = 1
}

public enum InboundFaxStatus
{
    Retrieving = 0,
    Unassigned = 1,
    Assigned = 2,
    NeedsAttention = 3
}

public sealed class FaxTransmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public Guid? SourceClinicalNoteId { get; set; }
    public Guid? OriginalTransmissionId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public string ClientCorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ProviderFaxId { get; set; }
    public string DocumentStorageKey { get; set; } = string.Empty;
    public string DocumentFileName { get; set; } = string.Empty;
    public string DocumentContentType { get; set; } = "application/pdf";
    public string DocumentHashSha256 { get; set; } = string.Empty;
    public long DocumentSizeBytes { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? CoverSubject { get; set; }
    public string? CoverMessage { get; set; }
    public bool IncludeCoverSheet { get; set; } = true;
    public FaxTransmissionStatus Status { get; set; } = FaxTransmissionStatus.Queued;
    public string? ProviderStatus { get; set; }
    public string? FailureCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public Clinic? Clinic { get; set; }
    public IntegrationConnection? IntegrationConnection { get; set; }
    public Patient? Patient { get; set; }
    public ICollection<FaxRecipient> Recipients { get; set; } = new List<FaxRecipient>();
    public ICollection<FaxStatusEvent> StatusEvents { get; set; } = new List<FaxStatusEvent>();
}

public sealed class FaxRecipient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FaxTransmissionId { get; set; }
    public string FaxNumber { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public FaxTransmissionStatus Status { get; set; } = FaxTransmissionStatus.Queued;
    public string? ProviderStatus { get; set; }
    public string? FailureCode { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public FaxTransmission? FaxTransmission { get; set; }
}

public sealed class FaxStatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FaxTransmissionId { get; set; }
    public FaxTransmissionStatus Status { get; set; }
    public string? ProviderStatus { get; set; }
    public string? FailureCode { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public FaxTransmission? FaxTransmission { get; set; }
}

public sealed class InboundFax
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ProviderFaxId { get; set; } = string.Empty;
    public string ProviderStatus { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string ToNumber { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public int PageCount { get; set; }
    public string DocumentStorageKey { get; set; } = string.Empty;
    public string DocumentFileName { get; set; } = string.Empty;
    public string DocumentContentType { get; set; } = "application/pdf";
    public string DocumentHashSha256 { get; set; } = string.Empty;
    public long DocumentSizeBytes { get; set; }
    public InboundFaxStatus Status { get; set; } = InboundFaxStatus.Retrieving;
    public Guid? AssignedPatientId { get; set; }
    public Guid? PatientDocumentId { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AssignedAtUtc { get; set; }
    public string? AssignmentReason { get; set; }

    public IntegrationConnection? IntegrationConnection { get; set; }
    public Patient? AssignedPatient { get; set; }
    public PatientDocument? PatientDocument { get; set; }
}
