using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.ReferenceData;

public sealed class IntakeBodyPartMapper(IIntakeReferenceDataCatalogService intakeReferenceData) : IIntakeBodyPartMapper
{
    private readonly IReadOnlyDictionary<string, BodyPart> _bodyPartMap = BuildBodyPartMap(intakeReferenceData);

    public BodyPart MapBodyPartId(string? bodyPartId)
    {
        if (string.IsNullOrWhiteSpace(bodyPartId))
        {
            return BodyPart.Other;
        }

        return _bodyPartMap.TryGetValue(bodyPartId.Trim(), out var mapped)
            ? mapped
            : BodyPart.Other;
    }

    private static IReadOnlyDictionary<string, BodyPart> BuildBodyPartMap(IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        var knownIds = intakeReferenceData.GetBodyPartGroups()
            .SelectMany(group => group.Items)
            .Select(item => item.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, BodyPart>(StringComparer.OrdinalIgnoreCase);

        MapIds(map, knownIds, BodyPart.Cervical, "neck", "cervical-spine", "head-neck-cervical-spine");
        MapIds(map, knownIds, BodyPart.Thoracic, "thoracic-spine", "upper-back-thoracic-spine");
        MapIds(map, knownIds, BodyPart.Lumbar, "lumbar-spine", "sacrum", "coccyx");
        MapIds(map, knownIds, BodyPart.Shoulder, "shoulder");
        MapIds(map, knownIds, BodyPart.Elbow, "elbow", "forearm");
        MapIds(map, knownIds, BodyPart.Wrist, "wrist");
        MapIds(map, knownIds, BodyPart.Hand, "hand", "fingers", "thumb");
        MapIds(map, knownIds, BodyPart.Hip, "hip");
        MapIds(map, knownIds, BodyPart.Knee, "knee");
        MapIds(map, knownIds, BodyPart.Ankle, "ankle", "achilles-tendon");
        MapIds(map, knownIds, BodyPart.Foot, "foot", "toes", "heel", "arch");
        MapIds(map, knownIds, BodyPart.PelvicFloor, "pelvic-floor");

        return map;
    }

    private static void MapIds(
        IDictionary<string, BodyPart> destination,
        IReadOnlySet<string> knownIds,
        BodyPart bodyPart,
        params string[] bodyPartIds)
    {
        foreach (var bodyPartId in bodyPartIds)
        {
            if (knownIds.Contains(bodyPartId))
            {
                destination[bodyPartId] = bodyPart;
            }
        }
    }
}
