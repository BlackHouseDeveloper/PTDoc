namespace PTDoc.Core.Models;

public enum IntegrationOutboxStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    DeadLetter = 3
}

public static class IntegrationJobTypes
{
    public const string FaxSubmit = "FaxSubmit";
    public const string FaxStatusReconcile = "FaxStatusReconcile";
    public const string FaxInboundRetrieve = "FaxInboundRetrieve";
    public const string FaxInboundPoll = "FaxInboundPoll";
    public const string WibbiPatientSync = "WibbiPatientSync";
    public const string WibbiProgramPublish = "WibbiProgramPublish";
    public const string WibbiTrackingSync = "WibbiTrackingSync";
    public const string WibbiDeltaSync = "WibbiDeltaSync";
}

public sealed class IntegrationOutboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public IntegrationOutboxStatus Status { get; set; } = IntegrationOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 8;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }

    public IntegrationConnection? IntegrationConnection { get; set; }
}

public sealed class IntegrationSyncCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string SyncType { get; set; } = string.Empty;
    public DateTime? LastSuccessfulAtUtc { get; set; }
    public string? Cursor { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public IntegrationConnection? IntegrationConnection { get; set; }
}

public enum IntegrationConflictStatus
{
    Open = 0,
    ResolvedUsePTDoc = 1,
    ResolvedUseProvider = 2,
    Dismissed = 3
}

public sealed class IntegrationConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid InternalEntityId { get; set; }
    public string ConflictType { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public IntegrationConflictStatus Status { get; set; } = IntegrationConflictStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }

    public IntegrationConnection? IntegrationConnection { get; set; }
}

public sealed class ProcessedIntegrationWebhook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadHashSha256 { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public IntegrationConnection? IntegrationConnection { get; set; }
}
