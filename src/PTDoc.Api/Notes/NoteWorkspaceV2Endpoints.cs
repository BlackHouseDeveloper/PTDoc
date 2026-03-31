using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Api.Notes;

public static class NoteWorkspaceV2Endpoints
{
    public static WebApplication MapNoteWorkspaceV2Endpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v2/notes/workspace")
            .WithTags("Note Workspace V2");

        group.MapGet("/{patientId:guid}/{noteId:guid}", LoadWorkspace)
            .RequireAuthorization(AuthorizationPolicies.NoteRead)
            .WithName("LoadNoteWorkspaceV2")
            .WithSummary("Load a typed v2 eval/reeval/progress workspace snapshot");

        group.MapPost("/", SaveWorkspace)
            .RequireAuthorization(AuthorizationPolicies.NoteWrite)
            .WithName("SaveNoteWorkspaceV2")
            .WithSummary("Save a typed v2 eval/reeval/progress workspace snapshot");

        group.MapGet("/catalogs/body-regions/{bodyPart}", GetBodyRegionCatalog)
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("GetNoteWorkspaceV2BodyRegionCatalog")
            .WithSummary("Get source-backed reference data for a body region");

        group.MapGet("/lookup/icd10", SearchIcd10)
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("SearchNoteWorkspaceV2Icd10")
            .WithSummary("Search source-backed ICD-10 reference data");

        group.MapGet("/lookup/cpt", SearchCpt)
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("SearchNoteWorkspaceV2Cpt")
            .WithSummary("Search CPT reference data available to the v2 workspace");

        return app;
    }

    private static async Task<IResult> LoadWorkspace(
        Guid patientId,
        Guid noteId,
        INoteWorkspaceV2Service service,
        CancellationToken cancellationToken)
    {
        var workspace = await service.LoadAsync(patientId, noteId, cancellationToken);
        return workspace is null
            ? Results.NotFound(new { error = $"Workspace note {noteId} was not found for patient {patientId}." })
            : Results.Ok(workspace);
    }

    private static async Task<IResult> SaveWorkspace(
        [FromBody] NoteWorkspaceV2SaveRequest request,
        INoteWorkspaceV2Service service,
        CancellationToken cancellationToken)
    {
        var validationErrors = new Dictionary<string, string[]>();

        if (request.PatientId == Guid.Empty)
        {
            validationErrors[nameof(request.PatientId)] = ["PatientId is required."];
        }

        if (request.DateOfService == default)
        {
            validationErrors[nameof(request.DateOfService)] = ["DateOfService is required."];
        }

        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        try
        {
            var saved = await service.SaveAsync(request, cancellationToken);
            return Results.Ok(saved);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static IResult GetBodyRegionCatalog(
        BodyPart bodyPart,
        IWorkspaceReferenceCatalogService catalogs)
    {
        return Results.Ok(catalogs.GetBodyRegionCatalog(bodyPart));
    }

    private static IResult SearchIcd10(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int take,
        IWorkspaceReferenceCatalogService catalogs)
    {
        return Results.Ok(catalogs.SearchIcd10(query, take));
    }

    private static IResult SearchCpt(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int take,
        IWorkspaceReferenceCatalogService catalogs)
    {
        return Results.Ok(catalogs.SearchCpt(query, take));
    }
}
