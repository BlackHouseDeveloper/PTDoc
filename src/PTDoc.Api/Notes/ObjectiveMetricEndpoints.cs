using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.Notes;

/// <summary>
/// CRUD endpoints for objective metrics on draft clinical notes.
/// Mutations are guarded: the parent note must be in Draft status.
/// Sprint O: TDD §5.4 ObjectiveMetric
/// </summary>
public static class ObjectiveMetricEndpoints
{
    public static void MapObjectiveMetricEndpoints(this IEndpointRouteBuilder app)
    {
        var readGroup = app.MapGroup("/api/v1/notes/{noteId:guid}/objective-metrics")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteRead);

        var writeGroup = app.MapGroup("/api/v1/notes/{noteId:guid}/objective-metrics")
            .WithTags("Notes")
            .RequireAuthorization(AuthorizationPolicies.NoteWrite);

        readGroup.MapGet("/", GetMetrics)
            .WithName("GetObjectiveMetrics")
            .WithSummary("Get all objective metrics for a note");

        writeGroup.MapPost("/", AddMetric)
            .WithName("AddObjectiveMetric")
            .WithSummary("Add an objective metric to a draft note");

        writeGroup.MapPut("/{metricId:guid}", UpdateMetric)
            .WithName("UpdateObjectiveMetric")
            .WithSummary("Update an objective metric on a draft note");

        writeGroup.MapDelete("/{metricId:guid}", DeleteMetric)
            .WithName("DeleteObjectiveMetric")
            .WithSummary("Delete an objective metric from a draft note");
    }

    // GET /api/v1/notes/{noteId}/objective-metrics
    private static async Task<IResult> GetMetrics(
        Guid noteId,
        [FromServices] ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var noteExists = await db.ClinicalNotes
            .AsNoTracking()
            .AnyAsync(n => n.Id == noteId, cancellationToken);

        if (!noteExists)
            return Results.NotFound(new { error = $"Note {noteId} not found." });

        var metrics = await db.ObjectiveMetrics
            .AsNoTracking()
            .Where(m => m.NoteId == noteId)
            .ToListAsync(cancellationToken);

        return Results.Ok(metrics.Select(ToResponse));
    }

    // POST /api/v1/notes/{noteId}/objective-metrics
    private static async Task<IResult> AddMetric(
        Guid noteId,
        [FromBody] CreateObjectiveMetricRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {noteId} not found." });

        if (note.NoteStatus != NoteStatus.Draft || !string.IsNullOrWhiteSpace(note.SignatureHash) || note.SignedUtc is not null)
            return Results.UnprocessableEntity(new { error = "Objective metrics can only be added to draft notes." });

        if (string.IsNullOrWhiteSpace(request.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                { nameof(request.Value), ["Value is required."] }
            });

        var metric = new ObjectiveMetric
        {
            NoteId = noteId,
            BodyPart = request.BodyPart,
            MetricType = request.MetricType,
            Value = request.Value.Trim(),
            Side = request.Side?.Trim(),
            Unit = request.Unit?.Trim(),
            IsWNL = request.IsWNL,
            LastModifiedUtc = DateTime.UtcNow
        };

        db.ObjectiveMetrics.Add(metric);

        // Update parent note sync/audit fields so metric changes propagate to clients
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = identityContext.GetCurrentUserId();
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("ClinicalNote", noteId, SyncOperation.Update, cancellationToken);

        return Results.Created(
            $"/api/v1/notes/{noteId}/objective-metrics/{metric.Id}",
            ToResponse(metric));
    }

    // PUT /api/v1/notes/{noteId}/objective-metrics/{metricId}
    private static async Task<IResult> UpdateMetric(
        Guid noteId,
        Guid metricId,
        [FromBody] UpdateObjectiveMetricRequest request,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {noteId} not found." });

        if (note.NoteStatus != NoteStatus.Draft || !string.IsNullOrWhiteSpace(note.SignatureHash) || note.SignedUtc is not null)
            return Results.UnprocessableEntity(new { error = "Objective metrics can only be modified on draft notes." });

        var metric = await db.ObjectiveMetrics
            .FirstOrDefaultAsync(m => m.Id == metricId && m.NoteId == noteId, cancellationToken);

        if (metric is null)
            return Results.NotFound(new { error = $"Metric {metricId} not found on note {noteId}." });

        if (request.BodyPart is not null)
            metric.BodyPart = request.BodyPart.Value;

        if (request.MetricType is not null)
            metric.MetricType = request.MetricType.Value;

        if (request.Value is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Value))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { nameof(request.Value), ["Value cannot be empty."] }
                });
            metric.Value = request.Value.Trim();
        }

        if (request.Side is not null)
            metric.Side = request.Side.Trim();

        if (request.Unit is not null)
            metric.Unit = request.Unit.Trim();

        if (request.IsWNL is not null)
            metric.IsWNL = request.IsWNL.Value;

        metric.LastModifiedUtc = DateTime.UtcNow;

        // Update parent note sync/audit fields
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = identityContext.GetCurrentUserId();
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("ClinicalNote", noteId, SyncOperation.Update, cancellationToken);

        return Results.Ok(ToResponse(metric));
    }

    // DELETE /api/v1/notes/{noteId}/objective-metrics/{metricId}
    private static async Task<IResult> DeleteMetric(
        Guid noteId,
        Guid metricId,
        [FromServices] ApplicationDbContext db,
        [FromServices] IIdentityContextAccessor identityContext,
        [FromServices] ISyncEngine syncEngine,
        CancellationToken cancellationToken)
    {
        var note = await db.ClinicalNotes
            .FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);

        if (note is null)
            return Results.NotFound(new { error = $"Note {noteId} not found." });

        if (note.NoteStatus != NoteStatus.Draft || !string.IsNullOrWhiteSpace(note.SignatureHash) || note.SignedUtc is not null)
            return Results.UnprocessableEntity(new { error = "Objective metrics can only be deleted from draft notes." });

        var metric = await db.ObjectiveMetrics
            .FirstOrDefaultAsync(m => m.Id == metricId && m.NoteId == noteId, cancellationToken);

        if (metric is null)
            return Results.NotFound(new { error = $"Metric {metricId} not found on note {noteId}." });

        db.ObjectiveMetrics.Remove(metric);

        // Update parent note sync/audit fields
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = identityContext.GetCurrentUserId();
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(cancellationToken);
        await syncEngine.EnqueueAsync("ClinicalNote", noteId, SyncOperation.Update, cancellationToken);

        return Results.NoContent();
    }

    private static ObjectiveMetricResponse ToResponse(ObjectiveMetric m) => new()
    {
        Id = m.Id,
        NoteId = m.NoteId,
        BodyPart = m.BodyPart,
        MetricType = m.MetricType,
        Value = m.Value,
        Side = m.Side,
        Unit = m.Unit,
        IsWNL = m.IsWNL,
        LastModifiedUtc = m.LastModifiedUtc
    };
}

/// <summary>Request DTO for updating an objective metric (all fields optional).</summary>
public sealed class UpdateObjectiveMetricRequest
{
    public BodyPart? BodyPart { get; set; }
    public MetricType? MetricType { get; set; }
    public string? Value { get; set; }
    public string? Side { get; set; }
    public string? Unit { get; set; }
    public bool? IsWNL { get; set; }
}
