namespace PTDoc.Application.Identity;

public sealed record IndividualSignupRequest(string FullName, string Email, DateTime DateOfBirth,
    string Pin, string LicenseNumber, string LicenseState, string ClinicName, string TimeZoneId);
public sealed record SubscriptionPaymentRequest(string OpaqueDataDescriptor, string OpaqueDataToken, bool AcceptRecurringTerms = false);
public sealed record SubscriptionOffer(bool Enabled, decimal Amount, int IntervalMonths, string Environment,
    string ApiLoginId, string ClientKey, string? OrganizationDemoUrl);
public sealed record SubscriptionAccount(Guid Id, bool HasClinicalAccess, DateTime? PaidThroughUtc,
    decimal Amount, int IntervalMonths, string Status, bool NeedsReconciliation, bool HasRecurringBilling);

public interface ISubscriptionService
{
    SubscriptionOffer GetOffer();
    Task<RegistrationResult> RegisterAsync(IndividualSignupRequest request, CancellationToken ct = default);
    Task<SubscriptionAccount?> GetAccountAsync(CancellationToken ct = default);
    Task<SubscriptionAccount> PayAsync(SubscriptionPaymentRequest request, CancellationToken ct = default);
    Task<SubscriptionAccount> RepairAsync(CancellationToken ct = default);
    Task<SubscriptionAccount> CancelAsync(CancellationToken ct = default);
}

public sealed class SubscriptionConflictException(string message) : Exception(message);
public sealed class SubscriptionUnavailableException(string message) : Exception(message);
