using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Notes.Workspace;

/// <summary>Keep typed observations in the same transaction as versioned note JSON, including offline writes.</summary>
internal static class ObjectiveMetricPersistence
{
    public static async Task ReplaceVersionedAsync(ApplicationDbContext db, ClinicalNote note, CancellationToken ct)
    {
        NoteWorkspaceV2Payload? payload;
        try { payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return; }
        if (payload?.Objective?.MeasurementSchemaVersion is not (1 or 2)) return;
        await ReplaceAsync(db, note, payload.Objective.Metrics, ct);
    }

    public static async Task ReplaceAsync(ApplicationDbContext db, ClinicalNote note, IEnumerable<ObjectiveMetricInputV2> rows, CancellationToken ct)
    {
        var existing = await db.ObjectiveMetrics.Where(m => m.NoteId == note.Id).ToListAsync(ct);
        db.ObjectiveMetrics.RemoveRange(existing);
        note.ObjectiveMetrics.Clear();
        foreach (var m in rows.Where(m => !string.IsNullOrWhiteSpace(m.Value)))
            db.ObjectiveMetrics.Add(new ObjectiveMetric
            {
                NoteId = note.Id,
                Note = note,
                Name = m.Name,
                BodyPart = m.BodyPart,
                MetricType = m.MetricType,
                Value = m.Value,
                IsWNL = m.IsWithinNormalLimits,
                MovementId = m.MovementId,
                Side = m.Side,
                MotionMode = m.MotionMode,
                Unit = m.Unit,
                NormValue = m.NormValue,
                PreviousValue = m.PreviousValue,
                ReferenceDocumentPath = m.ReferenceDocumentPath,
                ReferenceVersion = m.ReferenceVersion
            });
    }
}
