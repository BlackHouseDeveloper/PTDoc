using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;

namespace PTDoc.Api.ReferenceData;

public static class IntakeReferenceDataEndpoints
{
    public static void MapIntakeReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reference-data/intake")
            .WithTags("Reference Data")
            .RequireAuthorization(AuthorizationPolicies.IntakeRead);

        group.MapGet("/", GetCatalog)
            .WithName("GetIntakeReferenceCatalog")
            .WithSummary("Get intake body part, medication, and pain descriptor reference data");

        group.MapGet("/body-parts", GetBodyPartGroups)
            .WithName("GetIntakeBodyPartReferenceData")
            .WithSummary("Get intake body part reference data");

        group.MapGet("/medications", GetMedications)
            .WithName("GetIntakeMedicationReferenceData")
            .WithSummary("Get intake medication reference data");

        group.MapGet("/pain-descriptors", GetPainDescriptors)
            .WithName("GetIntakePainDescriptorReferenceData")
            .WithSummary("Get intake pain descriptor reference data");
    }

    private static IResult GetCatalog([FromServices] IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        return Results.Ok(intakeReferenceData.GetCatalog());
    }

    private static IResult GetBodyPartGroups([FromServices] IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        return Results.Ok(intakeReferenceData.GetBodyPartGroups());
    }

    private static IResult GetMedications([FromServices] IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        return Results.Ok(intakeReferenceData.GetMedications());
    }

    private static IResult GetPainDescriptors([FromServices] IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        return Results.Ok(intakeReferenceData.GetPainDescriptors());
    }
}
