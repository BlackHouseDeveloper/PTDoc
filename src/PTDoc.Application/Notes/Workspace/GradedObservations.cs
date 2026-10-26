using System.Text.Json;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;

namespace PTDoc.Application.Notes.Workspace;

/// <summary>Uses the existing runtime-canonical, separate grading catalogs; never converts ROM values.</summary>
public static class GradedObservations
{
    private static readonly Lazy<WorkspaceJointMobilityAndMmtAsset> Catalog = new(() =>
    {
        using var stream = typeof(GradedObservations).Assembly.GetManifestResourceStream("PTDoc.Application.Data.WorkspaceReferenceCatalog.json")
            ?? throw new InvalidOperationException("The workspace grading catalog is missing.");
        return JsonSerializer.Deserialize<WorkspaceReferenceCatalogAsset>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Shared.JointMobilityAndMmt;
    });
    public static IReadOnlyList<string> Grades(MetricType kind) => kind switch
    {
        MetricType.MMT => Catalog.Value.MmtGrades.AsReadOnly(),
        MetricType.JointMobility => Catalog.Value.JointMobilityGrades.AsReadOnly(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static bool IsGraded(MetricType kind) => kind is MetricType.MMT or MetricType.JointMobility;
    public static bool IsStructured(ObjectiveMetricInputV2 row) => IsGraded(row.MetricType) && !string.IsNullOrWhiteSpace(row.MovementId);
    public static IReadOnlyList<ObjectiveMetricInputV2> SeedMmt(IEnumerable<ObjectiveMetricInputV2> rom, IEnumerable<ObjectiveMetricInputV2> existing)
    {
        var seen = existing.Where(IsStructured).Select(ObjectiveMetricDimensions.Key).ToHashSet(StringComparer.Ordinal);
        var result = new List<ObjectiveMetricInputV2>();
        foreach (var row in rom.Where(m => m.MetricType == MetricType.ROM && !string.IsNullOrWhiteSpace(m.MovementId)))
        {
            var seed = New(row.BodyPart, MetricType.MMT, row.MovementId!, row.Name, row.Side);
            if (seen.Add(ObjectiveMetricDimensions.Key(seed))) result.Add(seed);
        }
        return result;
    }
    public static ObjectiveMetricInputV2 New(BodyPart region, MetricType kind, string movementId, string? name, string? side)
        => new()
        {
            BodyPart = region,
            MetricType = kind,
            MovementId = movementId,
            Name = name ?? string.Empty,
            Side = side,
            Unit = "grade",
            Value = string.Empty,
            ReferenceDocumentPath = Catalog.Value.Provenance?.DocumentPath,
            ReferenceVersion = Catalog.Value.Provenance?.Version
        };
    public static IEnumerable<string> Validate(ObjectiveMetricInputV2 row)
    {
        if (row.MetricType == MetricType.JointMobility && string.IsNullOrWhiteSpace(row.MovementId))
            yield return "Joint mobility requires a stable observation identity.";
        if (!IsStructured(row)) yield break; // Unclassified historical entries retain their original representation.
        if (row.MotionMode is not null || row.Unit != "grade") yield return "Strength and joint mobility use grades, without a ROM mode or degree unit.";
        if (ObjectiveMetricDimensions.IsPaired(row.BodyPart) && row.Side is not ("Left" or "Right")) yield return "Select Left or Right for paired graded observations.";
        if (ObjectiveMetricDimensions.IsSpine(row.BodyPart) && !string.IsNullOrWhiteSpace(row.Side)) yield return "Spine graded observations are unsided.";
        foreach (var value in new[] { row.Value, row.PreviousValue, row.NormValue })
            if (!string.IsNullOrEmpty(value) && !Grades(row.MetricType).Contains(value, StringComparer.Ordinal))
                yield return $"Use an approved {row.MetricType} grade; grades cannot be transferred between measurement kinds.";
    }
}
