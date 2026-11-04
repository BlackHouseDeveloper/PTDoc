using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using Moq;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Integrations.Services;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SubscriptionEndpointTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
    [Theory]
    [InlineData("charge", "createTransactionRequest")]
    [InlineData("profile", "createCustomerProfileFromTransactionRequest")]
    [InlineData("recurring", "ARBCreateSubscriptionRequest")]
    public async Task ProviderTimeout_Returns503_PreservesPendingState_AndBlocksRepeatMutation(string pending, string operation)
    {
        using var handler = new ProviderTimeoutHandler(operation);
        using var providerClient = new HttpClient(handler);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(item => item.CreateClient(nameof(AuthorizeNetSubscriptionGateway))).Returns(providerClient);
        await using var host = factory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:Enabled"] = "true",
                ["Subscriptions:Amount"] = "49",
                ["Subscriptions:IntervalMonths"] = "1",
                ["Subscriptions:AuthorizeNet:Environment"] = "Sandbox",
                ["Subscriptions:AuthorizeNet:ApiLoginId"] = "test-login",
                ["Subscriptions:AuthorizeNet:ClientKey"] = "test-client",
                ["Subscriptions:AuthorizeNet:TransactionKey"] = "test-only",
                ["Subscriptions:AuthorizeNet:SignatureKey"] = new string('A', 128)
            }))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ISubscriptionGateway>(provider =>
                new AuthorizeNetSubscriptionGateway(clients.Object, provider.GetRequiredService<IConfiguration>())))));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            await PtDocApiFactory.SeedTestUsersAsync(db);
            var user = await db.Users.SingleAsync(item => item.Username == "integration-pt");
            var clinic = await db.Clinics.SingleAsync(item => item.Id == user.ClinicId);
            db.IndividualSubscriptions.Add(new IndividualSubscription
            {
                UserId = user.Id,
                ClinicId = clinic.Id,
                OrganizationId = clinic.OrganizationId!.Value,
                Amount = 49,
                IntervalMonths = 1,
                Environment = "Sandbox"
            });
            await db.SaveChangesAsync();
        }
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "PT");
        var payment = new SubscriptionPaymentRequest("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true);
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions/payment", payment);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Check billing status", await response.Content.ReadAsStringAsync());
        var calls = handler.Operations.Count;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/subscriptions/payment", payment)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/subscriptions/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/subscriptions/repair", new { })).StatusCode);
        Assert.Equal(calls, handler.Operations.Count);
        Assert.Equal(1, handler.Operations.Count(item => item == operation));
        await using var verify = host.Services.CreateAsyncScope();
        var verifiedDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = await verifiedDb.IndividualSubscriptions.SingleAsync();
        Assert.Equal(pending, subscription.PendingOperation);
        Assert.Equal(pending != "charge", subscription.HasClinicalAccess(DateTime.UtcNow));
        Assert.Equal(pending == "charge" ? 0 : 1, await verifiedDb.SubscriptionPayments.CountAsync());
    }

    private sealed class ProviderTimeoutHandler(string timeoutOperation) : HttpMessageHandler
    {
        public List<string> Operations { get; } = [];
        private string? invoice;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var operation = document.RootElement.EnumerateObject().Single();
            Operations.Add(operation.Name);
            if (operation.Name == timeoutOperation) throw new TaskCanceledException("Provider timeout.");
            if (operation.Name == "createTransactionRequest")
                invoice = operation.Value.GetProperty("transactionRequest").GetProperty("order").GetProperty("invoiceNumber").GetString();
            object result = operation.Name switch
            {
                "createTransactionRequest" => new { transactionResponse = new { responseCode = "1", transId = "123" } },
                "getTransactionDetailsRequest" => new
                {
                    messages = new { resultCode = "Ok" },
                    transaction = new
                    {
                        transId = "123",
                        transactionStatus = "capturedPendingSettlement",
                        responseCode = "1",
                        settleAmount = "49.00",
                        submitTimeUTC = DateTime.UtcNow.ToString("O"),
                        order = new { invoiceNumber = invoice }
                    }
                },
                "createCustomerProfileFromTransactionRequest" => new
                {
                    messages = new { resultCode = "Ok" },
                    customerProfileId = "700",
                    customerPaymentProfileIdList = new[] { "800" }
                },
                _ => throw new InvalidOperationException("Unexpected provider operation: " + operation.Name)
            };
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
        }
    }

    [Fact]
    public async Task PublicOffer_ContainsNoMerchantSecrets_AndSignupDefaultsOff()
    {
        using var client = factory.CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/v1/subscriptions/offer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("transactionKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signatureKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.False((await response.Content.ReadFromJsonAsync<SubscriptionOffer>())!.Enabled);
    }

    [Theory]
    [InlineData("GET", "/api/v1/subscriptions/account")]
    [InlineData("POST", "/api/v1/subscriptions/payment")]
    [InlineData("POST", "/api/v1/subscriptions/repair")]
    [InlineData("POST", "/api/v1/subscriptions/cancel")]
    public async Task BillingEndpoints_RejectUnauthenticatedRequests(string method, string path)
    {
        using var client = factory.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new SubscriptionPaymentRequest("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Webhook_RejectsUnsignedPaymentEvents()
    {
        using var client = factory.CreateUnauthenticatedClient();
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions/authorize-net/webhook",
            new { eventType = "net.authorize.payment.authcapture.created", payload = new { id = "123" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(65536, true, false, HttpStatusCode.Unauthorized)]
    [InlineData(65536, false, false, HttpStatusCode.Unauthorized)]
    [InlineData(65537, true, false, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(65537, false, false, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(65537, true, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(65537, false, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(65536, true, true, HttpStatusCode.OK)]
    [InlineData(65536, false, true, HttpStatusCode.OK)]
    public async Task Webhook_SizeGateNeverBypassesSignatureOrProviderVerification(
        int size, bool knownLength, bool signed, HttpStatusCode expected)
    {
        var gateway = new Mock<ISubscriptionGateway>(MockBehavior.Strict);
        if (expected == HttpStatusCode.OK)
            gateway.Setup(service => service.GetTransactionAsync("123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SubscriptionTransaction("123", "unmatched-invoice", 49,
                    DateTime.UtcNow, true, null, 0));
        var key = new string('A', 128);
        await using var host = factory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:AuthorizeNet:SignatureKey"] = key
            }))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(gateway.Object))));
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        const string json = """{"eventType":"net.authorize.payment.authcapture.created","payload":{"id":"123"}}""";
        var body = Encoding.UTF8.GetBytes(json.PadRight(size));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/subscriptions/authorize-net/webhook")
        {
            Content = new WebhookContent(body, knownLength)
        };
        if (signed)
            request.Headers.Add("X-ANET-Signature", "sha512=" + Convert.ToHexString(HMACSHA512.HashData(Convert.FromHexString(key), body)));
        using var client = host.CreateClient();
        Assert.Equal(expected, (await client.SendAsync(request)).StatusCode);
        gateway.Verify(service => service.GetTransactionAsync("123", It.IsAny<CancellationToken>()),
            expected == HttpStatusCode.OK ? Times.Once() : Times.Never());
        gateway.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("net.authorize.payment.authcapture.created", true, true)]
    [InlineData("net.authorize.payment.authcapture.created", false, false)]
    [InlineData("net.authorize.customer.subscription.updated", false, false)]
    public async Task Webhook_DocumentedEvents_VerifyRenewalsAndKeepLifecycleSeparate(
        string eventType, bool captured, bool extendsCoverage)
    {
        var paymentEvent = eventType == "net.authorize.payment.authcapture.created";
        var starts = DateTime.UtcNow.Date.AddDays(-1);
        var gateway = new Mock<ISubscriptionGateway>(MockBehavior.Strict);
        if (paymentEvent)
            gateway.Setup(service => service.GetTransactionAsync("123", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SubscriptionTransaction("123", "", 49, starts, captured, "456", 1));
        else
            gateway.Setup(service => service.GetRecurringAsync("456", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new RecurringSubscription("456", "active"));
        var key = new string('A', 128);
        await using var host = factory.WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:AuthorizeNet:SignatureKey"] = key,
                ["Subscriptions:AuthorizeNet:Environment"] = "Sandbox"
            }))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(gateway.Object))));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var organization = new Organization { Name = "Renewal fixture" };
            var clinic = new Clinic { OrganizationId = organization.Id, Name = "Renewal fixture", Slug = "renewal-fixture" };
            var user = new User { Username = "renewal-fixture", Role = "PT", ClinicId = clinic.Id };
            db.AddRange(organization, clinic, user, new IndividualSubscription
            {
                UserId = user.Id,
                OrganizationId = organization.Id,
                ClinicId = clinic.Id,
                Amount = 49,
                IntervalMonths = 1,
                Environment = "Sandbox",
                RecurringSubscriptionId = "456",
                RecurringStartsAtUtc = starts,
                PaidThroughUtc = starts
            });
            await db.SaveChangesAsync();
        }
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            eventType,
            payload = new { entityName = paymentEvent ? "transaction" : "subscription", id = paymentEvent ? "123" : "456" }
        });
        using var client = host.CreateClient();
        // Duplicate deliveries must not add another paid period or ledger entry.
        for (var delivery = 0; delivery < 2; delivery++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/subscriptions/authorize-net/webhook")
            {
                Content = new ByteArrayContent(body)
            };
            request.Headers.Add("X-ANET-Signature", "sha512=" + Convert.ToHexString(HMACSHA512.HashData(Convert.FromHexString(key), body)));
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        await using var verify = host.Services.CreateAsyncScope();
        var verifiedDb = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = await verifiedDb.IndividualSubscriptions.SingleAsync();
        Assert.Equal(extendsCoverage ? starts.AddMonths(1) : starts, subscription.PaidThroughUtc);
        Assert.Equal(extendsCoverage ? 1 : 0, await verifiedDb.SubscriptionPayments.CountAsync());
        if (!paymentEvent) Assert.Equal("active", subscription.ProviderStatus);
        gateway.Verify(service => service.GetTransactionAsync("123", It.IsAny<CancellationToken>()), paymentEvent ? Times.Exactly(2) : Times.Never());
        gateway.Verify(service => service.GetRecurringAsync("456", It.IsAny<CancellationToken>()), paymentEvent ? Times.Never() : Times.Exactly(2));
        gateway.VerifyNoOtherCalls();
    }

    private sealed class WebhookContent(byte[] body, bool knownLength) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = body.Length;
            return knownLength;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(body, 0, body.Length);
    }

    [Fact]
    public async Task BillingRecovery_AllowsOwnerReadRepairAndCancel_WithoutGrantingAnotherUserOwnership()
    {
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var previous = configuration["Subscriptions:AuthorizeNet:Environment"];
        configuration["Subscriptions:AuthorizeNet:Environment"] = "Sandbox";
        Guid subscriptionId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(item => item.Username == "integration-pt");
            var clinic = await db.Clinics.SingleAsync(item => item.Id == user.ClinicId);
            var subscription = new IndividualSubscription
            {
                UserId = user.Id,
                ClinicId = clinic.Id,
                OrganizationId = clinic.OrganizationId!.Value,
                Amount = 49,
                IntervalMonths = 1,
                Environment = "Sandbox",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.IndividualSubscriptions.Add(subscription);
            await db.SaveChangesAsync();
            subscriptionId = subscription.Id;
        }
        try
        {
            using var owner = factory.CreateClientWithRole("PT");
            var account = await owner.GetFromJsonAsync<SubscriptionAccount>("/api/v1/subscriptions/account");
            Assert.Equal(subscriptionId, account!.Id);
            Assert.False(account.HasClinicalAccess);
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/subscriptions/repair", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync("/api/v1/subscriptions/cancel", new { })).StatusCode);
            using var foreign = factory.CreateClientWithRole("PTA");
            Assert.Equal(HttpStatusCode.Forbidden, (await foreign.PostAsJsonAsync("/api/v1/subscriptions/cancel", new { })).StatusCode);
        }
        finally
        {
            configuration["Subscriptions:AuthorizeNet:Environment"] = previous;
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.IndividualSubscriptions.Remove(await db.IndividualSubscriptions.SingleAsync(item => item.Id == subscriptionId));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Signup_RoutesCreateBillingIdentity_WithoutUnpaidAccess_AndPreserveDuplicateSafety()
    {
        // A derived host isolates enabled commercial configuration from the default-off tests.
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:Enabled"] = "true",
                ["Subscriptions:Amount"] = "49",
                ["Subscriptions:IntervalMonths"] = "1",
                ["Subscriptions:AuthorizeNet:Environment"] = "Sandbox",
                ["Subscriptions:AuthorizeNet:ApiLoginId"] = "test-login",
                ["Subscriptions:AuthorizeNet:ClientKey"] = "test-client",
                ["Subscriptions:AuthorizeNet:TransactionKey"] = "test-only",
                ["Subscriptions:AuthorizeNet:SignatureKey"] = new string('A', 128)
            })));
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        using var client = host.CreateClient();
        var request = new IndividualSignupRequest("Sample Clinician", "http-subscriber@example.test", new(1990, 1, 1),
            "12345678", "PT123", "NY", "HTTP Clinic", "UTC");
        var response = await client.PostAsJsonAsync("/api/v1/subscriptions/individual", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var registration = await response.Content.ReadFromJsonAsync<RegistrationResult>();
        Assert.True(registration!.Succeeded);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/v1/subscriptions/individual", request)).StatusCode);
        await using var verify = host.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var subscription = await db.IndividualSubscriptions.SingleAsync();
        Assert.Equal(registration.UserId, subscription.UserId);
        Assert.Null(subscription.PaidThroughUtc);
        Assert.Single(await db.Clinics.ToListAsync());
        Assert.Equal("PT", (await db.Users.SingleAsync()).Role);
    }
}
