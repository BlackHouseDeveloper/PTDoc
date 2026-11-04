using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>Separate merchant configuration from clinic copays. Never logs payment payloads.</summary>
public sealed class AuthorizeNetSubscriptionGateway(IHttpClientFactory clients, IConfiguration configuration) : ISubscriptionGateway
{
    public async Task<SubscriptionCharge> ChargeAsync(string invoice, decimal amount, string descriptor, string token, CancellationToken ct)
    {
        using var result = await SendAsync("createTransactionRequest", new()
        {
            ["refId"] = invoice,
            ["transactionRequest"] = new
            {
                transactionType = "authCaptureTransaction",
                amount = amount.ToString("F2", CultureInfo.InvariantCulture),
                payment = new { opaqueData = new { dataDescriptor = descriptor, dataValue = token } },
                order = new { invoiceNumber = invoice, description = "PTDoc individual EMR subscription" },
                transactionSettings = new { setting = new[] { new { settingName = "duplicateWindow", settingValue = "28800" } } }
            }
        }, ct, requireSuccess: false);
        var root = result.RootElement;
        if (!root.TryGetProperty("transactionResponse", out var transaction)) throw Uncertain();
        var code = Text(transaction, "responseCode");
        if (code == "2") return new(null, true);
        var id = Text(transaction, "transId");
        // Held, error and ambiguous responses are not proof that a charge did not occur.
        if (code != "1" || !ValidId(id)) throw Uncertain();
        return new(id, false);
    }

    public async Task<SubscriptionTransaction> GetTransactionAsync(string transactionId, CancellationToken ct)
    {
        if (!ValidId(transactionId)) throw new ArgumentException("Invalid transaction reference.");
        using var result = await SendAsync("getTransactionDetailsRequest", new() { ["transId"] = transactionId }, ct);
        var transaction = result.RootElement.GetProperty("transaction");
        var status = Text(transaction, "transactionStatus");
        var amount = transaction.GetProperty("settleAmount");
        var recurring = transaction.TryGetProperty("subscription", out var subscription);
        return new(Text(transaction, "transId"),
            transaction.TryGetProperty("order", out var order) ? Text(order, "invoiceNumber") : string.Empty,
            decimal.Parse(amount.ToString(), CultureInfo.InvariantCulture),
            DateTime.Parse(Text(transaction, "submitTimeUTC"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            Text(transaction, "responseCode") == "1" && status is "capturedPendingSettlement" or "settledSuccessfully",
            recurring ? Text(subscription, "id") : null,
            recurring && int.TryParse(Text(subscription, "payNum"), out var number) ? number : 0);
    }

    public async Task<SubscriptionProfile> CreateProfileAsync(string transactionId, CancellationToken ct)
    {
        using var result = await SendAsync("createCustomerProfileFromTransactionRequest", new() { ["transId"] = transactionId }, ct);
        var root = result.RootElement;
        var customer = Text(root, "customerProfileId");
        var payment = root.GetProperty("customerPaymentProfileIdList")[0].GetString() ?? string.Empty;
        if (!ValidId(customer) || !ValidId(payment)) throw Uncertain();
        return new(customer, payment);
    }

    public async Task<string> CreateRecurringAsync(string name, decimal amount, int intervalMonths, DateTime startUtc,
        SubscriptionProfile profile, CancellationToken ct)
    {
        using var result = await SendAsync("ARBCreateSubscriptionRequest", new()
        {
            ["subscription"] = new
            {
                name,
                paymentSchedule = new
                {
                    interval = new { length = intervalMonths, unit = "months" },
                    startDate = startUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    totalOccurrences = 9999
                },
                amount = amount.ToString("F2", CultureInfo.InvariantCulture),
                profile = new { customerProfileId = profile.CustomerId, customerPaymentProfileId = profile.PaymentId }
            }
        }, ct);
        var id = Text(result.RootElement, "subscriptionId");
        if (!ValidId(id)) throw Uncertain();
        return id;
    }

    public async Task<RecurringSubscription> GetRecurringAsync(string subscriptionId, CancellationToken ct)
    {
        using var result = await SendAsync("ARBGetSubscriptionRequest", new() { ["subscriptionId"] = subscriptionId, ["includeTransactions"] = true }, ct);
        var root = result.RootElement;
        if (root.TryGetProperty("ARBGetSubscriptionResponse", out var wrapped)) root = wrapped;
        var subscription = root.GetProperty("subscription");
        var ids = new List<string>();
        if (subscription.TryGetProperty("arbTransactions", out var transactions))
        {
            if (transactions.ValueKind == JsonValueKind.Object && transactions.TryGetProperty("arbTransaction", out var nested)) transactions = nested;
            var items = transactions.ValueKind == JsonValueKind.Array ? transactions.EnumerateArray().ToArray() : new[] { transactions };
            foreach (var item in items.Where(item => item.ValueKind == JsonValueKind.Object))
            {
                var id = Text(item, "transId");
                if (ValidId(id)) ids.Add(id);
            }
        }
        return new(subscriptionId, Text(subscription, "status"), ids.Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task CancelAsync(string subscriptionId, CancellationToken ct)
    {
        var current = await GetRecurringAsync(subscriptionId, ct);
        if (current.Status is "canceled" or "terminated" or "expired") return;
        using var result = await SendAsync("ARBCancelSubscriptionRequest", new() { ["subscriptionId"] = subscriptionId }, ct);
    }

    private async Task<JsonDocument> SendAsync(string operation, Dictionary<string, object> payload, CancellationToken ct, bool requireSuccess = true)
    {
        var section = configuration.GetSection("Subscriptions:AuthorizeNet");
        var environment = section["Environment"];
        if (environment is not ("Sandbox" or "Production") || string.IsNullOrWhiteSpace(section["ApiLoginId"])
            || string.IsNullOrWhiteSpace(section["TransactionKey"])) throw new SubscriptionUnavailableException("Subscription billing is not configured.");
        // Credentials must precede the operation-specific elements in Authorize.Net's JSON/XML mapping.
        var request = new Dictionary<string, object>
        {
            ["merchantAuthentication"] = new { name = section["ApiLoginId"], transactionKey = section["TransactionKey"] }
        };
        foreach (var item in payload) request.Add(item.Key, item.Value);
        var url = environment == "Production" ? "https://api.authorize.net/xml/v1/request.api" : "https://apitest.authorize.net/xml/v1/request.api";
        using var response = await clients.CreateClient(nameof(AuthorizeNetSubscriptionGateway))
            .PostAsJsonAsync(url, new Dictionary<string, object> { [operation] = request }, ct);
        if (!response.IsSuccessStatusCode) throw Uncertain();
        var result = JsonDocument.Parse((await response.Content.ReadAsStringAsync(ct)).TrimStart('\uFEFF'));
        var responseRoot = result.RootElement;
        if (responseRoot.TryGetProperty(operation.Replace("Request", "Response", StringComparison.Ordinal), out var wrapped)) responseRoot = wrapped;
        if (requireSuccess && (!responseRoot.TryGetProperty("messages", out var messages) || Text(messages, "resultCode") != "Ok"))
        {
            result.Dispose();
            throw Uncertain();
        }
        return result;
    }

    private static string Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : string.Empty;
    private static bool ValidId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 64 && id != "0" && id.All(char.IsAsciiDigit);
    private static SubscriptionUnavailableException Uncertain() => new("Billing could not be confirmed. Check billing status before trying again; contact support if it remains pending.");
}
