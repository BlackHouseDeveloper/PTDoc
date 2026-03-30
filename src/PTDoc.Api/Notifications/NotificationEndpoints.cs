using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.Api.Notifications;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", GetState)
            .WithName("GetNotificationState")
            .WithSummary("Get all active notifications and preferences for the current user");

        group.MapPost("/mark-all-read", MarkAllRead)
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Mark all notifications as read for the current user");

        group.MapPost("/{id:guid}/mark-read", MarkRead)
            .WithName("MarkNotificationRead")
            .WithSummary("Mark a single notification as read");

        group.MapPost("/clear", ClearAll)
            .WithName("ClearAllNotifications")
            .WithSummary("Archive (soft-delete) all notifications for the current user");

        group.MapPut("/preferences", SavePreferences)
            .WithName("SaveNotificationPreferences")
            .WithSummary("Save notification preferences for the current user");
    }

    private static async Task<IResult> GetState(
        [FromServices] IUserNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var state = await notificationService.GetStateAsync(cancellationToken);
        return Results.Ok(state);
    }

    private static async Task<IResult> MarkAllRead(
        [FromServices] IUserNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var state = await notificationService.MarkAllReadAsync(cancellationToken);
        return Results.Ok(state);
    }

    private static async Task<IResult> MarkRead(
        Guid id,
        [FromServices] IUserNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var state = await notificationService.MarkReadAsync(id, cancellationToken);
        return Results.Ok(state);
    }

    private static async Task<IResult> ClearAll(
        [FromServices] IUserNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var state = await notificationService.ClearAllAsync(cancellationToken);
        return Results.Ok(state);
    }

    private static async Task<IResult> SavePreferences(
        [FromBody] SaveNotificationPreferencesRequest request,
        [FromServices] IUserNotificationService notificationService,
        CancellationToken cancellationToken)
    {
        var state = await notificationService.SavePreferencesAsync(request, cancellationToken);
        return Results.Ok(state);
    }
}
