using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class InsurancePolicyMigrationTests
{
    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Postgres", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void PolicyLimits_RemainAfterUpgrade_AndOnlyTheirColumnsAreRemovedOnDowngrade(string assemblySuffix, string provider)
    {
        var assembly = Assembly.Load("PTDoc.Infrastructure.Migrations." + assemblySuffix);
        var type = assembly.GetType("PTDoc.Infrastructure.Data.Migrations.PolicyVisitLimits", throwOnError: true)!;
        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(type));
        migration.ActiveProvider = provider;
        var expected = new[] { "TotalVisitLimit", "VisitLimitType" };

        Assert.Equal(expected, migration.UpOperations.OfType<AddColumnOperation>().Select(value => value.Name).Order().ToArray());
        Assert.All(migration.UpOperations, operation =>
        {
            var added = Assert.IsType<AddColumnOperation>(operation);
            Assert.Equal("PatientInsurancePolicies", added.Table);
            Assert.True(added.IsNullable);
        });
        Assert.Equal(expected, migration.DownOperations.OfType<DropColumnOperation>().Select(value => value.Name).Order().ToArray());
        Assert.All(migration.DownOperations, operation => Assert.Equal("PatientInsurancePolicies", Assert.IsType<DropColumnOperation>(operation).Table));
    }
}
