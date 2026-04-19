using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Services;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class NoteWorkspaceCatalogEndpointIntegrationTests(PtDocApiFactory factory)
    : IClassFixture<PtDocApiFactory>
{
    [Fact]
    public async Task GetBodyRegionCatalog_InvalidEnumValue_ReturnsBadRequest()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/api/v2/notes/workspace/catalogs/body-regions/12");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.NotNull(payload);
        Assert.Equal("Unknown body part '12'.", payload["error"]);
    }
}
