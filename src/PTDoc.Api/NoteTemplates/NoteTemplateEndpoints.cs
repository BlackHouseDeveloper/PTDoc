using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.NoteTemplates;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Api.NoteTemplates;

public static class NoteTemplateEndpoints
{
    public static void MapNoteTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/note-templates").WithTags("Note Template Administration").RequireAuthorization(AuthorizationPolicies.NoteTemplateDraftManage);
        admin.MapGet("/", async ([FromQuery] NoteType? noteType, [FromQuery] NoteTemplateVersionStatus? status, INoteTemplateAdministrationService service, CancellationToken ct) => Results.Ok(await service.ListAsync(noteType, status, ct)));
        admin.MapGet("/versions/{versionId:guid}", async (Guid versionId, INoteTemplateAdministrationService service, CancellationToken ct) => await service.GetVersionAsync(versionId, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        admin.MapPost("/drafts", async (CreateNoteTemplateDraftRequest request, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.CreateDraftAsync(request, ct), true));
        admin.MapPost("/validate", async ([FromQuery] NoteType noteType, [FromQuery] NoteTemplateVariant variant, NoteTemplateSchemaDefinition schema, INoteTemplateAdministrationService service, CancellationToken ct) => Results.Ok(await service.ValidateAsync(noteType, variant, schema, ct)));
        admin.MapPut("/versions/{versionId:guid}", async (Guid versionId, UpdateNoteTemplateDraftRequest request, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.UpdateDraftAsync(versionId, request, ct)));
        admin.MapPost("/versions/{versionId:guid}/submit", async (Guid versionId, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.SubmitAsync(versionId, ct)));

        var clinical = app.MapGroup("/api/v1/clinical/note-templates").WithTags("Clinical Template Approval").RequireAuthorization(AuthorizationPolicies.NoteTemplateClinicalPublish);
        clinical.MapGet("/", async ([FromQuery] NoteType? noteType, [FromQuery] NoteTemplateVersionStatus? status, INoteTemplateAdministrationService service, CancellationToken ct) => Results.Ok(await service.ListForClinicalReviewAsync(noteType, status, ct)));
        clinical.MapGet("/versions/{versionId:guid}", async (Guid versionId, INoteTemplateAdministrationService service, CancellationToken ct) => await service.GetVersionAsync(versionId, ct) is { } row ? Results.Ok(row) : Results.NotFound());
        clinical.MapPost("/versions/{versionId:guid}/publish", async (Guid versionId, NoteTemplateReviewRequest request, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.PublishAsync(versionId, request, ct)));
        clinical.MapPost("/versions/{versionId:guid}/reject", async (Guid versionId, NoteTemplateReviewRequest request, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.RejectAsync(versionId, request, ct)));
        clinical.MapPost("/versions/{versionId:guid}/retire", async (Guid versionId, NoteTemplateReviewRequest request, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.RetireAsync(versionId, request, ct)));

        app.MapGet("/api/v1/note-templates/resolve", async ([FromQuery] NoteType noteType, [FromQuery] NoteTemplateVariant variant, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.ResolveAsync(noteType, variant, ct))).WithTags("Note Templates").RequireAuthorization(AuthorizationPolicies.NoteRead);
        app.MapGet("/api/v1/note-templates/versions/{versionId:guid}", async (Guid versionId, INoteTemplateAdministrationService service, CancellationToken ct) => await service.GetVersionAsync(versionId, ct) is { } row ? Results.Ok(row) : Results.NotFound()).WithTags("Note Templates").RequireAuthorization(AuthorizationPolicies.NoteRead);
        app.MapGet("/api/v1/note-templates/{templateId:guid}/versions", async (Guid templateId, INoteTemplateAdministrationService service, CancellationToken ct) => await Execute(() => service.ListVersionsAsync(templateId, ct))).WithTags("Note Templates").RequireAuthorization(AuthorizationPolicies.NoteRead);
    }
    private static async Task<IResult> Execute<T>(Func<Task<T>> action, bool created = false) { try { var value = await action(); return created ? Results.Created(string.Empty, value) : Results.Ok(value); } catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); } catch (DbUpdateConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); } catch (DbUpdateException) { return Results.Conflict(new { error = "The template change conflicts with a newer version. Refresh and try again." }); } catch (ArgumentException ex) { return Results.UnprocessableEntity(new { error = ex.Message }); } catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); } }
}
