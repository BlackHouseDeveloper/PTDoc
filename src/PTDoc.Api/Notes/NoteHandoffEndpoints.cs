using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Services;

namespace PTDoc.Api.Notes;

public static class NoteHandoffEndpoints
{
    public static void MapNoteHandoffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notes/{noteId:guid}/handoffs")
            .WithTags("Notes").RequireAuthorization(AuthorizationPolicies.NoteWrite);
        group.MapGet("", async (Guid noteId, [FromServices] INoteHandoffService service, CancellationToken ct) =>
            ToHttp(await service.GetAsync(noteId, ct)));
        group.MapPost("", async (Guid noteId, SendNoteHandoffRequest request,
            [FromServices] INoteHandoffService service, CancellationToken ct) => ToHttp(await service.SendAsync(noteId, request, ct)));
    }

    private static IResult ToHttp<T>(NoteHandoffResult<T> result) => result.Status switch
    {
        NoteHandoffStatus.Succeeded => Results.Ok(result.Value),
        NoteHandoffStatus.Forbidden => Results.Forbid(),
        NoteHandoffStatus.NotFound => Results.NotFound(),
        NoteHandoffStatus.Conflict => Results.Conflict(new { error = "The note or handoff changed. Reload before sending a new handoff." }),
        _ => Results.BadRequest(new { error = "A recipient, handoff type, UTC note version, and valid request ID are required." })
    };
}
