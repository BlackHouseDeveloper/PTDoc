using System.Text.Json;
using PTDoc.Application.Insurance;
using PTDoc.Tests.Integrations;
using PTDoc.UI.Services;

namespace PTDoc.Tests.Insurance;

[Trait("Category", "CoreCi")]
public sealed class InsurancePolicyApiServiceTests
{
    [Fact]
    public async Task ListAsync_DefaultRequestDoesNotIncludeArchivedQuery()
    {
        var patientId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/patients/{patientId}/insurance-policies", request.RequestUri!.AbsolutePath);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(Array.Empty<InsurancePolicyDto>()));
        });
        var service = new InsurancePolicyApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await service.ListAsync(patientId, CancellationToken.None);
    }

    [Fact]
    public async Task ListAsync_ExplicitFalseDoesNotIncludeArchivedQuery()
    {
        var patientId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/patients/{patientId}/insurance-policies", request.RequestUri!.AbsolutePath);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(Array.Empty<InsurancePolicyDto>()));
        });
        var service = new InsurancePolicyApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await service.ListAsync(patientId, includeArchived: false, ct: CancellationToken.None);
    }

    [Fact]
    public async Task ListAsync_RequestsArchivedPoliciesOnlyWhenExplicitlyIncluded()
    {
        var patientId = Guid.NewGuid();
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/patients/{patientId}/insurance-policies", request.RequestUri!.AbsolutePath);
            Assert.Equal("?includeArchived=true", request.RequestUri.Query);
            return StubHttpMessageHandler.JsonResponse(JsonSerializer.Serialize(Array.Empty<InsurancePolicyDto>()));
        });
        var service = new InsurancePolicyApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        await service.ListAsync(patientId, includeArchived: true, ct: CancellationToken.None);
    }
}
