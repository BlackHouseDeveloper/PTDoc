using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Settings;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsAdministrationApiClientTests
{
    [Theory]
    [InlineData(false, "/api/v1/appointments/visit-types")]
    [InlineData(true, "/api/v1/admin/scheduling/visit-types?includeInactive=true")]
    public async Task VisitTypes_UsesSchedulingAccessForActiveCatalog(bool includeInactive, string expectedPath)
    {
        using var handler = new CatalogHandler(expectedPath);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var client = new SettingsAdministrationApiClient(http);
        var result = await client.GetVisitTypesAsync(Guid.Empty, includeInactive);
        Assert.Equal("Custom visit", Assert.Single(result).Name);
    }

    private sealed class CatalogHandler(string expectedPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedPath, request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new VisitTypeDto(Guid.NewGuid(), "custom", "Custom visit", 45, false, true, true, true, 0, 1)
                })
            });
        }
    }
}
