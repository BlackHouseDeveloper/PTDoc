using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Claims;

namespace PTDoc.Api.Navigation;

public static class NavigationBadgeEndpoints
{
    public static void MapNavigationBadgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/navigation")
            .WithTags("Navigation")
            .RequireAuthorization(AuthorizationPolicies.PatientRead);

        group.MapGet("/badges", GetBadgeCounts)
            .WithName("GetNavigationBadgeCounts")
            .WithSummary("Get live badge counts for the main navigation");
    }

    private static async Task<IResult> GetBadgeCounts(
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] IAuthorizationService authorizationService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var today = now.UtcDateTime.Date;
        var tomorrow = today.AddDays(1);
        var currentUserId = identityContext.TryGetCurrentUserId();

        var canReadIntake = (await authorizationService.AuthorizeAsync(user, AuthorizationPolicies.IntakeRead)).Succeeded;
        var canReadNotes = (await authorizationService.AuthorizeAsync(user, AuthorizationPolicies.NoteRead)).Succeeded;

        var intakeCount = canReadIntake
            ? await CountIntakeActionItemsAsync(db, cancellationToken)
            : 0;
        var notesCount = canReadNotes
            ? await CountNoteActionItemsAsync(db, today, tomorrow, cancellationToken)
            : 0;
        var notificationsCount = currentUserId.HasValue
            ? await db.UserNotifications
                .AsNoTracking()
                .CountAsync(notification =>
                    notification.UserId == currentUserId.Value &&
                    !notification.IsArchived &&
                    !notification.IsRead,
                    cancellationToken)
            : 0;

        return Results.Ok(new NavigationBadgeCountsResponse
        {
            IntakeCount = intakeCount,
            NotesCount = notesCount,
            NotificationsCount = notificationsCount,
            GeneratedAtUtc = now
        });
    }

    private static async Task<int> CountIntakeActionItemsAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        return await db.IntakeForms
            .AsNoTracking()
            .CountAsync(intake =>
                intake.Patient != null &&
                !intake.Patient.IsArchived &&
                ((!intake.IsLocked && intake.SubmittedAt == null) ||
                 (intake.IsLocked && intake.SubmittedAt != null && intake.ReviewedAtUtc == null)),
                cancellationToken);
    }

    private static async Task<int> CountNoteActionItemsAsync(
        ApplicationDbContext db,
        DateTime today,
        DateTime tomorrow,
        CancellationToken cancellationToken)
    {
        var unsignedNotes = await db.ClinicalNotes
            .AsNoTracking()
            .CountAsync(note =>
                !note.IsAddendum &&
                note.NoteStatus != NoteStatus.Signed &&
                note.Patient != null &&
                !note.Patient.IsArchived,
                cancellationToken);

        var missingTodayNotes = await db.Appointments
            .AsNoTracking()
            .CountAsync(appointment =>
                appointment.StartTimeUtc >= today &&
                appointment.StartTimeUtc < tomorrow &&
                (appointment.Status == AppointmentStatus.CheckedIn ||
                 appointment.Status == AppointmentStatus.InProgress ||
                 appointment.Status == AppointmentStatus.Completed) &&
                appointment.Patient != null &&
                !appointment.Patient.IsArchived &&
                !db.ClinicalNotes.Any(note =>
                    !note.IsAddendum &&
                    (note.AppointmentId == appointment.Id ||
                     (note.PatientId == appointment.PatientId &&
                      note.DateOfService >= today &&
                      note.DateOfService < tomorrow))),
                cancellationToken);

        return unsignedNotes + missingTodayNotes;
    }
}
