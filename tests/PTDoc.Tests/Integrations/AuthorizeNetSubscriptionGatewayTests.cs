using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Integrations.Services;

namespace PTDoc.Tests.Integrations;

[Trait("Category", "CoreCi")]
public sealed class AuthorizeNetSubscriptionGatewayTests
{
    [Fact]
    public async Task InitialCharge_UsesConfiguredGateway_AndTokenizedServerAmount()
    {
        using var handler = new CaptureHandler("""{"transactionResponse":{"responseCode":"1","transId":"123"}}""");
        var gateway = Create(handler);
        var result = await gateway.ChargeAsync("PDinvoice", 49m, "COMMON.ACCEPT.INAPP.PAYMENT", "opaque-test", default);
        Assert.Equal("123", result.TransactionId);
        Assert.Equal("apitest.authorize.net", handler.Uri!.Host);
        using var request = JsonDocument.Parse(handler.Body!);
        var transaction = request.RootElement.GetProperty("createTransactionRequest").GetProperty("transactionRequest");
        Assert.Equal("49.00", transaction.GetProperty("amount").GetString());
        Assert.Equal("authCaptureTransaction", transaction.GetProperty("transactionType").GetString());
        Assert.Equal("PDinvoice", transaction.GetProperty("order").GetProperty("invoiceNumber").GetString());
        Assert.False(transaction.GetProperty("payment").TryGetProperty("creditCard", out _));
    }

    [Theory]
    [InlineData("4")]
    [InlineData("3")]
    public async Task HeldOrAmbiguousCharge_IsNotReportedAsSafeToRetry(string code)
    {
        using var handler = new CaptureHandler(JsonSerializer.Serialize(new
        {
            transactionResponse = new { responseCode = code, transId = "123" }
        }));
        await Assert.ThrowsAsync<SubscriptionUnavailableException>(() => Create(handler).ChargeAsync("PDinvoice", 49m, "descriptor", "token", default));
    }

    [Fact]
    public async Task Decline_DoesNotReportSuccessfulPayment()
    {
        using var handler = new CaptureHandler("""{"transactionResponse":{"responseCode":"2","transId":"0"}}""");
        var response = await Create(handler).ChargeAsync("PDinvoice", 49m, "descriptor", "token", default);
        Assert.True(response.Declined);
        Assert.Null(response.TransactionId);
    }

    [Theory]
    [InlineData("capturedPendingSettlement", true)]
    [InlineData("settledSuccessfully", true)]
    [InlineData("authorizedPendingCapture", false)]
    [InlineData("FDSAuthorizedPendingReview", false)]
    [InlineData("voided", false)]
    public async Task TransactionVerification_RequiresCapturedFunds(string status, bool captured)
    {
        using var handler = new CaptureHandler(JsonSerializer.Serialize(new
        {
            messages = new { resultCode = "Ok" },
            transaction = new
            {
                transId = "123",
                transactionStatus = status,
                responseCode = 1,
                settleAmount = "49.00",
                submitTimeUTC = "2026-11-03T12:00:00Z",
                order = new { invoiceNumber = "PDinvoice" },
                subscription = new { id = "456", payNum = 2 }
            }
        }));
        var result = await Create(handler).GetTransactionAsync("123", default);
        Assert.Equal(captured, result.Captured);
        Assert.Equal(49m, result.Amount);
        Assert.Equal(DateTimeKind.Utc, result.SubmittedAtUtc.Kind);
        Assert.Equal("456", result.RecurringId);
        Assert.Equal(2, result.PaymentNumber);
    }

    [Fact]
    public async Task Reconciliation_ReadsProviderHistoryWithoutTreatingActiveStatusAsPayment()
    {
        using var handler = new CaptureHandler("""
            {"messages":{"resultCode":"Ok"},"subscription":{"status":"active","arbTransactions":[
            {"transId":"123","payNum":1},{"transId":"123","payNum":1},{"transId":"0","payNum":2}]}}
            """);
        var result = await Create(handler).GetRecurringAsync("456", default);
        Assert.Equal("active", result.Status);
        Assert.Equal("123", Assert.Single(result.TransactionIds!));
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.True(request.RootElement.GetProperty("ARBGetSubscriptionRequest").GetProperty("includeTransactions").GetBoolean());
    }

    [Fact]
    public async Task RecurringSchedule_StartsAfterInitialPaidPeriod_AndUsesProviderProfile()
    {
        using var handler = new CaptureHandler("""{"messages":{"resultCode":"Ok"},"subscriptionId":"456"}""");
        await Create(handler).CreateRecurringAsync("PTDoc-subscription", 49, 1, new(2026, 12, 3), new SubscriptionProfile("700", "800"), default);
        using var request = JsonDocument.Parse(handler.Body!);
        var subscription = request.RootElement.GetProperty("ARBCreateSubscriptionRequest").GetProperty("subscription");
        Assert.Equal("2026-12-03", subscription.GetProperty("paymentSchedule").GetProperty("startDate").GetString());
        Assert.Equal("months", subscription.GetProperty("paymentSchedule").GetProperty("interval").GetProperty("unit").GetString());
        Assert.Equal("800", subscription.GetProperty("profile").GetProperty("customerPaymentProfileId").GetString());
        Assert.False(subscription.TryGetProperty("payment", out _));
    }

    private static AuthorizeNetSubscriptionGateway Create(CaptureHandler handler)
    {
        var client = new HttpClient(handler, disposeHandler: false);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(item => item.CreateClient(It.IsAny<string>())).Returns(client);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Subscriptions:AuthorizeNet:Environment"] = "Sandbox",
            ["Subscriptions:AuthorizeNet:ApiLoginId"] = "test-login",
            ["Subscriptions:AuthorizeNet:TransactionKey"] = "test-only-key"
        }).Build();
        return new(factory.Object, configuration);
    }

    private sealed class CaptureHandler(string response) : HttpMessageHandler
    {
        public string? Body;
        public Uri? Uri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
