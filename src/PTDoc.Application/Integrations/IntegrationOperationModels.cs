using PTDoc.Core.Models;

namespace PTDoc.Application.Integrations;

public sealed class IntegrationConnectionResponse
{
    public Guid Id { get; init; }
    public Guid ClinicId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool IsComplianceApproved { get; init; }
    public bool IsSecretConfigured { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
    public string? LastHealthCode { get; init; }
    public DateTime? LastVerifiedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class UpsertIntegrationConnectionRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public bool ComplianceApproved { get; init; }
    public string ConfigurationJson { get; init; } = "{}";
    public string SecretReference { get; init; } = string.Empty;
}

public sealed record WebhookTokenRotationResponse(string WebhookToken);

public sealed class FaxRecipientRequest
{
    public string FaxNumber { get; init; } = string.Empty;
    public string? RecipientName { get; init; }
}

public sealed class CreateFaxTransmissionRequest
{
    public Guid? PatientId { get; init; }
    public Guid? PatientDocumentId { get; init; }
    public Guid? ClinicalNoteId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public string? Base64Content { get; init; }
    public string? CoverSubject { get; init; }
    public string? CoverMessage { get; init; }
    public bool IncludeCoverSheet { get; init; } = true;
    public IReadOnlyList<FaxRecipientRequest> Recipients { get; init; } = Array.Empty<FaxRecipientRequest>();
}

public sealed class FaxTransmissionResponse
{
    public Guid Id { get; init; }
    public Guid? PatientId { get; init; }
    public Guid? OriginalTransmissionId { get; init; }
    public string? ProviderFaxId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentFileName { get; init; } = string.Empty;
    public FaxTransmissionStatus Status { get; init; }
    public string? FailureCode { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public IReadOnlyList<FaxRecipientResponse> Recipients { get; init; } = Array.Empty<FaxRecipientResponse>();
    public IReadOnlyList<FaxStatusEventResponse> StatusEvents { get; init; } = Array.Empty<FaxStatusEventResponse>();
}

public sealed record FaxRecipientResponse(
    Guid Id,
    string FaxNumber,
    string? RecipientName,
    FaxTransmissionStatus Status,
    int AttemptCount,
    string? FailureCode);

public sealed record FaxStatusEventResponse(
    Guid Id,
    FaxTransmissionStatus Status,
    string Source,
    string? FailureCode,
    DateTime OccurredAtUtc);

public sealed class InboundFaxResponse
{
    public Guid Id { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public string ToNumber { get; init; } = string.Empty;
    public string? SenderName { get; init; }
    public int PageCount { get; init; }
    public InboundFaxStatus Status { get; init; }
    public Guid? AssignedPatientId { get; init; }
    public Guid? PatientDocumentId { get; init; }
    public DateTime ReceivedAtUtc { get; init; }
    public DateTime? AssignedAtUtc { get; init; }
}

public sealed class AssignInboundFaxRequest
{
    public Guid PatientId { get; init; }
    public string DocumentType { get; init; } = "Fax";
    public string Reason { get; init; } = string.Empty;
}

public sealed class CreateHepProgramRequest
{
    public string Title { get; init; } = string.Empty;
    public string? TherapistNotes { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public IReadOnlyList<HepExerciseRequest> Exercises { get; init; } = Array.Empty<HepExerciseRequest>();
}

public sealed class HepExerciseRequest
{
    public string ExternalExerciseId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? DescriptionOverride { get; init; }
    public string? Sets { get; init; }
    public string? Repetitions { get; init; }
    public string? Weight { get; init; }
    public string? Frequency { get; init; }
    public string? Duration { get; init; }
    public string? Hold { get; init; }
    public string? Tempo { get; init; }
    public string? Rest { get; init; }
    public string? Level { get; init; }
    public string? Other { get; init; }
    public bool IsHomeExercise { get; init; } = true;
    public bool Mirror { get; init; }
    public bool Flip { get; init; }
}

public sealed class HepProgramResponse
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public HepProgramStatus Status { get; init; }
    public string? ProviderProgramId { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? LastSyncedAtUtc { get; init; }
    public DateTime? LastTrackingSyncAtUtc { get; init; }
    public string? LastFailureCode { get; init; }
    public HepProgramRevisionResponse? CurrentRevision { get; init; }
}

public sealed class HepProgramRevisionResponse
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public HepRevisionSource Source { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? TherapistNotes { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? PublishedAtUtc { get; init; }
    public IReadOnlyList<HepExerciseResponse> Exercises { get; init; } = Array.Empty<HepExerciseResponse>();
}

public sealed record HepExerciseResponse(
    Guid Id,
    int SortOrder,
    string ExternalExerciseId,
    string Title,
    string? DescriptionOverride,
    string? Sets,
    string? Repetitions,
    string? Weight,
    string? Frequency,
    string? Duration,
    string? Hold,
    string? Tempo,
    string? Rest,
    string? Level,
    string? Other,
    bool IsHomeExercise,
    bool Mirror,
    bool Flip);

public sealed record HepTrackingObservationResponse(
    Guid Id,
    string? ExternalExerciseId,
    string Code,
    string Value,
    string? UnitOfMeasure,
    DateTime ActivityAtUtc,
    DateTime ImportedAtUtc);

public sealed record ProviderLaunchResponse(string LaunchUrl);

public sealed record HumbleWebhookAcceptanceResponse(bool Duplicate, string EventType);

public sealed class IntegrationDeadLetterResponse
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public int AttemptCount { get; init; }
    public string? LastErrorCode { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}
