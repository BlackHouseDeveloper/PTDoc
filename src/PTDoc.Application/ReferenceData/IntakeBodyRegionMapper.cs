using PTDoc.Core.Models;

namespace PTDoc.Application.ReferenceData;

/// <summary>
/// Maps the patient-facing body diagram regions to the canonical clinical body-part catalog.
/// </summary>
public static class IntakeBodyRegionMapper
{
    public static BodyPart Map(BodyRegion region) => Map(region.ToString());

    public static BodyPart Map(string? regionKey)
    {
        if (string.IsNullOrWhiteSpace(regionKey))
        {
            return BodyPart.Other;
        }

        if (Contains(regionKey, "Neck", "Head")) return BodyPart.Cervical;
        if (Contains(regionKey, "Upperback", "Midback")) return BodyPart.Thoracic;
        if (Contains(regionKey, "Lowerback", "Pelvis", "Gluteal")) return BodyPart.Lumbar;
        if (Contains(regionKey, "Shoulder", "Deltoid")) return BodyPart.Shoulder;
        if (Contains(regionKey, "Arm", "Forearm", "Elbow")) return BodyPart.Elbow;
        if (Contains(regionKey, "Hand")) return BodyPart.Hand;
        if (Contains(regionKey, "Hip", "Thigh")) return BodyPart.Hip;
        if (Contains(regionKey, "Knee")) return BodyPart.Knee;
        if (Contains(regionKey, "Calf", "Ankle")) return BodyPart.Ankle;
        if (Contains(regionKey, "Foot")) return BodyPart.Foot;

        return BodyPart.Other;
    }

    private static bool Contains(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
