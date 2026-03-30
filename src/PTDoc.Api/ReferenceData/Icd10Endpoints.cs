using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Data;
using PTDoc.Application.Services;

namespace PTDoc.Api.ReferenceData;

/// <summary>
/// Endpoints for ICD-10 diagnosis code lookup.
/// Uses the bundled code list (no external API required for MVP).
/// </summary>
public static class Icd10Endpoints
{
    public static void MapIcd10Endpoints(this IEndpointRouteBuilder app)
    {
        // No auth required — code search is informational only (no PHI)
        var group = app.MapGroup("/api/v1/icd10")
            .WithTags("Reference Data");

        group.MapGet("/search", Search)
            .WithName("SearchIcd10Codes")
            .WithSummary("Search ICD-10 codes by code or description")
            .WithDescription("Returns up to 20 matching codes from the bundled PT-relevant ICD-10 list.");
    }

    private static IResult Search(
        [FromQuery] string? q,
        [FromQuery(Name = "maxResults")] int maxResults = 20,
        [FromServices] IIcd10Service icd10Service = null!)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.Ok(Array.Empty<Icd10Code>());

        var limit = maxResults <= 0 ? 20 : Math.Min(maxResults, 100);
        var results = icd10Service.Search(q, limit);
        return Results.Ok(results);
    }
}
