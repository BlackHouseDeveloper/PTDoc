namespace PTDoc.Application.Integrations;

// Only the server adapter creates these verified results; browsers never supply entitlement values.
public sealed record SubscriptionCharge(string? TransactionId, bool Declined);
public sealed record SubscriptionTransaction(string Id, string InvoiceNumber, decimal Amount,
    DateTime SubmittedAtUtc, bool Captured, string? RecurringId, int PaymentNumber);
public sealed record SubscriptionProfile(string CustomerId, string PaymentId);
public sealed record RecurringSubscription(string Id, string Status, IReadOnlyList<string>? TransactionIds = null);

public interface ISubscriptionGateway
{
    Task<SubscriptionCharge> ChargeAsync(string invoice, decimal amount, string descriptor, string token, CancellationToken ct);
    Task<SubscriptionTransaction> GetTransactionAsync(string transactionId, CancellationToken ct);
    Task<SubscriptionProfile> CreateProfileAsync(string transactionId, CancellationToken ct);
    Task<string> CreateRecurringAsync(string name, decimal amount, int intervalMonths, DateTime startUtc,
        SubscriptionProfile profile, CancellationToken ct);
    Task<RecurringSubscription> GetRecurringAsync(string subscriptionId, CancellationToken ct);
    Task CancelAsync(string subscriptionId, CancellationToken ct);
}
