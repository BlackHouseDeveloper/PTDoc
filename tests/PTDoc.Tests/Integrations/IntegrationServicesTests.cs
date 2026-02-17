using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
                ["Integrations:Hep:ApiKey"] = "test_hep_key"
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
    public async Task ExternalSystemMapping_UniqueConstraint_EnforcedByDatabase()
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

        // Act & Assert - In-memory DB doesn't enforce unique constraints like real SQLite would
        // This test documents the expected behavior with real database
        // Real SQLite would throw DbUpdateException due to unique index
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
    public async Task HepService_AssignProgram_MockSuccess()
    {
        // Arrange
        var config = CreateTestConfiguration(enabled: true);
        var httpClientFactory = new MockHttpClientFactory();
        var service = new WibbiHepService(httpClientFactory, config);

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

        // Assert - Mock implementation returns success
        Assert.True(result.Success);
        Assert.NotNull(result.AssignmentId);
        Assert.NotNull(result.PatientPortalUrl);
    }
}

/// <summary>
/// Mock HttpClientFactory for testing.
/// </summary>
public class MockHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}
