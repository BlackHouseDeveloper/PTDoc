using PTDoc.Application.ReferenceData;

namespace PTDoc.Infrastructure.ReferenceData;

public sealed class IntakeReferenceDataCatalogService : IIntakeReferenceDataCatalogService
{
    private static readonly IntakeReferenceCatalogDto Catalog = BuildCatalog();
    private static readonly Dictionary<string, IntakeBodyPartItemDto> BodyPartsById = Catalog.BodyPartGroups
        .SelectMany(group => group.Items)
        .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IntakeMedicationItemDto> MedicationsById = Catalog.Medications
        .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IntakePainDescriptorItemDto> PainDescriptorsById = Catalog.PainDescriptors
        .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    public IntakeReferenceCatalogDto GetCatalog() => Catalog;

    public IReadOnlyList<IntakeBodyPartGroupDto> GetBodyPartGroups() => Catalog.BodyPartGroups;

    public IReadOnlyList<IntakeMedicationItemDto> GetMedications() => Catalog.Medications;

    public IReadOnlyList<IntakePainDescriptorItemDto> GetPainDescriptors() => Catalog.PainDescriptors;

    public IntakeBodyPartItemDto? GetBodyPart(string bodyPartId)
    {
        if (string.IsNullOrWhiteSpace(bodyPartId))
        {
            return null;
        }

        return BodyPartsById.TryGetValue(bodyPartId, out var item) ? item : null;
    }

    public IntakeMedicationItemDto? GetMedication(string medicationId)
    {
        if (string.IsNullOrWhiteSpace(medicationId))
        {
            return null;
        }

        return MedicationsById.TryGetValue(medicationId, out var item) ? item : null;
    }

    public IntakePainDescriptorItemDto? GetPainDescriptor(string painDescriptorId)
    {
        if (string.IsNullOrWhiteSpace(painDescriptorId))
        {
            return null;
        }

        return PainDescriptorsById.TryGetValue(painDescriptorId, out var item) ? item : null;
    }

