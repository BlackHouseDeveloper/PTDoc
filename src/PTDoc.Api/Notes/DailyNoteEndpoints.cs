using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.Api.Notes;

public static class DailyNoteEndpoints
{
    public static WebApplication MapDailyNoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/daily-notes").RequireAuthorization();

        group.MapGet("/patient/{patientId:guid}", async (Guid patientId, IDailyNoteService service, CancellationToken ct) =>
            Results.Ok(await service.GetForPatientAsync(patientId, ct: ct)))
            .RequireAuthorization(AuthorizationPolicies.NoteRead)
            .WithName("GetDailyNotesForPatient");

        group.MapGet("/{noteId:guid}", async (Guid noteId, IDailyNoteService service, CancellationToken ct) =>
        {
            var note = await service.GetByIdAsync(noteId, ct);
            return note is null ? Results.NotFound() : Results.Ok(note);
        }).RequireAuthorization(AuthorizationPolicies.NoteRead)
          .WithName("GetDailyNoteById");

        group.MapPost("/", async ([FromBody] SaveDailyNoteJsonRequest request, IDailyNoteService service, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();

            if (request.PatientId == Guid.Empty)
                errors[nameof(request.PatientId)] = ["PatientId is required."];

            if (request.DateOfService == default)
                errors[nameof(request.DateOfService)] = ["DateOfService is required."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            try
            {
                var result = await service.SaveDraftAsync(request, ct);
                return result.IsValid
                    ? Results.Ok(result)
                    : Results.UnprocessableEntity(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new DailyNoteSaveResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new DailyNoteSaveResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new DailyNoteSaveResponse
                {
                    IsValid = false,
                    Errors = [ex.Message]
                });
            }
        }).RequireAuthorization(AuthorizationPolicies.NoteWrite)
          .WithName("SaveDailyNoteDraft");

        group.MapPost("/generate-assessment", async (
            [FromBody] JsonElement content,
            IDailyNoteService service,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var enableAi = configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration", false);
            if (!enableAi)
            {
                return Results.StatusCode(403);
            }

            var narrative = await service.GenerateAssessmentNarrativeAsync(content, ct);
            return Results.Ok(new { narrative });
        }).RequireAuthorization(AuthorizationPolicies.NoteWrite)
          .WithName("GenerateAssessmentNarrative");

        group.MapPost("/cpt-time", ([FromBody] CptTimeCalculationRequest request, IDailyNoteService service) =>
            Results.Ok(service.CalculateCptTime(request)))
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("CalculateCptTime");

        group.MapPost("/check-medical-necessity", ([FromBody] JsonElement content, IDailyNoteService service) =>
            Results.Ok(service.CheckMedicalNecessity(content)))
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("CheckMedicalNecessity");

        group.MapGet("/eval-carry-forward/{patientId:guid}", async (Guid patientId, IDailyNoteService service, CancellationToken ct) =>
            Results.Ok(await service.GetEvalCarryForwardAsync(patientId, ct)))
            .RequireAuthorization(AuthorizationPolicies.NoteRead)
            .WithName("GetEvalCarryForward");

        group.MapGet("/by-taxonomy", async (
            [AsParameters] TaxonomyFilterQuery filter,
            IDailyNoteService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(filter.CategoryId))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(filter.CategoryId), ["categoryId is required."] }
                });

            var normalizedLimit = filter.Limit <= 0 ? 50 : Math.Min(filter.Limit, 500);
            var results = await service.GetByTaxonomyAsync(
                filter.CategoryId, filter.ItemId, filter.PatientId, normalizedLimit, ct);
            return Results.Ok(results);
        })
        .RequireAuthorization(AuthorizationPolicies.NoteRead)
        .WithName("GetDailyNotesByTaxonomy")
        .WithSummary("Filter daily notes by taxonomy category or specific item");

        return app;
    }
}

/// <summary>Query parameters for the by-taxonomy filter endpoint.</summary>
internal sealed class TaxonomyFilterQuery
{
    [FromQuery(Name = "categoryId")]
    public string CategoryId { get; set; } = string.Empty;

    [FromQuery(Name = "itemId")]
    public string? ItemId { get; set; }

    [FromQuery(Name = "patientId")]
    public Guid? PatientId { get; set; }

    [FromQuery(Name = "limit")]
    public int Limit { get; set; } = 50;
}
