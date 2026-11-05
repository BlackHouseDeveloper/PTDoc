using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Identity;

public sealed class SubscriptionService(ApplicationDbContext db, IUserRegistrationService registration,
    IIdentityContextAccessor identity, ISubscriptionGateway gateway, IConfiguration configuration, TimeProvider clock) : ISubscriptionService
{
    public SubscriptionOffer GetOffer()
    {
        var section = configuration.GetSection("Subscriptions");
        var provider = section.GetSection("AuthorizeNet");
        decimal.TryParse(section["Amount"], NumberStyles.Integer | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount);
        var interval = section["IntervalMonths"];
        var months = interval is null ? 1
            : int.TryParse(interval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedMonths) ? parsedMonths : 0;
        var signature = provider["SignatureKey"] ?? string.Empty;
        var configured = amount > 0 && amount <= 99999 && decimal.Round(amount, 2) == amount && months is >= 1 and <= 12
            && provider["Environment"] is "Sandbox" or "Production"
            && !string.IsNullOrWhiteSpace(provider["ApiLoginId"]) && !string.IsNullOrWhiteSpace(provider["TransactionKey"])
            && !string.IsNullOrWhiteSpace(provider["ClientKey"]) && signature.Length == 128 && signature.All(Uri.IsHexDigit);
        var enabled = bool.TryParse(section["Enabled"], out var requestedEnabled) && requestedEnabled && configured;
        var demo = section["OrganizationDemoUrl"];
        if (!Uri.TryCreate(demo, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) demo = null;
        return new(enabled, amount, months, provider["Environment"] ?? "Sandbox",
            configured ? provider["ApiLoginId"]! : string.Empty, configured ? provider["ClientKey"]! : string.Empty, demo);
    }

    public async Task<RegistrationResult> RegisterAsync(IndividualSignupRequest request, CancellationToken ct = default)
    {
        var offer = GetOffer();
        if (!offer.Enabled) throw new SubscriptionUnavailableException("Individual subscriptions are not available yet.");
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Length > 200
            || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 255
            || string.IsNullOrWhiteSpace(request.LicenseNumber) || request.LicenseNumber.Length > 50
            || request.DateOfBirth.Date > clock.GetUtcNow().UtcDateTime.Date)
            throw new ArgumentException("Check the account name, email, license number and date of birth.");
        if (string.IsNullOrWhiteSpace(request.ClinicName) || request.ClinicName.Trim().Length > 200
            || string.IsNullOrWhiteSpace(request.TimeZoneId) || request.TimeZoneId.Length > 100
            || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZoneId, out _))
            throw new ArgumentException("Enter a clinic name and valid time zone.");
        // All local provisioning is atomic. A repeated email never creates a second organization.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var clinic = new Clinic { Name = request.ClinicName.Trim(), Slug = "individual-" + Guid.NewGuid().ToString("N"), TimeZoneId = request.TimeZoneId };
        db.Clinics.Add(clinic);
        await db.SaveChangesAsync(ct);
        var result = await registration.RegisterAsync(new(request.FullName, request.Email, request.DateOfBirth,
            Roles.PT, clinic.Id, request.Pin, request.LicenseNumber, request.LicenseState), ct);
        if (!result.IsPending || !result.UserId.HasValue)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return result;
        }
        var user = await db.Users.SingleAsync(item => item.Id == result.UserId.Value, ct);
        // Active identity permits billing recovery. The subscription guard, not IsActive,
        // controls paid clinical access. Only this newly created account is changed.
        user.IsActive = true;
        var membership = await db.OrganizationMemberships.SingleAsync(item => item.UserId == user.Id && item.OrganizationId == clinic.OrganizationId, ct);
        membership.Role = OrganizationRole.Owner;
        membership.Version++;
        var subscription = new IndividualSubscription
        {
            UserId = user.Id,
            OrganizationId = clinic.OrganizationId!.Value,
            ClinicId = clinic.Id,
            Amount = offer.Amount,
            IntervalMonths = offer.IntervalMonths,
            Environment = offer.Environment,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.IndividualSubscriptions.Add(subscription);
        Audit(subscription, "IndividualBillingAccountCreated");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(RegistrationStatus.Succeeded, user.Id, null);
    }

    public async Task<SubscriptionAccount?> GetAccountAsync(CancellationToken ct = default)
    {
        var subscription = await db.IndividualSubscriptions.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == identity.GetCurrentUserId(), ct);
        return subscription is null ? null : View(subscription);
    }

    public async Task<SubscriptionAccount> PayAsync(SubscriptionPaymentRequest request, CancellationToken ct = default)
    {
        var subscription = await OwnedAsync(ct);
        if (!GetOffer().Enabled) throw new SubscriptionUnavailableException("New payments are not available. Contact support.");
        if (subscription.PendingOperation is not null) throw Pending();
        if (subscription.ProviderStatus == "canceled")
            throw new SubscriptionConflictException("Contact support to reconcile canceled billing before restarting payment.");
        if (subscription.HasClinicalAccess(clock.GetUtcNow().UtcDateTime)) return View(subscription);
        if (!request.AcceptRecurringTerms || request.OpaqueDataDescriptor != "COMMON.ACCEPT.INAPP.PAYMENT" || string.IsNullOrWhiteSpace(request.OpaqueDataToken)
            || request.OpaqueDataToken.Length > 8192) throw new ArgumentException("Choose a payment method using the secure payment form.");
        // Stop the old schedule before a recovery charge, avoiding a concurrent renewal.
        // Recovery requires cancellation plus reconciliation of any already-submitted charge.
        if (subscription.RecurringSubscriptionId is not null)
            throw new SubscriptionConflictException("Cancel recurring billing and let support reconcile any pending renewal before restarting payment.");
        subscription.InvoiceNumber = "PD" + Guid.NewGuid().ToString("N")[..18];
        subscription.InitialTransactionId = null;
        subscription.CustomerProfileId = null;
        subscription.PaymentProfileId = null;
        subscription.ProviderStatus = "pending";
        await BeginAsync(subscription, "charge", ct);
        var charge = await gateway.ChargeAsync(subscription.InvoiceNumber, subscription.Amount, request.OpaqueDataDescriptor, request.OpaqueDataToken, ct);
        if (charge.Declined)
        {
            subscription.PendingOperation = null;
            subscription.ProviderStatus = "declined";
            await SaveAsync(subscription, "IndividualPaymentDeclined", ct);
            return View(subscription);
        }
        subscription.InitialTransactionId = charge.TransactionId ?? throw Pending();
        await SaveAsync(subscription, "IndividualPaymentVerificationPending", ct);
        var verified = await gateway.GetTransactionAsync(subscription.InitialTransactionId, ct);
        await ApplyPaymentAsync(subscription, verified, ct);
        if (subscription.PendingOperation == "charge") return View(subscription);
        return await ProvisionRecurringAsync(subscription, ct);
    }

    public async Task<SubscriptionAccount> RepairAsync(CancellationToken ct = default)
    {
        var subscription = await OwnedAsync(ct);
        if (subscription.PendingOperation == "charge" && subscription.InitialTransactionId is not null)
            await ApplyPaymentAsync(subscription, await gateway.GetTransactionAsync(subscription.InitialTransactionId, ct), ct);
        if (subscription.PendingOperation == "cancel" && subscription.RecurringSubscriptionId is not null)
        {
            var status = await gateway.GetRecurringAsync(subscription.RecurringSubscriptionId, ct);
            if (status.Status is "canceled" or "terminated" or "expired")
            {
                subscription.ProviderStatus = status.Status;
                subscription.PendingOperation = null;
                await SaveAsync(subscription, "IndividualCancellationConfirmed", ct);
            }
        }
        if (subscription.PendingOperation is not null) return View(subscription);
        if (subscription.RecurringSubscriptionId is not null)
        {
            var recurring = await gateway.GetRecurringAsync(subscription.RecurringSubscriptionId, ct);
            foreach (var transactionId in recurring.TransactionIds ?? [])
                await ApplyPaymentAsync(subscription, await gateway.GetTransactionAsync(transactionId, ct), ct);
            subscription.ProviderStatus = recurring.Status;
            await SaveAsync(subscription, "IndividualBillingReconciled", ct);
            return View(subscription);
        }
        return await ProvisionRecurringAsync(subscription, ct);
    }

    public async Task<SubscriptionAccount> CancelAsync(CancellationToken ct = default)
    {
        var subscription = await OwnedAsync(ct);
        if (subscription.PendingOperation is not null) throw Pending();
        if (subscription.RecurringSubscriptionId is null)
        {
            subscription.ProviderStatus = "canceled";
            await SaveAsync(subscription, "IndividualBillingCancelled", ct);
            return View(subscription);
        }
        await BeginAsync(subscription, "cancel", ct);
        await gateway.CancelAsync(subscription.RecurringSubscriptionId, ct);
        subscription.ProviderStatus = "canceled";
        subscription.PendingOperation = null;
        // Keep the recurring ID for delayed event reconciliation. Never erase payment history.
        await SaveAsync(subscription, "IndividualBillingCancelled", ct);
        return View(subscription);
    }

    public async Task ReceiveTransactionAsync(string transactionId, CancellationToken ct)
    {
        var verified = await gateway.GetTransactionAsync(transactionId, ct);
        var subscription = await db.IndividualSubscriptions.SingleOrDefaultAsync(item =>
            (verified.RecurringId != null && item.RecurringSubscriptionId == verified.RecurringId)
            || (verified.RecurringId == null && item.InvoiceNumber == verified.InvoiceNumber), ct);
        if (subscription is null) return; // Other merchant payments are not subscriptions.
        EnsureEnvironment(subscription);
        await ApplyPaymentAsync(subscription, verified, ct);
    }

    public async Task ReceiveRecurringAsync(string recurringId, CancellationToken ct)
    {
        var subscription = await db.IndividualSubscriptions.SingleOrDefaultAsync(item => item.RecurringSubscriptionId == recurringId, ct);
        if (subscription is null) return;
        EnsureEnvironment(subscription);
        // Fetch current state rather than trusting event ordering or a browser return URL.
        var status = (await gateway.GetRecurringAsync(recurringId, ct)).Status;
        if (subscription.ProviderStatus == status) return;
        subscription.ProviderStatus = status;
        await SaveAsync(subscription, "IndividualSubscriptionStatusChanged", ct);
    }

    private async Task ApplyPaymentAsync(IndividualSubscription subscription, SubscriptionTransaction payment, CancellationToken ct)
    {
        if (!payment.Captured || payment.Amount != subscription.Amount || payment.SubmittedAtUtc > clock.GetUtcNow().UtcDateTime.AddMinutes(5)) return;
        if (await db.SubscriptionPayments.AnyAsync(item => item.TransactionId == payment.Id, ct)) return;
        DateTime through;
        if (payment.RecurringId is null)
        {
            if (payment.InvoiceNumber != subscription.InvoiceNumber || subscription.PendingOperation != "charge") return;
            subscription.InitialTransactionId = payment.Id;
            subscription.PendingOperation = null;
            through = payment.SubmittedAtUtc.AddMonths(subscription.IntervalMonths);
            subscription.RecurringStartsAtUtc = through;
        }
        else
        {
            if (payment.RecurringId != subscription.RecurringSubscriptionId || payment.PaymentNumber is < 1 or > 9999
                || subscription.RecurringStartsAtUtc is null) return;
            // Absolute coverage from the scheduled payment number; duplicates and out-of-order
            // deliveries cannot add arbitrary months to an entitlement.
            try { through = subscription.RecurringStartsAtUtc.Value.AddMonths(checked(payment.PaymentNumber * subscription.IntervalMonths)); }
            catch (ArgumentOutOfRangeException) { return; }
            if (through > clock.GetUtcNow().UtcDateTime.AddMonths(subscription.IntervalMonths).AddDays(2)) return;
        }
        db.SubscriptionPayments.Add(new SubscriptionPayment
        {
            TransactionId = payment.Id,
            SubscriptionId = subscription.Id,
            PaidAtUtc = payment.SubmittedAtUtc,
            CoveredThroughUtc = through
        });
        if (subscription.PaidThroughUtc is null || through > subscription.PaidThroughUtc) subscription.PaidThroughUtc = through;
        await SaveAsync(subscription, "IndividualPaidAccessConfirmed", ct);
    }

    private async Task<SubscriptionAccount> ProvisionRecurringAsync(IndividualSubscription subscription, CancellationToken ct)
    {
        if (subscription.RecurringSubscriptionId is not null || subscription.InitialTransactionId is null
            || subscription.ProviderStatus == "canceled" || !subscription.HasClinicalAccess(clock.GetUtcNow().UtcDateTime)) return View(subscription);
        if (subscription.PendingOperation is not null) throw Pending();
        if (subscription.CustomerProfileId is null || subscription.PaymentProfileId is null)
        {
            await BeginAsync(subscription, "profile", ct);
            var profile = await gateway.CreateProfileAsync(subscription.InitialTransactionId, ct);
            subscription.CustomerProfileId = profile.CustomerId;
            subscription.PaymentProfileId = profile.PaymentId;
            subscription.PendingOperation = null;
            await SaveAsync(subscription, "IndividualPaymentProfileCreated", ct);
        }
        await BeginAsync(subscription, "recurring", ct);
        subscription.RecurringSubscriptionId = await gateway.CreateRecurringAsync("PTDoc-" + subscription.Id.ToString("N"), subscription.Amount,
            subscription.IntervalMonths, subscription.RecurringStartsAtUtc!.Value, new(subscription.CustomerProfileId!, subscription.PaymentProfileId!), ct);
        subscription.PendingOperation = null;
        subscription.ProviderStatus = "active";
        await SaveAsync(subscription, "IndividualRecurringBillingCreated", ct);
        return View(subscription);
    }

    private async Task<IndividualSubscription> OwnedAsync(CancellationToken ct)
    {
        var subscription = await db.IndividualSubscriptions.SingleOrDefaultAsync(item => item.UserId == identity.GetCurrentUserId(), ct)
            ?? throw new UnauthorizedAccessException("This account has no individual subscription.");
        EnsureEnvironment(subscription);
        return subscription;
    }

    private void EnsureEnvironment(IndividualSubscription subscription)
    {
        if (subscription.Environment != configuration["Subscriptions:AuthorizeNet:Environment"])
            throw new SubscriptionUnavailableException("The billing environment does not match this account.");
    }

    private async Task BeginAsync(IndividualSubscription subscription, string operation, CancellationToken ct)
    {
        subscription.PendingOperation = operation;
        await SaveAsync(subscription, "IndividualBillingOperationStarted", ct);
    }

    private async Task SaveAsync(IndividualSubscription subscription, string eventType, CancellationToken ct)
    {
        subscription.Version++;
        Audit(subscription, eventType);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new SubscriptionConflictException("Billing changed in another request. Reload before continuing."); }
    }

    private void Audit(IndividualSubscription subscription, string eventType) => db.AuditLogs.Add(new AuditLog
    {
        Id = Guid.NewGuid(),
        EventType = eventType,
        EntityType = nameof(IndividualSubscription),
        EntityId = subscription.Id,
        UserId = identity.TryGetCurrentUserId(),
        TimestampUtc = clock.GetUtcNow().UtcDateTime
    });

    private SubscriptionAccount View(IndividualSubscription subscription) => new(subscription.Id,
        subscription.Environment == configuration["Subscriptions:AuthorizeNet:Environment"] && subscription.HasClinicalAccess(clock.GetUtcNow().UtcDateTime), subscription.PaidThroughUtc, subscription.Amount,
        subscription.IntervalMonths, subscription.ProviderStatus, subscription.PendingOperation is not null, subscription.RecurringSubscriptionId is not null);
    private static SubscriptionConflictException Pending() => new("A billing operation needs verification. Refresh billing status; do not submit another payment. Contact support if it remains pending.");
}
