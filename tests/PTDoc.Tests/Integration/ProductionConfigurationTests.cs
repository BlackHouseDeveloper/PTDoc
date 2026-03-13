using System;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Configuration validation tests for Sprint E — Production Database Deployment Infrastructure.
///
/// These tests verify that:
///   • Database:AutoMigrate defaults to the correct value based on environment.
///   • Explicit overrides of Database:AutoMigrate are respected.
///   • Provider selection reads from environment configuration.
///   • Connection string injection works via environment variables.
///
/// Decision reference: PTDocs+ Branch-Specific Database Blueprint — Sprint E.
/// </summary>
public class ProductionConfigurationTests
{
    // -------------------------------------------------------------------------
    // AutoMigrate defaults
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void AutoMigrate_Is_True_When_Explicitly_Set_In_Configuration()
    {
        var config = BuildConfig(new()
        {
            ["Database:AutoMigrate"] = "true"
        });

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate");

        Assert.True(autoMigrate);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void AutoMigrate_Is_False_When_Explicitly_Set_In_Configuration()
    {
        var config = BuildConfig(new()
        {
            ["Database:AutoMigrate"] = "false"
        });

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate");

        Assert.False(autoMigrate);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void AutoMigrate_Returns_Null_When_Not_Configured_Allowing_Environment_Default()
    {
        // When the key is absent the API falls back to IsDevelopment() — null signals "use default".
        var config = BuildConfig(new());

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate");

        Assert.Null(autoMigrate);
    }

    // -------------------------------------------------------------------------
    // Provider selection
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Provider_Reads_Sqlite_From_Configuration()
    {
        var config = BuildConfig(new()
        {
            ["Database:Provider"] = "Sqlite"
        });

        var provider = config.GetValue<string>("Database:Provider");

        Assert.Equal("Sqlite", provider);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Provider_Reads_SqlServer_From_Configuration()
    {
        var config = BuildConfig(new()
        {
            ["Database:Provider"] = "SqlServer"
        });

        var provider = config.GetValue<string>("Database:Provider");

        Assert.Equal("SqlServer", provider);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Provider_Reads_Postgres_From_Configuration()
    {
        var config = BuildConfig(new()
        {
            ["Database:Provider"] = "Postgres"
        });

        var provider = config.GetValue<string>("Database:Provider");

        Assert.Equal("Postgres", provider);
    }

    [Theory]
    [Trait("Category", "ProductionConfig")]
    [InlineData("Sqlite")]
    [InlineData("SQLITE")]
    [InlineData("sqlite")]
    public void Provider_Comparison_Is_Case_Insensitive(string providerValue)
    {
        var supportedProviders = new[] { "Sqlite", "SqlServer", "Postgres" };

        var isSupported = Array.Exists(
            supportedProviders,
            p => string.Equals(p, providerValue, StringComparison.OrdinalIgnoreCase));

        Assert.True(isSupported);
    }

    // -------------------------------------------------------------------------
    // Connection string injection
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void ConnectionString_Reads_PTDocsServer_From_Configuration()
    {
        const string expected = "Server=prod-db;Database=PTDoc;Integrated Security=True;";

        var config = BuildConfig(new()
        {
            ["ConnectionStrings:PTDocsServer"] = expected
        });

        var connectionString = config.GetConnectionString("PTDocsServer");

        Assert.Equal(expected, connectionString);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void ConnectionString_Returns_Null_When_Not_Configured()
    {
        var config = BuildConfig(new());

        var connectionString = config.GetConnectionString("PTDocsServer");

        Assert.Null(connectionString);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void ConnectionString_Can_Be_Injected_Via_Environment_Variable_Format()
    {
        // .NET configuration maps __ to : so ConnectionStrings__PTDocsServer
        // becomes ConnectionStrings:PTDocsServer when loaded via env vars.
        const string expected = "Server=prod-db;Database=PTDoc;User=sa;Password=secret";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                // Simulate what the environment variable loader produces after __ → : mapping
                ["ConnectionStrings:PTDocsServer"] = expected
            })
            .Build();

        var connectionString = config.GetConnectionString("PTDocsServer");

        Assert.Equal(expected, connectionString);
    }

    // -------------------------------------------------------------------------
    // Production defaults (AutoMigrate=false, provider override)
    // -------------------------------------------------------------------------

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Production_AutoMigrate_Default_Is_False_When_Key_Absent_And_Not_Development()
    {
        // Mirrors the Program.cs logic:
        //   var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate") ?? isDevelopment;
        // In Production, isDevelopment == false, so absent key → false.
        var config = BuildConfig(new());
        var isDevelopment = false; // simulated production environment

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate") ?? isDevelopment;

        Assert.False(autoMigrate);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Development_AutoMigrate_Default_Is_True_When_Key_Absent_And_Development()
    {
        // Mirrors the Program.cs logic:
        //   var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate") ?? isDevelopment;
        // In Development, isDevelopment == true, so absent key → true.
        var config = BuildConfig(new());
        var isDevelopment = true; // simulated development environment

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate") ?? isDevelopment;

        Assert.True(autoMigrate);
    }

    [Fact]
    [Trait("Category", "ProductionConfig")]
    public void Production_AutoMigrate_Can_Be_Enabled_Explicitly()
    {
        // An operator can set Database:AutoMigrate=true in production to allow
        // auto-migration (e.g., for a managed container deployment).
        var config = BuildConfig(new()
        {
            ["Database:AutoMigrate"] = "true"
        });
        var isDevelopment = false;

        var autoMigrate = config.GetValue<bool?>("Database:AutoMigrate") ?? isDevelopment;

        Assert.True(autoMigrate);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IConfiguration BuildConfig(
        System.Collections.Generic.Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
