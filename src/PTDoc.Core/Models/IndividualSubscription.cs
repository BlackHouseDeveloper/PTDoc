namespace PTDoc.Core.Models;

/// <summary>Paid access is independent of identity, clinic grants and clinical records.</summary>
public sealed class IndividualSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ClinicId { get; set; }
    public decimal Amount { get; set; }
    public int IntervalMonths { get; set; }
    public string Environment { get; set; } = "Sandbox";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? PaidThroughUtc { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InitialTransactionId { get; set; }
    public string? CustomerProfileId { get; set; }
    public string? PaymentProfileId { get; set; }
    public string? RecurringSubscriptionId { get; set; }
    public DateTime? RecurringStartsAtUtc { get; set; }
    public string ProviderStatus { get; set; } = "pending";
    // Persist before making a remote mutation. An uncertain result requires reconciliation,
    // never a blind repeat of a charge or subscription creation.
    public string? PendingOperation { get; set; }
    public long Version { get; set; } = 1;

    public bool HasClinicalAccess(DateTime utcNow) => PaidThroughUtc > utcNow;
}

public sealed class SubscriptionPayment
{
    public string TransactionId { get; set; } = string.Empty;
    public Guid SubscriptionId { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public DateTime CoveredThroughUtc { get; set; }
}
