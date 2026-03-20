using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Integrations;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Integrations;
using PTDoc.Integrations.Services;
using Xunit;

namespace PTDoc.Tests.Integrations;

public class IntegrationServicesTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private IConfiguration CreateTestConfiguration(bool enabled = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Payments:Enabled"] = enabled.ToString(),
                ["Integrations:Payments:ApiLoginId"] = "test_login",
                ["Integrations:Payments:TransactionKey"] = "test_key",
                ["Integrations:Fax:Enabled"] = enabled.ToString(),
                ["Integrations:Fax:ApiKey"] = "test_fax_key",
                ["Integrations:Hep:Enabled"] = enabled.ToString(),
                ["Integrations:Hep:PatientLaunchEnabled"] = enabled.ToString(),
                ["Integrations:Hep:ClinicianAssignmentEnabled"] = "false",
                ["Integrations:Hep:BaseUrl"] = "https://v4.api.wibbi.com",
                ["Integrations:Hep:ApiUsername"] = "license-admin",
                ["Integrations:Hep:ApiPassword"] = "super-secret",
                ["Integrations:Hep:Entity"] = "entity-123",
                ["Integrations:Hep:ClinicLicenseId"] = "clm-123",
                ["Integrations:Hep:AllowCredentialBearingRedirects"] = "false",
                ["Integrations:Hep:TokenRefreshSkew"] = "00:01:00"
            })
            .Build();
        return config;
    }

    [Fact]
    public async Task ExternalSystemMapping_GetOrCreate_CreatesNewMapping()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var service = new ExternalSystemMappingService(context);
        var patientId = Guid.NewGuid();

        // Act
        var result = await service.GetOrCreateMappingAsync(
            patientId,
            "Wibbi",
            "wibbi-123");

        // Assert
        Assert.True(result.IsNewMapping);
        Assert.Equal("Wibbi", result.ExternalSystemName);
        Assert.Equal("wibbi-123", result.ExternalId);
        Assert.Equal(patientId, result.InternalPatientId);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task ExternalSystemMapping_GetOrCreate_ReusesExisting()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var service = new ExternalSystemMappingService(context);
        var patientId1 = Guid.NewGuid();
        var patientId2 = Guid.NewGuid();

        // Create initial mapping
        var first = await service.GetOrCreateMappingAsync(patientId1, "Wibbi", "wibbi-123");

        // Act - Try to create mapping with same external ID but different patient
        var second = await service.GetOrCreateMappingAsync(patientId2, "Wibbi", "wibbi-123");

        // Assert - Should reuse existing mapping (prevents duplicate external patient creation)
        Assert.False(second.IsNewMapping);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(patientId1, second.InternalPatientId); // Original patient ID preserved
    }

    [Fact]
    public async Task ExternalSystemMapping_UniqueConstraint_DocumentedBehavior()
    {
        // Arrange
        await using var context = CreateInMemoryContext();
        var patientId = Guid.NewGuid();

        var mapping1 = new PTDoc.Core.Models.ExternalSystemMapping
        {
            ExternalSystemName = "Wibbi",
            ExternalId = "wibbi-123",
            InternalPatientId = patientId,
            CreatedAt = DateTime.UtcNow
        };

        var mapping2 = new PTDoc.Core.Models.ExternalSystemMapping
        {
            ExternalSystemName = "Wibbi",
            ExternalId = "wibbi-123", // Duplicate
            InternalPatientId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        context.ExternalSystemMappings.Add(mapping1);
        await context.SaveChangesAsync();

        context.ExternalSystemMappings.Add(mapping2);

        // Act & Assert
        // In-memory DB doesn't enforce unique constraints like real SQLite would
        // This test documents that behavior - duplicates are allowed in InMemory but would fail in SQLite
        await context.SaveChangesAsync(); // This succeeds in InMemory
        var count = await context.ExternalSystemMappings
            .CountAsync(m => m.ExternalSystemName == "Wibbi" && m.ExternalId == "wibbi-123");

        // Document the difference: InMemory allows duplicates (count == 2)
        // Real SQLite would throw DbUpdateException on the second SaveChangesAsync
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PaymentService_ProcessPayment_RequiresEnabled()
    {
        // Arrange
        var config = CreateTestConfiguration(enabled: false);
        var httpClientFactory = new MockHttpClientFactory();
        var service = new AuthorizeNetPaymentService(httpClientFactory, config);

        var request = new PaymentRequest
        {
            OpaqueDataToken = "token123",
            Amount = 50.00m,
            PatientId = Guid.NewGuid()
        };

        // Act
        var result = await service.ProcessPaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("DISABLED", result.ErrorCode);
    }

    [Fact]
    public async Task PaymentService_ProcessPayment_MockSuccess()
    {
        // Arrange
        var config = CreateTestConfiguration(enabled: true);
        var httpClientFactory = new MockHttpClientFactory();
        var service = new AuthorizeNetPaymentService(httpClientFactory, config);

        var request = new PaymentRequest
        {
            OpaqueDataToken = "token123",
            OpaqueDataDescriptor = "COMMON.ACCEPT.INAPP.PAYMENT",
            Amount = 50.00m,
            PatientId = Guid.NewGuid()
        };

        // Act
        var result = await service.ProcessPaymentAsync(request);

        // Assert - Mock implementation returns success
        Assert.True(result.Success);
        Assert.NotNull(result.TransactionId);
        Assert.StartsWith("MOCK-TXN-", result.TransactionId);
        Assert.Equal(50.00m, result.Amount);
    }

    [Fact]
    public async Task FaxService_SendFax_RequiresEnabled()
    {
        // Arrange
        var config = CreateTestConfiguration(enabled: false);
        var httpClientFactory = new MockHttpClientFactory();
        var service = new HumbleFaxService(httpClientFactory, config);

        var request = new FaxRequest
        {
            RecipientNumber = "+1-555-555-5555",
            PdfContent = new byte[] { 1, 2, 3 },
            PatientId = Guid.NewGuid(),
            DocumentType = "Progress Note"
        };

        // Act
        var result = await service.SendFaxAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("disabled", result.ErrorMessage);
    }

    [Fact]
    public async Task HepService_AssignProgram_IsDisabledForClinicianWorkflow()
    {
        // Arrange
        var config = CreateTestConfiguration(enabled: true);
        var httpClientFactory = new MockHttpClientFactory();
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var request = new HepAssignmentRequest
        {
            PatientId = Guid.NewGuid(),
            PatientEmail = "test@example.com",
            PatientFirstName = "John",
            PatientLastName = "Doe",
            ProgramId = "wibbi-program-1"
        };

        // Act
        var result = await service.AssignProgramAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not enabled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_ReturnsBrokeredLaunchUrl()
    {
        // Arrange
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);

        var loginCalls = 0;
        var launchCalls = 0;
        var authSchemes = new List<string?>();
        var authParameters = new List<string?>();
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/api/v4/authentication/login", StringComparison.Ordinal))
            {
                loginCalls++;
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "token": "wibbi-api-token",
                  "expires": "2030-01-01T00:00:00+00:00"
                }
                """);
            }

            if (request.RequestUri.AbsoluteUri.Contains("action=GetClientLink", StringComparison.Ordinal))
            {
                launchCalls++;
                authSchemes.Add(request.Headers.Authorization?.Scheme);
                authParameters.Add(request.Headers.Authorization?.Parameter);

                return StubHttpMessageHandler.JsonResponse("""
                {
                  "URL": "https://hep.wibbi.com/session/launch/abc123"
                }
                """);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        // Act
        var first = await service.GetPatientProgramAsync(patientId);
        var second = await service.GetPatientProgramAsync(patientId);

        // Assert
        Assert.Equal(1, loginCalls);
        Assert.Equal(2, launchCalls);
        Assert.All(authSchemes, scheme => Assert.Equal("Bearer", scheme));
        Assert.All(authParameters, parameter => Assert.Equal("wibbi-api-token", parameter));
        Assert.True(first.Success, first.ErrorMessage);
        Assert.Equal("external-client-123", first.AssignmentId);
        Assert.Equal("https://hep.wibbi.com/session/launch/abc123", first.PatientPortalUrl);
        Assert.True(second.Success, second.ErrorMessage);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_WhenUpstreamAuthFails_ThrowsTypedException()
    {
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var exception = await Assert.ThrowsAsync<WibbiAuthenticationException>(() => service.GetPatientProgramAsync(patientId));

        Assert.Equal("authenticate", exception.Operation);
        Assert.Equal((int)HttpStatusCode.Unauthorized, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_WhenAuthTransportFails_ThrowsTypedException()
    {
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom", null, HttpStatusCode.BadGateway));
        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var exception = await Assert.ThrowsAsync<WibbiAuthenticationException>(() => service.GetPatientProgramAsync(patientId));

        Assert.Equal("authenticate", exception.Operation);
        Assert.Equal((int)HttpStatusCode.BadGateway, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_WhenAuthResponseMalformed_ThrowsTypedException()
    {
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.JsonResponse("""
        {
          "token": "wibbi-api-token"
        }
        """));
        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var exception = await Assert.ThrowsAsync<WibbiAuthenticationException>(() => service.GetPatientProgramAsync(patientId));

        Assert.Equal("authenticate", exception.Operation);
        Assert.Equal((int)HttpStatusCode.OK, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_WhenLaunchResponseMalformed_ThrowsTypedException()
    {
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/api/v4/authentication/login", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "token": "wibbi-api-token",
                  "expires": "2030-01-01T00:00:00+00:00"
                }
                """);
            }

            return StubHttpMessageHandler.JsonResponse("""
            {"URL":
            """);
        });

        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var exception = await Assert.ThrowsAsync<WibbiAuthenticationException>(() => service.GetPatientProgramAsync(patientId));

        Assert.Equal("patient_launch", exception.Operation);
        Assert.Equal((int)HttpStatusCode.OK, exception.UpstreamStatusCode);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_RejectsCredentialBearingLaunchUrls_ByDefault()
    {
        var patientId = Guid.NewGuid();
        var config = CreateTestConfiguration(enabled: true);

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/api/v4/authentication/login", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "token": "wibbi-api-token",
                  "expires": "2030-01-01T00:00:00+00:00"
                }
                """);
            }

            return StubHttpMessageHandler.JsonResponse("""
            {
              "URL": "https://hep.wibbi.com?do=patient&action=new_load&username=example&password=example"
            }
            """);
        });

        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var exception = await Assert.ThrowsAsync<WibbiUnsafeLaunchUrlException>(() => service.GetPatientProgramAsync(patientId));

        Assert.Equal("patient_launch", exception.Operation);
        Assert.Contains("username", exception.BlockedParameters);
        Assert.Contains("password", exception.BlockedParameters);
    }

    [Fact]
    public async Task HepService_GetPatientProgram_AllowsCredentialBearingLaunchUrls_WhenExplicitlyEnabled()
    {
        var patientId = Guid.NewGuid();
        var config = new ConfigurationBuilder()
            .AddConfiguration(CreateTestConfiguration(enabled: true))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Hep:AllowCredentialBearingRedirects"] = "true"
            })
            .Build();

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri.EndsWith("/api/v4/authentication/login", StringComparison.Ordinal))
            {
                return StubHttpMessageHandler.JsonResponse("""
                {
                  "token": "wibbi-api-token",
                  "expires": "2030-01-01T00:00:00+00:00"
                }
                """);
            }

            return StubHttpMessageHandler.JsonResponse("""
            {
              "URL": "https://hep.wibbi.com?do=patient&action=new_load&username=example&password=example"
            }
            """);
        });

        var httpClientFactory = new MockHttpClientFactory(new HttpClient(handler));
        await using var context = CreateInMemoryContext();
        var mappingService = new ExternalSystemMappingService(context);
        await mappingService.GetOrCreateMappingAsync(patientId, "Wibbi", "external-client-123");
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = new WibbiHepService(httpClientFactory, config, mappingService, memoryCache, NullLogger<WibbiHepService>.Instance);

        var result = await service.GetPatientProgramAsync(patientId);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("https://hep.wibbi.com?do=patient&action=new_load&username=example&password=example", result.PatientPortalUrl);
    }
}

/// <summary>
/// Mock HttpClientFactory for testing.
/// </summary>
public class MockHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;

    public MockHttpClientFactory()
        : this(new HttpClient())
    {
    }

    public MockHttpClientFactory(HttpClient client)
    {
        _client = client;
    }

    public HttpClient CreateClient(string name)
    {
        return _client;
    }
}

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responder(request));
    }

    public static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
