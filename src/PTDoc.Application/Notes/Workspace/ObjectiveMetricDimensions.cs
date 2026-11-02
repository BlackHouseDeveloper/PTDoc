using System.Text.RegularExpressions;
using System.Text.Json;
using PTDoc.Core.Models;

namespace PTDoc.Application.Notes.Workspace;

public sealed record RomMovementDefinition(string Id, string Name, string? ReferenceValue, string? Unit,
    string? ReferenceDocumentPath, string? ReferenceVersion);

/// <summary>Observation dimensions, shared by ROM entry, storage and comparison. Never infers legacy mode.</summary>
public static class ObjectiveMetricDimensions
{
    public static bool IsSpine(BodyPart region) => region is BodyPart.Cervical or BodyPart.Thoracic or BodyPart.Lumbar;
    public static bool IsPaired(BodyPart region) => region is BodyPart.Shoulder or BodyPart.Elbow or BodyPart.Wrist
        or BodyPart.Hand or BodyPart.Hip or BodyPart.Knee or BodyPart.Ankle or BodyPart.Foot;

    public static bool AppliesToRegion(BodyPart region, string label) => region switch
    {
        BodyPart.Cervical or BodyPart.Thoracic or BodyPart.Lumbar => true,
        BodyPart.Hand => new[] { "MCP ", "PIP ", "DIP ", "Thumb " }.Any(prefix => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
        BodyPart.Foot => label.StartsWith("Great Toe ", StringComparison.OrdinalIgnoreCase) || label.StartsWith("Foot ", StringComparison.OrdinalIgnoreCase),
        _ => label.StartsWith(region + " ", StringComparison.OrdinalIgnoreCase)
    };

    public static RomMovementDefinition FromCatalog(BodyPart region, string label, CatalogAvailability availability)
    {
        var parts = label.Split(':', 2, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var id = $"{region.ToString().ToLowerInvariant()}:{Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-')}";
        var reference = parts.Length == 2 ? parts[1] : null;
        return new(id, name, reference, reference?.Contains("degrees", StringComparison.OrdinalIgnoreCase) == true ? "degrees" : null,
            availability.Provenance?.DocumentPath, availability.Provenance?.Version);
    }

    public static IReadOnlyList<ObjectiveMetricInputV2> CreateRomRows(BodyPart region, RomMovementDefinition movement)
    {
        string?[] sides = IsPaired(region) ? ["Left", "Right"] : [null];
        return sides.SelectMany(side => new[] { RomMotionMode.Active, RomMotionMode.Passive }.Select(mode => new ObjectiveMetricInputV2
        {
            MovementId = movement.Id,
            Name = movement.Name,
            BodyPart = region,
            MetricType = MetricType.ROM,
            Side = side,
            MotionMode = mode,
            Unit = movement.Unit,
            NormValue = movement.ReferenceValue,
            ReferenceDocumentPath = movement.ReferenceDocumentPath,
            ReferenceVersion = movement.ReferenceVersion
        })).ToList();
    }

    public static string Key(BodyPart region, MetricType type, string? movementId, string? name, string? side, RomMotionMode? mode, string? unit)
        => string.Join("|", (int)region, (int)type, Normalize(movementId ?? name), Normalize(side), mode?.ToString() ?? "Unknown", Normalize(unit));
    public static string Key(ObjectiveMetricInputV2 m) => Key(m.BodyPart, m.MetricType, m.MovementId, m.Name, m.Side, m.MotionMode, m.Unit);
    public static string Key(ObjectiveMetric m) => Key(m.BodyPart, m.MetricType, m.MovementId, m.Name, m.Side, m.MotionMode, m.Unit);
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    public static IReadOnlyList<string> Validate(IEnumerable<ObjectiveMetricInputV2> rows)
    {
        var errors = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in rows)
        {
            if (m.MotionMode.HasValue && (!Enum.IsDefined(m.MotionMode.Value) || m.MetricType != MetricType.ROM))
                errors.Add("Active/passive mode is valid only for ROM observations.");
            if (m.MotionMode.HasValue && IsSpine(m.BodyPart) && !string.IsNullOrWhiteSpace(m.Side))
                errors.Add("Spine ROM uses an unsided observation; do not assign an artificial limb side.");
            if (m.MotionMode.HasValue && IsPaired(m.BodyPart) && m.Side is not ("Left" or "Right"))
                errors.Add("Select Left or Right for a paired-region ROM observation.");
            if (!string.IsNullOrEmpty(m.MovementId) && !seen.Add(Key(m)))
                errors.Add("Duplicate movement, side and mode observations are not allowed.");
            if (!Enum.IsDefined(m.MetricType)) errors.Add("Unknown objective measurement kind.");
            errors.AddRange(GradedObservations.Validate(m));
            Check(m.Name, 200); Check(m.Value, 200); Check(m.PreviousValue, 200); Check(m.NormValue, 200);
            Check(m.MovementId, 160); Check(m.Side, 20); Check(m.Unit, 50); Check(m.ReferenceDocumentPath, 500); Check(m.ReferenceVersion, 100);
        }
        return errors;
        void Check(string? value, int limit) { if (value?.Length > limit) errors.Add($"An objective measurement field exceeds its {limit}-character limit."); }
    }
    /// <summary>Used by workspace, legacy note and offline writes, never by historical readers.</summary>
    public static IReadOnlyList<string> ValidateJsonRevision(string? existingJson, string? incomingJson)
    {
        var old = ReadWorkspace(existingJson)?.Objective;
        var current = ReadWorkspace(incomingJson)?.Objective;
        var oldHasDimensions = old?.MeasurementSchemaVersion is not null || old?.Metrics?.Any(m => m.MotionMode.HasValue || m.MovementId is not null) == true;
        var newHasDimensions = current?.Metrics?.Any(m => m.MotionMode.HasValue || m.MovementId is not null) == true;
        var graded = old?.MeasurementSchemaVersion == 2 || old?.Metrics?.Any(GradedObservations.IsStructured) == true
            || current?.Metrics?.Any(m => GradedObservations.IsStructured(m) || m.MetricType == MetricType.JointMobility) == true;
        if ((oldHasDimensions || newHasDimensions) && current?.MeasurementSchemaVersion is not (1 or 2)
            || current?.MeasurementSchemaVersion is not (null or 1 or 2)
            || graded && current?.MeasurementSchemaVersion != 2)
            return ["Use a compatible note editor to preserve the measurement kinds, dimensions and their schema version."];
        return current is null ? [] : Validate(current.Metrics ?? []);
    }

    public static void EnsureValidJsonRevision(string? existingJson, string? incomingJson)
    {
        var errors = ValidateJsonRevision(existingJson, incomingJson);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
    }

    private static NoteWorkspaceV2Payload? ReadWorkspace(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase)
                    && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var version) && version == 2)) return null;
            return JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { return null; }
    }

}
