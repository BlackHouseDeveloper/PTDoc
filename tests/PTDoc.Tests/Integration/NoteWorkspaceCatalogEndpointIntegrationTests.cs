using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Notes.Workspace;
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

    [Fact]
    public async Task GetInterventionLibraryCatalog_ClinicalStaff_ReturnsVersionedSourceBackedCatalog()
    {
        using var client = factory.CreateClientWithRole(Roles.PT);

        using var response = await client.GetAsync("/api/v2/notes/workspace/catalogs/interventions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = await response.Content.ReadFromJsonAsync<InterventionLibraryCatalog>();
        Assert.NotNull(catalog);
        Assert.False(string.IsNullOrWhiteSpace(catalog.Version));
        Assert.NotNull(catalog.Provenance);
        Assert.False(string.IsNullOrWhiteSpace(catalog.Provenance.DocumentPath));
        Assert.Contains(catalog.Items, item => item.Kind == InterventionKind.Exercise);
        Assert.Contains(catalog.Items, item => item.Kind == InterventionKind.ManualTechnique);
    }
}
