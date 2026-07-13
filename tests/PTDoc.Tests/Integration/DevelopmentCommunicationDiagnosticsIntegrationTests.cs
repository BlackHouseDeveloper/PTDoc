using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Communication;
using PTDoc.Application.Services;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class DevelopmentCommunicationDiagnosticsIntegrationTests
{
    [Fact]
    public async Task DevelopmentCommunicationDiagnostics_WhenDeveloperModeDisabled_Returns404()
    {
        using var env = CreateEnvironment(developerModeEnabled: false);
        var factory = new PtDocApiFactory();

        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);

            using var response = await client.GetAsync("/diagnostics/development/communications");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DevelopmentCommunicationDiagnostics_WhenAccessedByNonAdmin_ReturnsForbidden()
    {
        using var env = CreateEnvironment(developerModeEnabled: true);
        var factory = new PtDocApiFactory();

        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.PT);

            using var response = await client.GetAsync("/diagnostics/development/communications");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DevelopmentCommunicationDiagnostics_AdminCanReadCapturedOtpMessage()
    {
        using var env = CreateEnvironment(developerModeEnabled: true);
        var factory = new PtDocApiFactory();

        try
        {
            await factory.InitializeAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var store = scope.ServiceProvider.GetRequiredService<DevelopmentCommunicationMessageStore>();
                store.Clear();
                store.CaptureEmail(new EmailMessage
                {
                    ToAddress = "patient@example.com",
                    Subject = "Your PTDoc intake verification code",
                    PlainTextBody = "Your PTDoc intake verification code is 123456.",
                    HtmlBody = "<p>Your PTDoc intake verification code is 123456.</p>",
                    Purpose = DeliveryPurpose.IntakeOtp
                });
            }

            using var client = factory.CreateClientWithRole(Roles.Admin);
            using var response = await client.GetAsync(
                "/diagnostics/development/communications?purpose=IntakeOtp&channel=Email");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var message = Assert.Single(payload.RootElement.GetProperty("messages").EnumerateArray());

            Assert.Equal("Email", message.GetProperty("channel").GetString());
            Assert.Equal("IntakeOtp", message.GetProperty("purpose").GetString());
            Assert.Equal("patient@example.com", message.GetProperty("recipient").GetString());
            Assert.Equal("Your PTDoc intake verification code", message.GetProperty("subject").GetString());
            Assert.Contains("123456", message.GetProperty("plainTextBody").GetString(), StringComparison.Ordinal);
            Assert.Contains("123456", message.GetProperty("htmlBody").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task IntakeOtpDiagnostics_AdminReadsSanitizedRecentOutcomes()
    {
        using var env = CreateEnvironment(developerModeEnabled: false);
        var factory = new PtDocApiFactory();

        try
        {
            await factory.InitializeAsync();
            var intakeId = Guid.NewGuid();
            var requestId = Guid.NewGuid().ToString("N");
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.AuditLogs.Add(new AuditLog
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventType = "IntakeOtpDeliveryFailed",
                    Severity = "Error",
                    EntityType = "IntakeForm",
                    EntityId = intakeId,
                    CorrelationId = requestId,
                    Success = false,
                    ErrorMessage = "ProviderRejected",
                    MetadataJson =
                        """{"Channel":"Email","Provider":"AzureCommunicationServices","Outcome":"ProviderRejected","ErrorCode":"AcsRejected"}"""
                });
                await db.SaveChangesAsync();
            }

            using var client = factory.CreateClientWithRole(Roles.Admin);
            using var response = await client.GetAsync("/diagnostics/intake-otp?take=10");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var rawPayload = await response.Content.ReadAsStringAsync();
            using var payload = JsonDocument.Parse(rawPayload);
            var outcome = Assert.Single(
                payload.RootElement.GetProperty("outcomes").EnumerateArray(),
                item => item.GetProperty("requestId").GetString() == requestId);
            Assert.Equal(intakeId, outcome.GetProperty("intakeId").GetGuid());
            Assert.Equal(requestId, outcome.GetProperty("requestId").GetString());
            Assert.Equal("Email", outcome.GetProperty("channel").GetString());
            Assert.Equal("AzureCommunicationServices", outcome.GetProperty("provider").GetString());
            Assert.Equal("ProviderRejected", outcome.GetProperty("outcome").GetString());
            Assert.Equal("AcsRejected", outcome.GetProperty("errorCode").GetString());
            Assert.DoesNotContain("recipient", rawPayload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("otpCode", rawPayload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task IntakeOtpDiagnostics_InvalidTakeReturnsNormalizedBadRequest()
    {
        using var env = CreateEnvironment(developerModeEnabled: false);
        var factory = new PtDocApiFactory();

        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);
            using var response = await client.GetAsync("/diagnostics/intake-otp?take=not-a-number");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("bad_request", payload.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("correlationId").GetString()));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static EnvironmentVariableScope CreateEnvironment(bool developerModeEnabled)
    {
        return new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_DEVELOPER_MODE"] = developerModeEnabled ? "true" : "false",
            ["FeatureFlags__EnableAiGeneration"] = "false"
        });
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previousValue) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }
}
