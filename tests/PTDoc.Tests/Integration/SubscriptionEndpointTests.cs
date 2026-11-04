using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class SubscriptionEndpointTests(PtDocApiFactory factory) : IClassFixture<PtDocApiFactory>
{
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
