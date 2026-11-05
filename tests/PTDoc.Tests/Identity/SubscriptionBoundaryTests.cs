using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Api.Identity;
using PTDoc.Application.Identity;
using PTDoc.Application.LocalData;
using PTDoc.Infrastructure.Data;
using PTDoc.Tests.Integration;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public sealed class SubscriptionBoundaryTests
{
    [Fact]
    public void WebhookSignature_RequiresExactBodyAndMerchantKey()
    {
        const string payload = "{\"eventType\":\"net.authorize.payment.authcapture.created\",\"payload\":{\"id\":\"123\"}}";
        var key = new string('A', 128);
        var body = Encoding.UTF8.GetBytes(payload);
        var signature = "sha512=" + Convert.ToHexString(HMACSHA512.HashData(Convert.FromHexString(key), body));
        Assert.True(SubscriptionEndpoints.VerifySignature(body, signature, key));
        Assert.False(SubscriptionEndpoints.VerifySignature(Encoding.UTF8.GetBytes(payload + " "), signature, key));
        Assert.False(SubscriptionEndpoints.VerifySignature(body, signature, new string('B', 128)));
        Assert.False(SubscriptionEndpoints.VerifySignature(body, "sha512=invalid", key));
        Assert.False(SubscriptionEndpoints.VerifySignature(body, signature, null));
    }

    [Fact]
    public async Task NativePaidDeadline_BlocksCacheAtExpiry_WithoutDestroyingScopeOrAuthentication()
    {
        var clock = new TestClock();
        var scope = new LocalDataScope(clock);
        var key = Guid.NewGuid().ToString("N") + ":" + Guid.NewGuid().ToString("N");
        await scope.ChangeAsync(key, () => { scope.SetClinicalAccessUntil(clock.Utc.AddHours(1)); return Task.CompletedTask; });
        using (await scope.EnterAsync()) Assert.Equal(key, scope.Key);
        clock.Utc = clock.Utc.AddHours(1);
        Assert.Null(scope.Key);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.EnterAsync());
        // Billing authentication is still available; renewal restores the original partition.
        using (await scope.EnterAuthenticationAsync()) scope.SetClinicalAccessUntil(clock.Utc.AddMonths(1));
        using (await scope.EnterAsync()) Assert.Equal(key, scope.Key);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("malformed", true)]
    [InlineData("2026-11-03T12:00:00.0000000+00:00", false)]
    public void PaidThroughClaim_MalformedValuesFailClosed_ExistingAccountsAreUnchanged(string? value, bool malformed)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(value is null ? [] : [new Claim(PTDocClaimTypes.SubscriptionPaidThrough, value)]));
        var deadline = SubscriptionClaims.PaidThrough(principal);
        if (value is null) Assert.Null(deadline);
        else if (malformed) Assert.Equal(DateTimeOffset.MinValue, deadline);
        else Assert.Equal(DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), deadline);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("Postgres")]
    public void MigrationMetadata_PreservesExistingEntities_AndRecordsUniquePaymentOwnership(string provider)
    {
        var assembly = Assembly.Load($"PTDoc.Infrastructure.Migrations.{provider}");
        var migration = (Migration)Activator.CreateInstance(assembly.GetType("PTDoc.Infrastructure.Data.Migrations.AddIndividualSubscriptions", true)!)!;
        var snapshot = (ModelSnapshot)Activator.CreateInstance(assembly.GetType("PTDoc.Infrastructure.Data.Migrations.ApplicationDbContextModelSnapshot", true)!, true)!;
        foreach (var model in new[] { migration.TargetModel, snapshot.Model })
        {
            Assert.NotNull(model.FindEntityType("PTDoc.Core.Models.Organization"));
            Assert.NotNull(model.FindEntityType("PTDoc.Core.Models.IntegrationConnection"));
            Assert.NotNull(model.FindEntityType("PTDoc.Core.Models.ClinicalNote"));
            var subscription = model.FindEntityType("PTDoc.Core.Models.IndividualSubscription")!;
            Assert.True(subscription.FindProperty("Version")!.IsConcurrencyToken);
            Assert.Contains(subscription.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == "UserId");
            var payment = model.FindEntityType("PTDoc.Core.Models.SubscriptionPayment")!;
            Assert.Equal("TransactionId", payment.FindPrimaryKey()!.Properties.Single().Name);
        }
        Assert.Contains(migration.DownOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(), operation => operation.Sql.Contains("IndividualSubscriptions"));
    }

    [Fact]
    public void SqlServerRollback_GeneratesInitializedCommandsInDependencyOrder()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=unused;Integrated Security=True;TrustServerCertificate=True",
                options => options.MigrationsAssembly("PTDoc.Infrastructure.Migrations.SqlServer")).Options);
        var applied = db.GetService<IMigrationsAssembly>().Migrations.Keys;
        var commands = DatabaseProviderSmokeTests.GenerateOrganizationRollbackCommands(db, applied);
        Assert.NotEmpty(commands);
        Assert.All(commands, command => Assert.False(command.TransactionSuppressed));
        var sql = string.Join("\n", commands.Select(command => command.CommandText));
        var paymentsDrop = sql.IndexOf("DROP TABLE [SubscriptionPayments]", StringComparison.Ordinal);
        var subscriptionsDrop = sql.IndexOf("DROP TABLE [IndividualSubscriptions]", StringComparison.Ordinal);
        var organizationsDrop = sql.IndexOf("DROP TABLE [Organizations]", StringComparison.Ordinal);
        Assert.True(paymentsDrop >= 0 && subscriptionsDrop > paymentsDrop && organizationsDrop > subscriptionsDrop);
        Assert.Contains("Reconcile subscriptions", sql);
        Assert.Contains("Reconcile organization memberships", sql);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