    private static IntakeReferenceCatalogDto BuildCatalog()
    {
        var bodyPartGroups = new List<IntakeBodyPartGroupDto>
        {
            Group("head-neck", "Head & Neck", IntakeBodyPartGroupKind.AnatomicalRegion, 1,
                BodyPart("head", "Head"),
                BodyPart("skull", "Skull"),
                BodyPart("jaw", "Jaw (TMJ)", supportsLaterality: true),
                BodyPart("neck", "Neck", supportsLaterality: true),
                BodyPart(
                    "head-neck-cervical-spine",
                    "Cervical spine",
                    supportsLaterality: true,
                    sourceNote: "Source also lists a separate non-lateral cervical spine item under Spine & Trunk.")),

            Group("shoulders-upper-back", "Shoulders & Upper Back", IntakeBodyPartGroupKind.AnatomicalRegion, 2,
                BodyPart("shoulder", "Shoulder", supportsLaterality: true),
                BodyPart("clavicle", "Clavicle (collarbone)", supportsLaterality: true),
                BodyPart("scapula", "Scapula (shoulder blade)", supportsLaterality: true),
                BodyPart("upper-back-thoracic-spine", "Upper back (thoracic spine)", supportsLaterality: true)),

            Group("arms", "Arms", IntakeBodyPartGroupKind.AnatomicalRegion, 3,
                BodyPart("upper-arm", "Upper arm", supportsLaterality: true),
                BodyPart("elbow", "Elbow", supportsLaterality: true),
                BodyPart("forearm", "Forearm", supportsLaterality: true),
                BodyPart("wrist", "Wrist", supportsLaterality: true),
                BodyPart("hand", "Hand", supportsLaterality: true),
                BodyPart(
                    "fingers",
                    "Fingers",
                    supportsLaterality: true,
                    digitOptions:
                    [
                        Digit("index", "Index finger", 1),
                        Digit("middle", "Middle finger", 2),
                        Digit("ring", "Ring finger", 3),
                        Digit("little", "Little finger", 4)
                    ]),
                BodyPart("thumb", "Thumb", supportsLaterality: true)),

            Group("chest-abdomen", "Chest & Abdomen", IntakeBodyPartGroupKind.AnatomicalRegion, 4,
                BodyPart("rib-cage", "Rib cage", supportsLaterality: true),
                BodyPart("sternum", "Sternum"),
                BodyPart("abdominal-muscles", "Abdominal muscles"),
                BodyPart("diaphragm", "Diaphragm"),
                BodyPart("pelvic-floor", "Pelvic floor")),

            Group("spine-trunk", "Spine & Trunk", IntakeBodyPartGroupKind.AnatomicalRegion, 5,
                BodyPart("cervical-spine", "Cervical spine"),
                BodyPart("thoracic-spine", "Thoracic spine"),
                BodyPart("lumbar-spine", "Lumbar spine"),
                BodyPart("sacrum", "Sacrum"),
                BodyPart("coccyx", "Coccyx (tailbone)")),

            Group("pelvis-hips", "Pelvis & Hips", IntakeBodyPartGroupKind.AnatomicalRegion, 6,
                BodyPart("pelvis", "Pelvis", supportsLaterality: true),
                BodyPart("hip", "Hip", supportsLaterality: true),
                BodyPart("sacroiliac-joint", "Sacroiliac joint (SIJ)", supportsLaterality: true),
                BodyPart("groin", "Groin", supportsLaterality: true)),

            Group("legs", "Legs", IntakeBodyPartGroupKind.AnatomicalRegion, 7,
                BodyPart("thigh", "Thigh", supportsLaterality: true),
                BodyPart("knee", "Knee", supportsLaterality: true),
                BodyPart("hamstring", "Hamstring", supportsLaterality: true),
                BodyPart("quadriceps", "Quadriceps", supportsLaterality: true),
                BodyPart("it-band", "IT band", supportsLaterality: true),
                BodyPart("calf", "Calf", supportsLaterality: true),
                BodyPart("shin", "Shin", supportsLaterality: true),
                BodyPart("ankle", "Ankle", supportsLaterality: true),
                BodyPart("foot", "Foot", supportsLaterality: true),
                BodyPart(
                    "toes",
                    "Toes",
                    supportsLaterality: true,
                    digitOptions:
                    [
                        Digit("great", "Great toe", 1),
                        Digit("second", "Second toe", 2),
                        Digit("third", "Third toe", 3),
                        Digit("fourth", "Fourth toe", 4),
                        Digit("fifth", "Fifth toe", 5)
                    ]),
                BodyPart("heel", "Heel", supportsLaterality: true),
                BodyPart("arch", "Arch", supportsLaterality: true),
                BodyPart("achilles-tendon", "Achilles tendon", supportsLaterality: true)),

            Group("neurological-systemic-focus", "Neurological / Systemic Focus (If applicable)", IntakeBodyPartGroupKind.SystemicFocus, 8,
                BodyPart("nerves", "Nerves", supportsLaterality: true, sourceNote: "Source example: sciatic nerve."),
                BodyPart("balance-systems", "Balance systems (vestibular, proprioceptive)"),
                BodyPart("coordination-motor-planning", "Coordination and motor planning"),
                BodyPart("circulatory-lymphatic-regions", "Circulatory and lymphatic regions"))
        };

        return new IntakeReferenceCatalogDto
        {
            Version = "2026-03-30",
            BodyPartGroups = bodyPartGroups,
            Medications =
            [
                Medication(1, "lipitor-atorvastatin", "Lipitor / Atorvastatin", "Lipitor", "Atorvastatin"),
                Medication(2, "synthroid-levothyroxine", "Synthroid / Levothyroxine", "Synthroid", "Levothyroxine"),
                Medication(3, "norvasc-amlodipine", "Norvasc / Amlodipine", "Norvasc", "Amlodipine"),
                Medication(4, "glucophage-metformin", "Glucophage / Metformin", "Glucophage", "Metformin"),
                Medication(5, "zocor-simvastatin", "Zocor / Simvastatin", "Zocor", "Simvastatin"),
                Medication(6, "prilosec-omeprazole", "Prilosec / Omeprazole", "Prilosec", "Omeprazole"),
                Medication(7, "zithromax-azithromycin", "Zithromax / Azithromycin", "Zithromax", "Azithromycin"),
                Medication(8, "amoxil-amoxicillin", "Amoxil / Amoxicillin", "Amoxil", "Amoxicillin"),
                Medication(9, "hydrodiuril-hydrochlorothiazide", "HydroDiuril / Hydrochlorothiazide", "HydroDiuril", "Hydrochlorothiazide"),
                Medication(10, "zoloft-sertraline", "Zoloft / Sertraline", "Zoloft", "Sertraline"),
                Medication(11, "lasix-furosemide", "Lasix / Furosemide", "Lasix", "Furosemide"),
                Medication(12, "prozac-fluoxetine", "Prozac / Fluoxetine", "Prozac", "Fluoxetine"),
                Medication(13, "crestor-rosuvastatin", "Crestor / Rosuvastatin", "Crestor", "Rosuvastatin"),
                Medication(14, "plavix-clopidogrel", "Plavix / Clopidogrel", "Plavix", "Clopidogrel"),
                Medication(15, "neurontin-gabapentin", "Neurontin / Gabapentin", "Neurontin", "Gabapentin"),
                Medication(16, "tenormin-atenolol", "Tenormin / Atenolol", "Tenormin", "Atenolol"),
                Medication(17, "xanax-alprazolam", "Xanax / Alprazolam", "Xanax", "Alprazolam"),
                Medication(18, "vicodin-hydrocodone-acetaminophen", "Vicodin / Hydrocodone/Acetaminophen", "Vicodin", "Hydrocodone/Acetaminophen", isCombinationMedication: true),
                Medication(19, "lopressor-metoprolol", "Lopressor / Metoprolol", "Lopressor", "Metoprolol"),
                Medication(20, "ambien-zolpidem", "Ambien / Zolpidem", "Ambien", "Zolpidem"),
                Medication(21, "augmentin-amoxicillin-clavulanate", "Augmentin / Amoxicillin/Clavulanate", "Augmentin", "Amoxicillin/Clavulanate", isCombinationMedication: true),
                Medication(22, "advair-diskus-fluticasone-salmeterol", "Advair Diskus / Fluticasone/Salmeterol", "Advair Diskus", "Fluticasone/Salmeterol", isCombinationMedication: true),
                Medication(23, "nexium-esomeprazole", "Nexium / Esomeprazole", "Nexium", "Esomeprazole"),
                Medication(24, "lexapro-escitalopram", "Lexapro / Escitalopram", "Lexapro", "Escitalopram"),
                Medication(25, "proair-hfa-albuterol", "ProAir HFA / Albuterol", "ProAir HFA", "Albuterol"),
                Medication(26, "lantus-insulin-glargine", "Lantus / Insulin Glargine", "Lantus", "Insulin Glargine"),
                Medication(27, "effexor-xr-venlafaxine", "Effexor XR / Venlafaxine", "Effexor XR", "Venlafaxine"),
                Medication(28, "wellbutrin-bupropion", "Wellbutrin / Bupropion", "Wellbutrin", "Bupropion"),
                Medication(29, "flonase-fluticasone", "Flonase / Fluticasone", "Flonase", "Fluticasone"),
                Medication(30, "celebrex-celecoxib", "Celebrex / Celecoxib", "Celebrex", "Celecoxib"),
                Medication(31, "spiriva-tiotropium", "Spiriva / Tiotropium", "Spiriva", "Tiotropium"),
                Medication(32, "seroquel-quetiapine", "Seroquel / Quetiapine", "Seroquel", "Quetiapine"),
                Medication(33, "tramadol-ultram", "Tramadol / Ultram", "Ultram", "Tramadol", isSourceOrderReversed: true),
                Medication(34, "coumadin-warfarin", "Coumadin / Warfarin", "Coumadin", "Warfarin"),
                Medication(35, "paxil-paroxetine", "Paxil / Paroxetine", "Paxil", "Paroxetine"),
                Medication(36, "klonopin-clonazepam", "Klonopin / Clonazepam", "Klonopin", "Clonazepam"),
                Medication(37, "ativan-lorazepam", "Ativan / Lorazepam", "Ativan", "Lorazepam"),
                Medication(38, "lyrica-pregabalin", "Lyrica / Pregabalin", "Lyrica", "Pregabalin"),
                Medication(39, "cymbalta-duloxetine", "Cymbalta / Duloxetine", "Cymbalta", "Duloxetine"),
                Medication(40, "fosamax-alendronate", "Fosamax / Alendronate", "Fosamax", "Alendronate"),
                Medication(41, "zyrtec-cetirizine", "Zyrtec / Cetirizine", "Zyrtec", "Cetirizine"),
                Medication(42, "claritin-loratadine", "Claritin / Loratadine", "Claritin", "Loratadine"),
                Medication(43, "singulair-montelukast", "Singulair / Montelukast", "Singulair", "Montelukast"),
                Medication(44, "pravachol-pravastatin", "Pravachol / Pravastatin", "Pravachol", "Pravastatin"),
                Medication(45, "diovan-valsartan", "Diovan / Valsartan", "Diovan", "Valsartan"),
                Medication(46, "levaquin-levofloxacin", "Levaquin / Levofloxacin", "Levaquin", "Levofloxacin"),
                Medication(47, "protonix-pantoprazole", "Protonix / Pantoprazole", "Protonix", "Pantoprazole"),
                Medication(48, "toprol-xl-metoprolol-succinate", "Toprol XL / Metoprolol Succinate", "Toprol XL", "Metoprolol Succinate"),
                Medication(49, "actos-pioglitazone", "Actos / Pioglitazone", "Actos", "Pioglitazone"),
                Medication(50, "zestril-lisinopril", "Zestril / Lisinopril", "Zestril", "Lisinopril")
            ],
            PainDescriptors =
            [
                PainDescriptor(1, "sharp"),
                PainDescriptor(2, "stabbing"),
                PainDescriptor(3, "shooting"),
                PainDescriptor(4, "burning"),
                PainDescriptor(5, "aching"),
                PainDescriptor(6, "throbbing"),
                PainDescriptor(7, "dull"),
                PainDescriptor(8, "cramping"),
                PainDescriptor(9, "gnawing"),
                PainDescriptor(10, "tingling"),
                PainDescriptor(11, "electric-like"),
                PainDescriptor(12, "radiating"),
                PainDescriptor(13, "pulsing"),
                PainDescriptor(14, "searing"),
                PainDescriptor(15, "heavy"),
                PainDescriptor(16, "squeezing"),
                PainDescriptor(17, "tight"),
                PainDescriptor(18, "prickling"),
                PainDescriptor(19, "cutting"),
                PainDescriptor(20, "pounding")
            ]
        };
    }

