using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Compliance;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;

namespace PTDoc.Api.ReferenceData;

public static class TreatmentTaxonomyEndpoints
{
    public static void MapTreatmentTaxonomyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reference-data/treatment-taxonomy")
            .WithTags("Reference Data")
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff);

        group.MapGet("/", GetCatalog)
            .WithName("GetTreatmentTaxonomyCatalog")
            .WithSummary("Get the structured PT treatment taxonomy catalog");

        group.MapGet("/{categoryId}", GetCategory)
            .WithName("GetTreatmentTaxonomyCategory")
            .WithSummary("Get a single PT treatment taxonomy category by id");
    }

    private static IResult GetCatalog([FromServices] ITreatmentTaxonomyCatalogService taxonomyCatalog)
    {
        return Results.Ok(taxonomyCatalog.GetCatalog());
    }

    private static IResult GetCategory(string categoryId, [FromServices] ITreatmentTaxonomyCatalogService taxonomyCatalog)
    {
        var category = taxonomyCatalog.GetCategory(categoryId);
        return category is null ? Results.NotFound() : Results.Ok(category);
    }
}
