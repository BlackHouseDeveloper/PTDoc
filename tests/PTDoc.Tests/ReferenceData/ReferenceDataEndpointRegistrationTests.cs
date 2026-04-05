using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Api.ReferenceData;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "CoreCi")]
public class ReferenceDataEndpointRegistrationTests
{
    [Fact]
    public void MapTreatmentTaxonomyEndpoints_RegistersCatalogRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();

        app.MapTreatmentTaxonomyEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/api/v1/reference-data/treatment-taxonomy/", routes);
        Assert.Contains("/api/v1/reference-data/treatment-taxonomy/{categoryId}", routes);
    }

    [Fact]
    public void MapIntakeReferenceDataEndpoints_RegistersCatalogRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();

        app.MapIntakeReferenceDataEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/api/v1/reference-data/intake/", routes);
        Assert.Contains("/api/v1/reference-data/intake/body-parts", routes);
        Assert.Contains("/api/v1/reference-data/intake/medications", routes);
        Assert.Contains("/api/v1/reference-data/intake/pain-descriptors", routes);
    }
}
