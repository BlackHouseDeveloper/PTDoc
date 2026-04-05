using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class SqliteStartupInitializationTests
{
    [Fact]
    public async Task PlainSqlite_Startup_Allows_Liveness_Request_Without_TestAssemblyBootstrap()
    {
        using var factory = new PlainSqliteApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EncryptedSqlite_Startup_Resolves_DbContext_And_Opens_Connection()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"ptdoc-api-startup-{Guid.NewGuid():N}.db");
        const string encryptionKey = "startup-test-encryption-key-minimum-32-chars";

        using var factory = new EncryptedSqliteApiFactory(databasePath, encryptionKey);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var scope = factory.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.OpenConnectionAsync();

        var connection = context.Database.GetDbConnection();
        Assert.Equal(ConnectionState.Open, connection.State);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private sealed class PlainSqliteApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(CreateBaseSettings());
            });
        }
    }

    private sealed class EncryptedSqliteApiFactory : WebApplicationFactory<Program>
    {
        private readonly string databasePath;
        private readonly string encryptionKey;
        private readonly string? previousKey;

        public EncryptedSqliteApiFactory(string databasePath, string encryptionKey)
        {
            this.databasePath = databasePath;
            this.encryptionKey = encryptionKey;
            previousKey = Environment.GetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY");
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", encryptionKey);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = CreateBaseSettings();
                settings["Database:Encryption:Enabled"] = "true";
                settings["Database:Path"] = databasePath;
                config.AddInMemoryCollection(settings);
            });
        }

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("PTDOC_DB_ENCRYPTION_KEY", previousKey);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            base.Dispose(disposing);
        }
    }

    private static Dictionary<string, string?> CreateBaseSettings() =>
        new()
        {
            ["Jwt:SigningKey"] = "integration-test-signing-key-do-not-use-in-prod-min-64-chars!",
            ["Jwt:Issuer"] = "ptdoc-integration-tests",
            ["Jwt:Audience"] = "ptdoc-api-tests",
            ["IntakeInvite:SigningKey"] = "integration-test-intake-invite-key-do-not-use-in-prod-64-chars!",
            ["IntakeInvite:PublicWebBaseUrl"] = "http://localhost",
            ["IntakeInvite:InviteExpiryMinutes"] = "1440",
            ["AzureBlobStorage:ConnectionString"] = "UseDevelopmentStorage=true",
            ["AzureOpenAi:Endpoint"] = "https://test.openai.azure.com/",
            ["AzureOpenAi:ApiKey"] = "test-api-key-for-unit-tests-only",
            ["AzureOpenAi:Deployment"] = "test-deployment",
            ["Database:Provider"] = "Sqlite",
            ["Database:Encryption:Enabled"] = "false",
            ["Database:AutoMigrate"] = "false"
        };
}
