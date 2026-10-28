using System.Net;
using System.Net.Http.Json;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Settings;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsAdministrationApiClientTests
{
    [Fact]
    public async Task VisitTypes_UsesMauiAuthenticatedHandlerForCatalogRequests()
    {
        var tokenStore = new Mock<ITokenStore>();
        tokenStore.Setup(store => store.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenResponse("catalog-access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10)));
        using var handler = new AuthenticatedHttpMessageHandler(tokenStore.Object, Mock.Of<ITokenService>(), Mock.Of<IUserService>())
        {
            InnerHandler = new CatalogHandler("/api/v1/appointments/visit-types", "catalog-access")
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        ISchedulingAdministrationService client = new SettingsAdministrationApiClient(http);

        Assert.Equal("Custom visit", Assert.Single(await client.GetVisitTypesAsync(Guid.Empty, false)).Name);
    }

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

    private sealed class CatalogHandler(string expectedPath, string? expectedToken = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(expectedPath, request.RequestUri!.PathAndQuery);
            if (expectedToken is not null)
            {
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(expectedToken, request.Headers.Authorization?.Parameter);
            }
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
