using PTDoc.Core.Models;

namespace PTDoc.UI.Components.Intake.Models;

public static class IntakeSupplementalOptionCatalog
{
    public static readonly IReadOnlyList<string> Comorbidities =
    [
        "Hypertension (High Blood Pressure)",
        "Type 2 Diabetes Mellitus",
        "Obesity",
        "Hyperlipidemia (High Cholesterol)",
        "Coronary Artery Disease (CAD)",
        "Chronic Obstructive Pulmonary Disease (COPD)",
        "Asthma",
        "Osteoarthritis",
        "Rheumatoid Arthritis",
        "Chronic Kidney Disease (CKD)",
        "Depression",
        "Anxiety Disorders",
        "Sleep Apnea (Obstructive Sleep Apnea - OSA)",
        "Hypothyroidism",
        "Anemia",
        "Gastroesophageal Reflux Disease (GERD)",
        "Heart Failure (Congestive Heart Failure - CHF)",
        "Stroke / Cerebrovascular Disease",
        "Cancer (e.g., breast, prostate, colon)",
        "Dementia / Alzheimer's"
    ];

    public static readonly IReadOnlyList<string> LivingSituations =
    [
        "Lives alone",
        "Lives with spouse/partner",
        "Lives with family (e.g., adult children, siblings, extended family)",
        "Lives with roommate(s)",
        "Lives in assisted living facility",
        "Lives in skilled nursing facility (SNF)",
        "Lives in group home or supervised housing",
        "Lives in shelter or transitional housing",
        "Experiencing homelessness / unhoused",
        "Lives with caregiver (paid or unpaid)"
    ];

    public static readonly IReadOnlyList<string> HouseLayoutOptions =
    [
        "Single-Story Home: Bedroom and bathroom on main floor",
        "Two-Story Home: Bedroom and bathroom on second floor",
        "Two-Story Home: Bedroom on second floor, bathroom on first floor",
        "Two-Story Home: Bedroom and bathroom on second floor, additional bathroom on first floor",
        "Two-Story Home: Bedroom on first floor, bathroom on second floor",
        "Two-Story Home: Bedroom and bathroom on first floor, other bedrooms/bath upstairs"
    ];

    public static readonly IReadOnlyList<string> AssistiveDevices =
    [
        "Cervical collar (soft or rigid)",
        "Ergonomic pens / computer mouse",
        "Arm sling / shoulder immobilizer",
        "Long-handled sponge / hairbrush",
        "One-handed dressing aids",
        "Wheelchair with arm troughs or lap trays",
        "Scooter with steering modifications",
        "Elbow brace (static/dynamic)",
        "Walker with forearm support",
        "Wrist brace / splint",
        "Universal cuff",
        "Built-up utensil handles",
        "Jar opener / grip aids",
        "Electric can opener",
        "Thumb spica splint",
        "Button hook / zipper puller",
        "Lumbar support belt",
        "Back brace (e.g., TLSO)",
        "Shower chair or transfer bench",
        "Long-handled reacher",
        "Sock aid / dressing stick",
        "SI belt",
        "Hip abduction brace",
        "Raised toilet seat / commode",
        "Grab bars and railings",
        "Knee brace (hinged, compression)",
        "Step stool or transfer bench",
        "Ankle brace or air-stirrup splint",
        "Knee scooter",
        "Crutches",
        "Walker",
        "Cane",
        "Cane (standard, quad)",
        "Crutches (axillary or forearm)",
        "Walker (standard, front-wheeled, rollator)",
        "Wheelchair (manual or powered)"
    ];

    public static IReadOnlyList<string> GetSuggestedGroupIds(IEnumerable<BodyRegion> regions)
    {
        var suggested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in regions)
        {
            var key = region.ToString();
            if (key.Contains("Head", StringComparison.OrdinalIgnoreCase) || key.Contains("Neck", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("head-neck");
            }

            if (key.Contains("Shoulder", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Deltoid", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Upperback", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("shoulders-upper-back");
            }

            if (key.Contains("Arm", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Forearm", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Elbow", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Hand", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("arms");
            }

            if (key.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Abdomen", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Midtorso", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("chest-abdomen");
            }

            if (key.Contains("back", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("spine-trunk");
            }

            if (key.Contains("Pelvis", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Gluteal", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("pelvis-hips");
            }

            if (key.Contains("Thigh", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Knee", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Calf", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Foot", StringComparison.OrdinalIgnoreCase))
            {
                suggested.Add("legs");
            }
        }

        return suggested.ToList();
    }

    public static IReadOnlyList<string> GetRecommendedOutcomeMeasures(IEnumerable<string> bodyPartIds)
    {
        var recommendations = new List<string>();

        foreach (var bodyPartId in bodyPartIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var measure in bodyPartId switch
                     {
                         "neck" or "cervical-spine" or "head-neck-cervical-spine" => new[] { "NDI", "PSFS", "VAS/NPRS" },
                         "shoulder" => new[] { "DASH", "QuickDASH", "SPADI", "ASES" },
                         "elbow" or "wrist" or "hand" or "fingers" or "thumb" or "forearm" => new[] { "DASH", "QuickDASH", "PRWE", "Michigan Hand Outcomes Questionnaire" },
                         "upper-back-thoracic-spine" or "thoracic-spine" => new[] { "PSFS", "ODI", "VAS" },
                         "lumbar-spine" or "sacrum" or "coccyx" => new[] { "ODI", "Roland-Morris Disability Questionnaire", "PSFS", "FABQ" },
                         "hip" => new[] { "LEFS", "HOOS", "Harris Hip Score", "PSFS" },
                         "knee" => new[] { "LEFS", "KOOS", "IKDC", "Lysholm Knee Score", "Tegner Activity Scale" },
                         "ankle" or "foot" or "toes" or "heel" or "arch" or "achilles-tendon" => new[] { "FAAM", "LEFS", "AOFAS" },
                         "balance-systems" or "coordination-motor-planning" => new[] { "BBS", "TUG", "5xSTS", "DGI", "FGA", "ABC", "Mini-BESTest" },
                         _ => new[] { "PSFS", "NPRS/VAS" }
                     })
            {
                if (!recommendations.Contains(measure, StringComparer.OrdinalIgnoreCase))
                {
                    recommendations.Add(measure);
                }
            }
        }

        return recommendations;
    }
}
