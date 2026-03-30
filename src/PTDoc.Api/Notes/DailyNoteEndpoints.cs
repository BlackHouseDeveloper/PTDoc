using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.Api.Notes;

public static class DailyNoteEndpoints
{
    public static WebApplication MapDailyNoteEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/daily-notes").RequireAuthorization();

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

        group.MapPost("/", async ([FromBody] SaveDailyNoteRequest request, IDailyNoteService service, CancellationToken ct) =>
            Results.Ok(await service.SaveDraftAsync(request, ct)))
            .RequireAuthorization(AuthorizationPolicies.NoteWrite)
            .WithName("SaveDailyNoteDraft");

        group.MapPost("/generate-assessment", async ([FromBody] DailyNoteContentDto content, IDailyNoteService service, CancellationToken ct) =>
        {
            var narrative = await service.GenerateAssessmentNarrativeAsync(content, ct);
            return Results.Ok(new { narrative });
        }).RequireAuthorization(AuthorizationPolicies.NoteWrite)
          .WithName("GenerateAssessmentNarrative");

        group.MapPost("/cpt-time", ([FromBody] CptTimeCalculationRequest request, IDailyNoteService service) =>
            Results.Ok(service.CalculateCptTime(request)))
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("CalculateCptTime");

        group.MapPost("/check-medical-necessity", ([FromBody] DailyNoteContentDto content, IDailyNoteService service) =>
            Results.Ok(service.CheckMedicalNecessity(content)))
            .RequireAuthorization(AuthorizationPolicies.ClinicalStaff)
            .WithName("CheckMedicalNecessity");

        group.MapGet("/eval-carry-forward/{patientId:guid}", async (Guid patientId, IDailyNoteService service, CancellationToken ct) =>
            Results.Ok(await service.GetEvalCarryForwardAsync(patientId, ct)))
            .RequireAuthorization(AuthorizationPolicies.NoteRead)
            .WithName("GetEvalCarryForward");

        return app;
    }
}
