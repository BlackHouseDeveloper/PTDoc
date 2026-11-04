using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Api.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public sealed class SubscriptionServiceTests
{
    [Theory]
    [InlineData("49.95", null, "true", true, 1)]
    [InlineData("49.95", "12", "true", true, 12)]
    [InlineData(null, "1", "true", false, 1)]
    [InlineData("invalid", "1", "true", false, 1)]
    [InlineData("49,95", "1", "true", false, 1)]
    [InlineData("49.95", "invalid", "true", false, 0)]
    [InlineData("49.95", "0", "true", false, 0)]
    [InlineData("49.95", "13", "true", false, 13)]
    [InlineData("49.95", "1", null, false, 1)]
    [InlineData("49.95", "1", "invalid", false, 1)]
    public async Task Offer_ParsesInvariantConfiguration_AndDisablesInvalidBilling(
        string? amount, string? interval, string? enabled, bool expectedEnabled, int expectedMonths)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Configuration["Subscriptions:Amount"] = amount;
        fixture.Configuration["Subscriptions:IntervalMonths"] = interval;
        fixture.Configuration["Subscriptions:Enabled"] = enabled;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var offer = fixture.Service.GetOffer();
            Assert.Equal(expectedEnabled, offer.Enabled);
            Assert.Equal(expectedMonths, offer.IntervalMonths);
            Assert.Equal(amount == "49.95" ? 49.95m : 0m, offer.Amount);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public async Task Signup_IsAtomic_LeavesExistingPendingAccountsUnchanged_AndDoesNotGrantPaidAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacyClinic = new Clinic { Name = "Existing", Slug = "existing" };
        fixture.Db.Clinics.Add(legacyClinic);
        await fixture.Db.SaveChangesAsync();
        var pending = new User { Username = "pending", Email = "pending@example.test", Role = Roles.PT, ClinicId = legacyClinic.Id, IsActive = false };
        fixture.Db.Users.Add(pending);
        await fixture.Db.SaveChangesAsync();
        var request = Signup("new@example.test");

        var result = await fixture.Service.RegisterAsync(request);
        Assert.True(result.Succeeded);
        fixture.Actor = result.UserId!.Value;
        var account = await fixture.Service.GetAccountAsync();
        Assert.NotNull(account);
        Assert.False(account.HasClinicalAccess);
        Assert.True((await fixture.Db.Users.SingleAsync(item => item.Id == result.UserId)).IsActive);
        Assert.False((await fixture.Db.Users.SingleAsync(item => item.Id == pending.Id)).IsActive);
        var organizations = await fixture.Db.Organizations.CountAsync();
        var clinics = await fixture.Db.Clinics.CountAsync();

        Assert.Equal(RegistrationStatus.EmailAlreadyExists, (await fixture.Service.RegisterAsync(request)).Status);
        Assert.Equal(organizations, await fixture.Db.Organizations.CountAsync());
        Assert.Equal(clinics, await fixture.Db.Clinics.CountAsync());
        Assert.Single(await fixture.Db.IndividualSubscriptions.ToListAsync());
    }

    [Fact]
    public async Task CapturedPayment_GrantsOnePeriod_AndRecurringCreationAloneDoesNotExtendIt()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        var account = await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque-test-token", true));
        Assert.True(account.HasClinicalAccess);
        Assert.Equal(fixture.Clock.Utc.UtcDateTime.AddMonths(1), account.PaidThroughUtc);
        Assert.Equal(1, fixture.Gateway.Charges);
        Assert.Equal(49m, fixture.Gateway.ChargedAmount);
        Assert.Equal(account.PaidThroughUtc, fixture.Gateway.RecurringStart);
        Assert.Single(await fixture.Db.SubscriptionPayments.ToListAsync());
        await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "another-token", true));
        Assert.Equal(1, fixture.Gateway.Charges);
    }

    [Fact]
    public async Task DuplicateAndOutOfOrderEvents_DoNotExtendCoverageTwice()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        var subscription = await fixture.Db.IndividualSubscriptions.SingleAsync();
        fixture.Clock.Utc = fixture.Clock.Utc.AddMonths(2);
        fixture.Gateway.Transactions["202"] = new("202", "", 49, fixture.Clock.Utc.UtcDateTime, true, "900", 2);
        fixture.Gateway.Transactions["201"] = new("201", "", 49, fixture.Clock.Utc.UtcDateTime.AddMonths(-1), true, "900", 1);
        await fixture.Service.ReceiveTransactionAsync("202", default);
        await fixture.Service.ReceiveTransactionAsync("202", default);
        await fixture.Service.ReceiveTransactionAsync("201", default);
        Assert.Equal(subscription.RecurringStartsAtUtc!.Value.AddMonths(2), subscription.PaidThroughUtc);
        Assert.Equal(3, await fixture.Db.SubscriptionPayments.CountAsync());
    }

    [Theory]
    [InlineData("held")]
    [InlineData("wrong-amount")]
    [InlineData("wrong-invoice")]
    public async Task UnverifiedOrUnmatchedPayment_NeverGrantsAccess(string caseName)
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        fixture.Gateway.PaymentCase = caseName;
        var account = await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        Assert.False(account.HasClinicalAccess);
        Assert.True(account.NeedsReconciliation);
        Assert.Empty(await fixture.Db.SubscriptionPayments.ToListAsync());
        Assert.Equal(0, fixture.Gateway.RecurringCreates);
    }

    [Fact]
    public async Task LostChargeResponse_BlocksASecondCharge_AndWebhookRecoversPaidAccess()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        fixture.Gateway.LoseChargeResponse = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true)));
        await Assert.ThrowsAsync<SubscriptionConflictException>(() => fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "other", true)));
        Assert.Equal(1, fixture.Gateway.Charges);
        Assert.False((await fixture.Service.GetAccountAsync())!.HasClinicalAccess);

        await fixture.Service.ReceiveTransactionAsync("101", default);
        Assert.True((await fixture.Service.GetAccountAsync())!.HasClinicalAccess);
        await fixture.Service.RepairAsync();
        Assert.Equal(1, fixture.Gateway.RecurringCreates);
        Assert.Equal(1, fixture.Gateway.Charges);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("recurring")]
    public async Task ProvisioningFailure_PreservesPaidPeriod_AndDoesNotRepeatRemoteMutation(string operation)
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        fixture.Gateway.FailOperation = operation;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true)));
        var account = await fixture.Service.RepairAsync();
        Assert.True(account.HasClinicalAccess);
        Assert.True(account.NeedsReconciliation);
        Assert.Equal(1, fixture.Gateway.Charges);
        Assert.Equal(operation, (await fixture.Db.IndividualSubscriptions.SingleAsync()).PendingOperation);
        Assert.Equal(operation == "recurring" ? 1 : 0, fixture.Gateway.RecurringCreates);
    }

    [Fact]
    public async Task CancellationAndExpiry_RetainAccountAndPayments_AndLeaveBillingRecoveryAvailable()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        var account = await fixture.Service.CancelAsync();
        Assert.True(account.HasClinicalAccess);
        fixture.Clock.Utc = new DateTimeOffset(account.PaidThroughUtc!.Value, TimeSpan.Zero);
        Assert.False((await fixture.Service.GetAccountAsync())!.HasClinicalAccess);
        Assert.True((await fixture.Db.Users.SingleAsync(item => item.Id == fixture.Actor)).IsActive);
        Assert.Single(await fixture.Db.SubscriptionPayments.ToListAsync());
        Assert.Equal("canceled", (await fixture.Service.RepairAsync()).Status);
    }

    [Fact]
    public async Task SignupAndAdministration_CannotAddStaffToIndividualClinic()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        var subscription = await fixture.Db.IndividualSubscriptions.SingleAsync();
        var result = await fixture.Registration.RegisterAsync(new("Additional Staff", "staff@example.test", new(1990, 1, 1),
            Roles.PT, subscription.ClinicId, "12345678", "PT123", "NY"));
        Assert.Equal(RegistrationStatus.ClinicNotFound, result.Status);
        Assert.DoesNotContain(await fixture.Registration.GetActiveClinicListAsync(), clinic => clinic.Id == subscription.ClinicId);
        var service = new OrganizationAdministrationService(fixture.Db, fixture.Identity.Object, fixture.Clock);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.CreateClinicAsync(subscription.OrganizationId, new("Another clinic", "another", "UTC")));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.InviteAsync(subscription.OrganizationId, "staff@example.test"));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.SetClinicMembershipAsync(subscription.OrganizationId,
            subscription.ClinicId, fixture.Actor, new(Roles.Admin, true, 1)));
        Assert.Single(await fixture.Db.Users.ToListAsync());
    }

    [Theory]
    [InlineData(false, "/api/v1/patients", false)]
    [InlineData(true, "/api/v1/patients", true)]
    [InlineData(false, "/api/v1/subscriptions/account", true)]
    [InlineData(false, "/api/v1/auth/me", true)]
    [InlineData(true, "/api/v1/integrations/payment/process", false)]
    [InlineData(true, "/api/v1/appointments/123/check-in-payment", false)]
    [InlineData(true, "/api/v1/appointments/123/check-in-payment/", false)]
    [InlineData(true, "/api/v1/appointments/123/CHECK-IN-PAYMENT/", false)]
    [InlineData(true, "/api/v1/appointments/123/check-in", true)]
    [InlineData(true, "/api/v1/organizations", false)]
    public async Task ServerGuard_RechecksExpiryAndPlanLimits(bool paid, string path, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        var subscription = await fixture.Db.IndividualSubscriptions.SingleAsync();
        subscription.PaidThroughUtc = paid ? fixture.Clock.Utc.UtcDateTime.AddDays(1) : fixture.Clock.Utc.UtcDateTime;
        await fixture.Db.SaveChangesAsync();
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.Request.Path = path;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(PTDocClaimTypes.InternalUserId, fixture.Actor.ToString()), new Claim("clinic_id", subscription.ClinicId.ToString())
        }, "test"));
        using var services = new ServiceCollection().BuildServiceProvider();
        http.RequestServices = services;
        var resolver = new PrincipalRecordResolver(new HttpContextAccessor { HttpContext = http }, services);
        var invoked = false;
        await new SubscriptionAccessMiddleware(_ => { invoked = true; return Task.CompletedTask; }).InvokeAsync(http, resolver, fixture.Db, fixture.Clock, fixture.Configuration);
        Assert.Equal(allowed, invoked);
        Assert.Equal(allowed ? 200 : 403, http.Response.StatusCode);
        if (!allowed)
        {
            http.Response.Body.Position = 0;
            var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
            Assert.Contains(paid ? "individual_plan_limit" : "subscription_payment_required", body);
        }
    }

    [Fact]
    public async Task ConcurrentPaymentRequests_ObserveDurableMarkerBeforeAnotherRemoteCharge()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        fixture.Gateway.HoldCharge = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        await fixture.Gateway.ChargeEntered.Task;
        await using var secondDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(fixture.Connection).Options);
        var second = new SubscriptionService(secondDb,
            new UserRegistrationService(secondDb, NullLogger<UserRegistrationService>.Instance), fixture.Identity.Object,
            fixture.Gateway, fixture.Configuration, fixture.Clock);
        try
        {
            await Assert.ThrowsAsync<SubscriptionConflictException>(() => second.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "another", true)));
        }
        finally { fixture.Gateway.HoldCharge.TrySetResult(true); }
        Assert.True((await first).HasClinicalAccess);
        Assert.Equal(1, fixture.Gateway.Charges);
    }

    [Fact]
    public async Task ChangingGatewayEnvironment_DoesNotCarrySandboxEntitlementIntoProduction()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        await fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque", true));
        fixture.Configuration["Subscriptions:AuthorizeNet:Environment"] = "Production";
        Assert.False((await fixture.Service.GetAccountAsync())!.HasClinicalAccess);
        await Assert.ThrowsAsync<SubscriptionUnavailableException>(() => fixture.Service.CancelAsync());
    }

    [Fact]
    public async Task BillingOwnershipAndConsent_AreRequired()
    {
        await using var fixture = await Fixture.CreateAsync(subscriber: true);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.PayAsync(new("COMMON.ACCEPT.INAPP.PAYMENT", "opaque")));
        fixture.Actor = Guid.NewGuid();
        Assert.Null(await fixture.Service.GetAccountAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CancelAsync());
        Assert.Equal(0, fixture.Gateway.Charges);
    }

    private static IndividualSignupRequest Signup(string email) => new("Sample Clinician", email, new(1990, 1, 1), "12345678", "PT123", "NY", "Individual Clinic", "UTC");

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public Guid Actor { get; set; }
        public Mock<IIdentityContextAccessor> Identity { get; } = new();
        public TestClock Clock { get; } = new();
        public required FakeGateway Gateway { get; init; }
        public UserRegistrationService Registration { get; private set; } = null!;
        public SubscriptionService Service { get; private set; } = null!;
        public IConfiguration Configuration { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(bool subscriber = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture { Connection = connection, Db = db, Gateway = new FakeGateway() };
            fixture.Gateway.Clock = fixture.Clock;
            fixture.Identity.Setup(item => item.GetCurrentUserId()).Returns(() => fixture.Actor);
            fixture.Identity.Setup(item => item.TryGetCurrentUserId()).Returns(() => fixture.Actor == Guid.Empty ? null : fixture.Actor);
            fixture.Registration = new(db, NullLogger<UserRegistrationService>.Instance, fixture.Identity.Object);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:Enabled"] = "true",
                ["Subscriptions:Amount"] = "49",
                ["Subscriptions:IntervalMonths"] = "1",
                ["Subscriptions:AuthorizeNet:Environment"] = "Sandbox",
                ["Subscriptions:AuthorizeNet:ApiLoginId"] = "test-login",
                ["Subscriptions:AuthorizeNet:TransactionKey"] = "test-only",
                ["Subscriptions:AuthorizeNet:ClientKey"] = "test-client",
                ["Subscriptions:AuthorizeNet:SignatureKey"] = new string('A', 128)
            }).Build();
            fixture.Configuration = config;
            fixture.Service = new(db, fixture.Registration, fixture.Identity.Object, fixture.Gateway, config, fixture.Clock);
            if (subscriber) fixture.Actor = (await fixture.Service.RegisterAsync(Signup("subscriber@example.test"))).UserId!.Value;
            return fixture;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed class FakeGateway : ISubscriptionGateway
    {
        public TestClock Clock { get; set; } = null!;
        public Dictionary<string, SubscriptionTransaction> Transactions { get; } = new();
        public int Charges, RecurringCreates;
        public decimal ChargedAmount;
        public DateTime RecurringStart;
        public bool LoseChargeResponse;
        public string? PaymentCase, FailOperation;
        public TaskCompletionSource<bool> ChargeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? HoldCharge;
        public string Status = "active";
        public async Task<SubscriptionCharge> ChargeAsync(string invoice, decimal amount, string descriptor, string token, CancellationToken ct)
        {
            Charges++; ChargedAmount = amount;
            Transactions["101"] = new("101", PaymentCase == "wrong-invoice" ? "foreign" : invoice,
                PaymentCase == "wrong-amount" ? 1 : amount, Clock.Utc.UtcDateTime, PaymentCase != "held", null, 0);
            ChargeEntered.TrySetResult(true);
            if (HoldCharge is not null) await HoldCharge.Task.WaitAsync(ct);
            if (LoseChargeResponse) throw new HttpRequestException("Response lost");
            return new SubscriptionCharge("101", false);
        }
        public Task<SubscriptionTransaction> GetTransactionAsync(string transactionId, CancellationToken ct) => Task.FromResult(Transactions[transactionId]);
        public Task<SubscriptionProfile> CreateProfileAsync(string transactionId, CancellationToken ct)
        {
            if (FailOperation == "profile") throw new HttpRequestException("Response lost");
            return Task.FromResult(new SubscriptionProfile("700", "800"));
        }
        public Task<string> CreateRecurringAsync(string name, decimal amount, int intervalMonths, DateTime startUtc, SubscriptionProfile profile, CancellationToken ct)
        {
            RecurringCreates++; RecurringStart = startUtc;
            if (FailOperation == "recurring") throw new HttpRequestException("Response lost");
            return Task.FromResult("900");
        }
        public Task<RecurringSubscription> GetRecurringAsync(string subscriptionId, CancellationToken ct) => Task.FromResult(new RecurringSubscription(subscriptionId, Status));
        public Task CancelAsync(string subscriptionId, CancellationToken ct) { Status = "canceled"; return Task.CompletedTask; }
    }
}