    private static IntakeBodyPartGroupDto Group(
        string id,
        string title,
        IntakeBodyPartGroupKind kind,
        int displayOrder,
        params IntakeBodyPartItemDto[] items)
    {
        return new IntakeBodyPartGroupDto
        {
            Id = id,
            Title = title,
            Kind = kind,
            DisplayOrder = displayOrder,
            Items = items.Select((item, index) => new IntakeBodyPartItemDto
            {
                Id = item.Id,
                Label = item.Label,
                GroupId = id,
                GroupTitle = title,
                GroupKind = kind,
                GroupDisplayOrder = displayOrder,
                DisplayOrder = index + 1,
                SupportsLaterality = item.SupportsLaterality,
                SupportsDigitSelection = item.SupportsDigitSelection,
                DigitOptions = item.DigitOptions,
                SourceNote = item.SourceNote
            }).ToArray()
        };
    }

    private static IntakeBodyPartItemDto BodyPart(
        string id,
        string label,
        bool supportsLaterality = false,
        IReadOnlyList<IntakeDigitOptionDto>? digitOptions = null,
        string? sourceNote = null)
    {
        return new IntakeBodyPartItemDto
        {
            Id = id,
            Label = label,
            SupportsLaterality = supportsLaterality,
            SupportsDigitSelection = digitOptions is { Count: > 0 },
            DigitOptions = digitOptions ?? Array.Empty<IntakeDigitOptionDto>(),
            SourceNote = sourceNote
        };
    }

    private static IntakeDigitOptionDto Digit(string id, string label, int displayOrder)
    {
        return new IntakeDigitOptionDto
        {
            Id = id,
            Label = label,
            DisplayOrder = displayOrder
        };
    }

    private static IntakeMedicationItemDto Medication(
        int displayOrder,
        string id,
        string displayLabel,
        string brandName,
        string genericName,
        bool isCombinationMedication = false,
        bool isSourceOrderReversed = false)
    {
        return new IntakeMedicationItemDto
        {
            Id = id,
            DisplayLabel = displayLabel,
            BrandName = brandName,
            GenericName = genericName,
            IsCombinationMedication = isCombinationMedication,
            IsSourceOrderReversed = isSourceOrderReversed,
            DisplayOrder = displayOrder
        };
    }

    private static IntakePainDescriptorItemDto PainDescriptor(int displayOrder, string label)
    {
        return new IntakePainDescriptorItemDto
        {
            Id = label,
            Label = label switch
            {
                "electric-like" => "Electric-like",
                _ => char.ToUpperInvariant(label[0]) + label[1..]
            },
            DisplayOrder = displayOrder
        };
    }
}
