using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class WorkspaceReferenceCatalogService(IOutcomeMeasureRegistry outcomeMeasureRegistry)
    : IWorkspaceReferenceCatalogService
{
    private const string CervicalGoalSource = "docs/clinicrefdata/C-spine limitations_objective_Goals.md";
    private const string LumbarGoalSource = "docs/clinicrefdata/LBP limitations_object_smart goals.md";
    private const string LowerExtremityGoalSource = "docs/clinicrefdata/LE limitations_objectives_Goals.md";
    private const string PelvicFloorGoalSource = "docs/clinicrefdata/Pelvic Floor limitations_objectives_Goals.md";
    private const string UpperExtremityGoalSource = "Goals_UELimitations_SmartGoals.docx.md";
    private const string NormalRomSource = "docs/clinicrefdata/Normal ROM Measurements.md";
    private const string SpecialTestsSource = "docs/clinicrefdata/List of commonly used Special test.md";
    private const string OutcomeMeasuresSource = "docs/clinicrefdata/List of functional outcome measures.md";
    private const string ExercisesSource = "docs/clinicrefdata/Exercises.md";
    private const string JointMobilitySource = "docs/clinicrefdata/Joint mobility and MMT.md";
    private const string TreatmentInterventionsSource = "docs/clinicrefdata/what-generally-was-worked-on.md";
    private const string TreatmentFocusSource = "docs/clinicrefdata/what-was-specifically-worked-on.md";
    private const string TenderMusclesSource = "docs/clinicrefdata/Muscles TTP.md";

    private static readonly Lazy<IReadOnlyDictionary<BodyPart, BodyRegionCatalog>> Catalogs =
        new(BuildCatalogs);

    private static readonly Lazy<WorkspaceLookupReferenceDataAsset> LookupCatalog =
        new(() => EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<WorkspaceLookupReferenceDataAsset>(
            "PTDoc.Application.Data.WorkspaceLookupReferenceData.json"));

    private static readonly Lazy<IReadOnlyList<SearchableCodeLookupEntry>> SourceIcd10Codes =
        new(() => BuildLookupEntries(LookupCatalog.Value.Icd10Codes, LookupCatalog.Value.Icd10Provenance));

    private static readonly Lazy<IReadOnlyList<SearchableCodeLookupEntry>> SourceCptCodes =
        new(() => BuildLookupEntries(
            LookupCatalog.Value.CptCodes,
            LookupCatalog.Value.CptProvenance,
            LookupCatalog.Value.DefaultCptModifierOptions,
            LookupCatalog.Value.DefaultPtSuggestedModifiers));

    private static readonly IReadOnlyList<string> SharedTreatmentInterventions =
    [
        "Range of Motion (ROM) - active, passive, and active-assistive",
        "Joint Mobilization - grade I-V, Maitland or Kaltenborn techniques",
        "Soft Tissue Mobilization - myofascial release, trigger point therapy",
        "Scar Tissue Mobilization - post-op or injury",
        "Stretching - static, dynamic, PNF",
        "Contracture Management",
        "Muscle Strengthening - isolated or functional",
        "Core Stabilization - lumbar, pelvic, and trunk muscles",
        "Endurance Training - aerobic conditioning, pacing strategies",
        "Functional Strength Training - bodyweight or resistance-based for daily tasks",
        "Neuromuscular Re-education - retraining movement patterns",
        "Postural Training - static and dynamic",
        "Ergonomic Education - workplace and home setup",
        "Body Mechanics Retraining - lifting, bending, squatting",
        "Spinal Alignment & Stabilization",
        "Static and Dynamic Balance Training",
        "Vestibular Rehab - BPPV, gaze stabilization, habituation",
        "Fall Prevention - strategies and balance progressions",
        "Proprioceptive Training - joint position sense, barefoot tasks",
        "Coordination & Motor Control - UE/LE control, fine motor skills",
        "Gait Training - with/without assistive devices",
        "Stair Training",
        "Transfer Training - bed, chair, toilet, car",
        "Community Mobility Training - curbs, uneven terrain",
        "Energy Conservation & Pacing",
        "Manual Therapy - massage, mobilization, joint distraction",
        "Modalities - heat, cold, TENS, ultrasound",
        "Kinesiotaping / Strapping",
        "Pain Neuroscience Education",
        "Positioning for Comfort or Offloading",
        "Functional Task Training - reaching, lifting, getting off floor",
        "Work Simulation & Ergonomic Training",
        "ADL Retraining - self-care, dressing, grooming",
        "Rehabilitation for Return to Work or Sport",
        "Home Exercise Program (HEP) Development & Instruction",
        "Pelvic Floor Therapy - incontinence, pelvic pain, prolapse",
        "Post-Surgical Rehab - total joints, rotator cuff, spinal surgeries",
        "Chronic Condition Management - arthritis, MS, Parkinson's",
        "Neurological Rehab - stroke, balance disorders, post-concussion",
        "Pediatric Milestone Development - gross motor coordination"
    ];

    private static readonly IReadOnlyList<string> SharedMmtGrades =
    [
        "0 - Zero - No visible or palpable contraction",
        "1- - Trace minus - Slight flicker of contraction, barely perceptible",
        "1 - Trace - Palpable or visible contraction, no joint movement",
        "1+ - Trace plus - Slight contraction with minimal movement, but not full ROM in gravity-eliminated",
        "2- - Poor minus - Partial ROM in a gravity-eliminated position",
        "2 - Poor - Full ROM in a gravity-eliminated position",
        "2+ - Poor plus - Full ROM in gravity-eliminated and initiates slight movement against gravity",
        "3- - Fair minus - Greater than 50% but not full ROM against gravity",
        "3 - Fair - Full ROM against gravity, no resistance",
        "3+ - Fair plus - Full ROM against gravity, minimal resistance",
        "4- - Good minus - Full ROM against gravity, less than moderate resistance",
        "4 - Good - Full ROM against gravity, moderate resistance",
        "4+ - Good plus - Full ROM against gravity, nearly strong resistance",
        "5 - Normal - Full ROM against gravity, maximal resistance without breaking"
    ];

    private static readonly IReadOnlyList<string> SharedJointMobilityGrades =
    [
        "0 - No movement - Ankylosed (fused joint)",
        "1 - Considerably hypomobile - Severe restriction in joint play",
        "2 - Slightly hypomobile - Mild restriction",
        "3 - Normal mobility - Normal joint play",
        "4 - Slightly hypermobile - Mildly excessive motion",
        "5 - Considerably hypermobile - Significantly excessive motion",
        "6 - Unstable - Pathological hypermobility or instability"
    ];

    public BodyRegionCatalog GetBodyRegionCatalog(BodyPart bodyPart)
    {
        if (Catalogs.Value.TryGetValue(bodyPart, out var catalog))
        {
            return CloneCatalog(catalog, bodyPart);
        }

        return CloneCatalog(BuildMissingCatalog(bodyPart), bodyPart);
    }

    public IReadOnlyList<CodeLookupEntry> SearchIcd10(string? query, int take = 20) =>
        SearchCodes(SourceIcd10Codes.Value, query, take);

    public IReadOnlyList<CodeLookupEntry> SearchCpt(string? query, int take = 20) =>
        SearchCodes(SourceCptCodes.Value, query, take);

    private static IReadOnlyList<CodeLookupEntry> SearchCodes(
        IReadOnlyList<SearchableCodeLookupEntry> source,
        string? query,
        int take)
    {
        var trimmed = query?.Trim();
        var effectiveTake = take <= 0 ? 20 : Math.Min(take, 100);

        IEnumerable<SearchableCodeLookupEntry> results = source;
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            results = results.Where(entry =>
                entry.Entry.Code.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                entry.Entry.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                entry.SearchTerms.Any(term => term.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));
        }

        return results
            .Take(effectiveTake)
            .Select(searchable => new CodeLookupEntry
            {
                Code = searchable.Entry.Code,
                Description = searchable.Entry.Description,
                Source = searchable.Entry.Source,
                Provenance = CloneProvenance(searchable.Entry.Provenance),
                IsCompleteLibrary = searchable.Entry.IsCompleteLibrary,
                ModifierOptions = [.. searchable.Entry.ModifierOptions],
                SuggestedModifiers = [.. searchable.Entry.SuggestedModifiers],
                ModifierSource = searchable.Entry.ModifierSource
            })
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<SearchableCodeLookupEntry> BuildLookupEntries(
        IReadOnlyCollection<WorkspaceLookupCodeAsset> source,
        ReferenceDataProvenance provenance,
        IReadOnlyCollection<string>? defaultModifierOptions = null,
        IReadOnlyCollection<string>? defaultSuggestedModifiers = null)
    {
        var documentPath = provenance.DocumentPath;

        return source
            .Select(entry => new SearchableCodeLookupEntry
            {
                Entry = new CodeLookupEntry
                {
                    Code = entry.Code,
                    Description = entry.Description,
                    Source = documentPath,
                    Provenance = CloneProvenance(provenance),
                    IsCompleteLibrary = entry.IsCompleteLibrary,
                    ModifierOptions = entry.ModifierOptions.Count > 0
                        ? [.. entry.ModifierOptions]
                        : [.. (defaultModifierOptions ?? Array.Empty<string>())],
                    SuggestedModifiers = entry.SuggestedModifiers.Count > 0
                        ? [.. entry.SuggestedModifiers]
                        : [.. (defaultSuggestedModifiers ?? Array.Empty<string>())],
                    ModifierSource = (entry.ModifierOptions.Count > 0 || (defaultModifierOptions?.Count ?? 0) > 0)
                        ? documentPath
                        : null
                },
                SearchTerms = entry.SearchTerms
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .Select(term => term.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList()
            .AsReadOnly();
    }

    private static ReferenceDataProvenance? CloneProvenance(ReferenceDataProvenance? provenance)
        => provenance is null
            ? null
            : new ReferenceDataProvenance
            {
                DocumentPath = provenance.DocumentPath,
                Version = provenance.Version,
                Notes = provenance.Notes
            };

    private sealed class SearchableCodeLookupEntry
    {
        public CodeLookupEntry Entry { get; init; } = new();
        public List<string> SearchTerms { get; init; } = new();
    }

    private static IReadOnlyDictionary<BodyPart, BodyRegionCatalog> BuildCatalogs()
    {
        var cervical = CreateCatalog(
            BodyPart.Cervical,
            CervicalGoalSource,
            CervicalGoalSource,
            new List<CatalogCategory>
            {
                Category("Self-Care / ADLs",
                    "Unable to wash or shampoo hair without pain or assistance",
                    "Unable to brush or style hair due to limited range of motion",
                    "Unable to shave or apply makeup without neck discomfort",
                    "Unable to put on shirts or jackets that require overhead motion",
                    "Unable to turn head to check appearance in mirror comfortably",
                    "Unable to sleep comfortably due to neck pain or positioning issues",
                    "Unable to maintain head posture during meals without strain"),
                Category("Mobility & Driving",
                    "Unable to turn head fully to look over shoulder while backing up",
                    "Unable to check blind spots while driving without pain or limitation",
                    "Unable to look side-to-side safely when crossing streets",
                    "Unable to walk in crowded areas due to reduced head mobility or awareness",
                    "Unable to maintain balance during quick movements due to vestibular limitations",
                    "Unable to bend over or look down for extended periods"),
                Category("Work-Related Activities",
                    "Unable to sit or stand for prolonged periods at a desk without neck pain",
                    "Unable to look up or down at monitor/screen for extended durations",
                    "Unable to hold phone between ear and shoulder due to discomfort",
                    "Unable to lift or carry items without neck strain",
                    "Unable to reach overhead or forward without pain or limitation",
                    "Unable to read or write with prolonged downward gaze"),
                Category("Household Activities",
                    "Unable to look up to access or reach high shelves",
                    "Unable to perform cleaning tasks involving overhead or downward motions",
                    "Unable to carry heavy laundry baskets or groceries without neck pain",
                    "Unable to make beds or change linens due to restricted movement",
                    "Unable to perform gardening or yard tasks that involve bending or reaching"),
                Category("Recreational & Leisure Activities",
                    "Unable to participate in sports requiring rapid head movements",
                    "Unable to read or use electronic devices for long periods comfortably",
                    "Unable to watch TV or use computer without ergonomic discomfort",
                    "Unable to dance or follow exercise routines involving head turning",
                    "Unable to play musical instruments without strain"),
                Category("Other Functional Impacts",
                    "Unable to focus well due to neck pain or headache interference",
                    "Unable to achieve restful sleep due to poor pillow support or neck positioning",
                    "Unable to sustain upright posture without excessive fatigue from compensation",
                    "Unable to maintain sensation in arms consistently due to nerve compression")
            },
            new List<CatalogCategory>
            {
                Category("Self-Care Goals",
                    "Patient will wash or shampoo hair independently without pain >=3x/week within 4 weeks.",
                    "Patient will brush or style hair using both arms with >=75% normal range of motion in 3 weeks.",
                    "Patient will apply makeup or shave without neck discomfort for 5 continuous minutes within 2 weeks."),
                Category("Mobility & Driving Goals",
                    "Patient will rotate head >=60 degrees to check over shoulder while seated within 3 weeks.",
                    "Patient will check blind spots while driving without neck pain during simulated driving task in 3 weeks.",
                    "Patient will complete quick turns and movements while maintaining balance in 4 weeks."),
                Category("Posture & Endurance Goals",
                    "Patient will sit or stand at a desk for 45 minutes without neck pain within 3 weeks.",
                    "Patient will maintain neck-neutral posture while looking at monitor for 30 minutes within 3 weeks.",
                    "Patient will sustain upright seated posture for 45 minutes with <3/10 fatigue in neck or upper back within 3 weeks.")
            },
            specialTests: new List<string>
            {
                "Spurling's Test - Cervical radiculopathy",
                "Distraction Test - Nerve root compression",
                "Vertebral Artery Test - Vertebrobasilar insufficiency",
                "Sharp-Purser Test - Atlantoaxial instability",
                "Lhermitte's Sign - Cervical cord dysfunction"
            },
            outcomeMeasures: new List<string>
            {
                "NDI - Neck Disability Index",
                "PSFS - Patient-Specific Functional Scale",
                "VAS/NPRS - Pain rating scales"
            },
            normalRom: new List<string>
            {
                "Flexion: 0-45 degrees",
                "Extension: 0-45 degrees",
                "Lateral Flexion: 0-45 degrees",
                "Rotation: 0-60 degrees"
            },
            tenderMuscles: new List<string>
            {
                "Sternocleidomastoid",
                "Scalenes (anterior, middle, posterior)",
                "Splenius capitis and cervicis",
                "Suboccipital muscles",
                "Upper trapezius",
                "Levator scapulae"
            },
            exercises: new List<string>
            {
                "Chin tucks",
                "Upper trapezius stretch",
                "Levator scapulae stretch",
                "Cervical isometrics",
                "Scapular retraction with band"
            },
            treatmentFocuses: new List<string>
            {
                "Cervical joint mobility (flexion/extension/rotation/side-bend)",
                "Cervical arthrokinematics (facet glide, OA joint)",
                "Cervical traction (manual/mechanical)",
                "Deep neck flexor activation",
                "Cervical proprioception",
                "Cervicogenic headache treatment",
                "Thoracic mobility influence on cervical loading"
            });

        var lumbar = CreateCatalog(
            BodyPart.Lumbar,
            LumbarGoalSource,
            LumbarGoalSource,
            new List<CatalogCategory>
            {
                Category("Mobility & Movement",
                    "Unable to walk long distances or on uneven surfaces",
                    "Unable to stand for prolonged periods",
                    "Unable to sit for prolonged periods",
                    "Unable to transition easily between sitting and standing",
                    "Unable to lie down or change positions in bed comfortably",
                    "Unable to roll over in bed without pain or difficulty",
                    "Unable to bend forward or backward without discomfort",
                    "Unable to twist or rotate the trunk fully",
                    "Unable to get in and out of bed, car, or bathtub without assistance",
                    "Unable to climb stairs or curbs safely or comfortably"),
                Category("Self-Care / ADLs",
                    "Unable to dress lower body independently",
                    "Unable to bathe effectively",
                    "Unable to perform toileting tasks with ease",
                    "Unable to groom at the sink due to difficulty standing or leaning",
                    "Unable to reach overhead for clothing or hygiene items",
                    "Unable to pick objects up from the floor safely"),
                Category("Household Activities",
                    "Unable to vacuum, sweep, or mop without discomfort",
                    "Unable to make the bed without pain or limitations",
                    "Unable to lift laundry baskets comfortably",
                    "Unable to carry groceries or heavy items safely",
                    "Unable to reach into low cabinets or drawers",
                    "Unable to cook while standing or lifting pots and pans",
                    "Unable to perform gardening or yard work"),
                Category("Work-Related Tasks",
                    "Unable to sit at a desk or workstation for long periods",
                    "Unable to lift, push, or pull objects without discomfort",
                    "Unable to reach or bend repeatedly without symptoms",
                    "Unable to drive for work due to pain or limited mobility",
                    "Unable to stand at a workbench or counter for long durations",
                    "Unable to operate machinery or tools requiring trunk control",
                    "Unable to walk or stand throughout a shift comfortably"),
                Category("Recreation & Community",
                    "Unable to exercise or participate in sports without pain",
                    "Unable to run, jog, or jump comfortably",
                    "Unable to bike due to posture or spinal discomfort",
                    "Unable to attend social events or sit through performances comfortably",
                    "Unable to use public transportation easily",
                    "Unable to get into or out of vehicles without difficulty"),
                Category("Other Impacts",
                    "Unable to sleep through the night due to pain or positioning issues",
                    "Unable to concentrate fully due to chronic pain",
                    "Unable to engage in social activities due to physical limitations",
                    "Unable to maintain previous level of independence or lifestyle",
                    "Unable to maintain safe gait due to fall risk or altered movement")
            },
            new List<CatalogCategory>
            {
                Category("Mobility Goals",
                    "Patient will ambulate 500 feet independently with a normalized gait pattern and pain <=2/10 within 8 weeks.",
                    "Patient will improve walking tolerance to 20 minutes continuously with pain <=3/10 within 6 weeks.",
                    "Patient will achieve sit-to-stand transfers independently without upper-extremity support within 4 weeks."),
                Category("Strength & Endurance Goals",
                    "Patient will complete a 30-second sit-to-stand test meeting age-based norms within 6 weeks.",
                    "Patient will lift and carry 10 lbs for 50 feet with proper mechanics and pain <=2/10 within 8 weeks.",
                    "Patient will improve core stability to maintain a 60-second modified plank within 8 weeks."),
                Category("Daily Function Goals",
                    "Patient will complete household chores for 20 minutes with only one rest break within 6 weeks.",
                    "Patient will tolerate sitting for 60 minutes with appropriate posture and pain <=3/10 within 6 weeks.",
                    "Patient will perform a 10-minute home exercise program independently with correct form within 3 weeks.")
            },
            specialTests: new List<string>
            {
                "Straight Leg Raise (SLR) - Lumbar radiculopathy/sciatica",
                "Slump Test - Neural tension",
                "FABER (Patrick's) Test - SI joint or hip pathology",
                "Gaenslen's Test - SI joint dysfunction",
                "Prone Instability Test - Lumbar instability",
                "Quadrant (Kemp's) Test - Facet joint involvement"
            },
            outcomeMeasures: new List<string>
            {
                "ODI - Oswestry Disability Index",
                "Roland-Morris Disability Questionnaire",
                "PSFS - Patient-Specific Functional Scale",
                "FABQ - Fear-Avoidance Beliefs Questionnaire"
            },
            normalRom: new List<string>
            {
                "Flexion: 0-60 degrees",
                "Extension: 0-25 degrees",
                "Lateral Flexion (Side Bending): 0-25 degrees",
                "Rotation: 0-30 degrees"
            },
            tenderMuscles: new List<string>
            {
                "Erector spinae (iliocostalis, longissimus, spinalis)",
                "Quadratus lumborum",
                "Rectus abdominis",
                "External obliques",
                "Internal obliques",
                "Transverse abdominis"
            },
            exercises: new List<string>
            {
                "Pelvic tilts",
                "Supine knees-to-chest stretch",
                "Bird dog",
                "Bridges",
                "Prone press-ups"
            },
            treatmentFocuses: new List<string>
            {
                "Lumbar segmental mobility",
                "Core activation and bracing (transversus abdominis, multifidus)",
                "Flexion vs extension bias assessment",
                "Lumbopelvic rhythm",
                "Facet joint mobility",
                "Traction response",
                "Neural mobility",
                "Directional preference (McKenzie)",
                "Pain centralization strategies"
            });

        var lowerExtremity = CreateCatalog(
            BodyPart.Knee,
            LowerExtremityGoalSource,
            LowerExtremityGoalSource,
            new List<CatalogCategory>
            {
                Category("Mobility & Transfers",
                    "Unable to walk 1/4 mile without onset of leg pain",
                    "Unable to walk 500 feet without rest or assistive device",
                    "Unable to ascend or descend a flight of stairs reciprocally without using handrails",
                    "Unable to rise from chair without pushing off with hands",
                    "Unable to step over a 6-inch curb without stumbling or support",
                    "Unable to stand longer than 20 minutes without leaning or shifting weight",
                    "Unable to kneel and return to standing without support",
                    "Unable to get in/out of bed without use of hands or assistance",
                    "Unable to sit or rise from toilet without hand support"),
                Category("Self-Care / ADLs",
                    "Unable to put on pants/socks/shoes without frequent sitting",
                    "Unable to step in/out of tub without holding on",
                    "Unable to perform hygiene/clothing tasks without assistance",
                    "Unable to stand >5 minutes at sink without shifting weight",
                    "Unable to transition to floor without support"),
                Category("Work-Related Tasks",
                    "Unable to stand at workstation for >30 minutes",
                    "Unable to walk 1/2 mile at work without stopping",
                    "Unable to lift 10 lbs from floor without compensation",
                    "Unable to press pedals for >10 minutes without discomfort",
                    "Unable to climb ladder without skipping steps",
                    "Unable to squat and return to stand without support"),
                Category("Household Activities",
                    "Unable to cook for 15 minutes without leaning",
                    "Unable to vacuum or mop for >10 minutes",
                    "Unable to carry 10-lb basket up/down stairs",
                    "Unable to carry full bag >100 feet",
                    "Unable to mow, rake, or garden for >10 minutes"),
                Category("Leisure & Community",
                    "Unable to participate in sport play >10 minutes",
                    "Unable to hike >1/2 mile without stopping",
                    "Unable to bike >10 minutes without discomfort",
                    "Unable to dance 2-3 songs without rest",
                    "Unable to drive >30 minutes without needing break",
                    "Unable to carry 10-lb bag >200 feet",
                    "Unable to attend school or classes for a full period")
            },
            new List<CatalogCategory>
            {
                Category("Mobility & Gait Goals",
                    "Patient will ambulate 500 feet independently with a normalized gait pattern and pain <=2/10 within 8 weeks.",
                    "Patient will ascend and descend 12 stairs with a reciprocal pattern and use of 1 rail within 6 weeks.",
                    "Patient will demonstrate single-leg stance for 10 seconds bilaterally within 6 weeks."),
                Category("Strength & Function Goals",
                    "Patient will improve lower-extremity strength to 5/5 on MMT for functional tasks within 10 weeks.",
                    "Patient will perform 15 controlled step-ups on an 8-inch step within 8 weeks.",
                    "Patient will complete a 30-second sit-to-stand test meeting age-based norms within 6 weeks."),
                Category("Participation Goals",
                    "Patient will complete a full grocery-shopping trip with pain <=3/10 within 10 weeks.",
                    "Patient will return to work at modified duties for 4-hour shifts within 8 weeks.",
                    "Patient will resume recreational activity for at least 20 minutes, 3x/week within 10 weeks.")
            },
            specialTests: new List<string>
            {
                "FABER (Patrick's) Test - Hip/SI joint dysfunction",
                "FADIR Test - Hip impingement (FAI)",
                "Thomas Test - Hip flexor tightness",
                "Ober's Test - IT band tightness",
                "Trendelenburg Test - Gluteus medius weakness",
                "Scour Test - Intra-articular pathology",
                "Lachman's Test - ACL tear",
                "Anterior Drawer Test - ACL integrity",
                "Posterior Drawer Test - PCL integrity",
                "Valgus Stress Test - MCL integrity",
                "Varus Stress Test - LCL integrity",
                "McMurray's Test - Meniscal tear",
                "Apley's Compression/Distraction - Meniscal vs ligament injury",
                "Thessaly Test - Meniscus involvement",
                "Patellar Apprehension Test - Patellar instability",
                "Clarke's Test (Patellar Grind) - PFPS/chondromalacia",
                "Anterior Drawer Test (ankle) - ATFL sprain",
                "Talar Tilt Test - CFL or deltoid ligament injury",
                "Thompson Test - Achilles tendon rupture",
                "Morton's Test - Neuroma or metatarsalgia",
                "Homan's Sign - DVT screen",
                "Windlass Test - Plantar fasciitis"
            },
            outcomeMeasures: new List<string>
            {
                "LEFS - Lower Extremity Functional Scale",
                "HOOS - Hip disability and Osteoarthritis Outcome Score",
                "KOOS - Knee Injury and Osteoarthritis Outcome Score",
                "IKDC - International Knee Documentation Committee form",
                "Lysholm Knee Score",
                "Tegner Activity Scale",
                "FAAM - Foot and Ankle Ability Measure",
                "AOFAS - American Orthopaedic Foot & Ankle Society Score",
                "PSFS - Patient-Specific Functional Scale"
            },
            normalRom: new List<string>
            {
                "Hip Flexion: 0-120 degrees",
                "Hip Extension: 0-30 degrees",
                "Hip Abduction: 0-45 degrees",
                "Hip Adduction: 0-30 degrees",
                "Hip Internal Rotation: 0-45 degrees",
                "Hip External Rotation: 0-45 degrees",
                "Knee Flexion: 0-135 degrees",
                "Knee Extension: 0 degrees (hyperextension to -10 may be normal)",
                "Ankle Dorsiflexion: 0-20 degrees",
                "Ankle Plantarflexion: 0-50 degrees",
                "Ankle Inversion: 0-35 degrees",
                "Ankle Eversion: 0-15 degrees",
                "Great Toe MTP Flexion: 0-45 degrees",
                "Great Toe MTP Extension: 0-70 degrees"
            },
            tenderMuscles: new List<string>
            {
                "Gluteus maximus, medius, minimus",
                "Piriformis",
                "Tensor fasciae latae (TFL)",
                "Iliopsoas",
                "Sartorius",
                "Adductor group",
                "Hamstrings",
                "Quadriceps group",
                "Popliteus",
                "Gastrocnemius",
                "Soleus",
                "Tibialis anterior",
                "Tibialis posterior",
                "Peroneus longus and brevis",
                "Extensor hallucis longus",
                "Flexor hallucis longus",
                "Foot intrinsics"
            },
            exercises: new List<string>
            {
                "Clamshells",
                "Side-lying hip abduction",
                "Hip flexor stretch",
                "Glute bridges",
                "Standing hip extension with band",
                "Quad sets",
                "Straight leg raise",
                "Terminal knee extension with band",
                "Step-ups",
                "Wall sits",
                "Ankle alphabets",
                "Towel scrunches",
                "Calf raises",
                "Ankle dorsiflexion with band",
                "Single-leg balance",
                "Toe curls",
                "Arch lifts",
                "Marble pickups",
                "Plantar fascia stretch",
                "Short foot exercise"
            },
            treatmentFocuses: new List<string>
            {
                "Hip arthrokinematics",
                "Lateral pelvic control (Trendelenburg)",
                "Glute med/max activation and recruitment",
                "Hip flexor length and control",
                "Hip rotation control (IR/ER)",
                "Femoral head positioning",
                "SI joint mobility/stability",
                "Lumbopelvic rhythm coordination",
                "Piriformis syndrome management",
                "Patellar tracking and alignment",
                "Tibiofemoral arthrokinematics",
                "Quad control and activation",
                "Hamstring-quadriceps strength ratio",
                "Joint mobility (flexion/extension ROM)",
                "Knee valgus/varus dynamic control",
                "Meniscal loading tolerance",
                "Terminal knee extension",
                "Proximal control influence (hip to knee)",
                "Ankle dorsiflexion/plantarflexion mobility",
                "Subtalar inversion/eversion control",
                "Midfoot mobility/stiffness",
                "Toe flexor/extensor strength",
                "Ankle proprioception and balance",
                "Talocrural arthrokinematics",
                "Arch support and dynamic foot posture",
                "Gait pattern / push-off mechanics",
                "Achilles tendon loading"
            });

        var upperExtremity = CreateCatalog(
            BodyPart.Shoulder,
            UpperExtremityGoalSource,
            UpperExtremityGoalSource,
            new List<CatalogCategory>
            {
                Category("Self-Care / ADLs",
                    "Unable to brush teeth or hair with affected arm",
                    "Unable to reach back of head without pain or assistance",
                    "Unable to use both hands for grooming",
                    "Unable to wash entire face and upper body comfortably",
                    "Unable to reach certain areas due to pain or stiffness",
                    "Unable to lift arm overhead while showering",
                    "Unable to dress upper body without assistance",
                    "Unable to fasten buttons or bras independently",
                    "Unable to cut or scoop food without help",
                    "Unable to lift a full cup or glass with one hand",
                    "Unable to manage hygiene or clothing fully"),
                Category("Household Activities",
                    "Unable to prepare full meals without discomfort",
                    "Unable to lift pots or use knives independently",
                    "Unable to perform repetitive cleaning motions",
                    "Unable to wash dishes without rest breaks",
                    "Unable to carry a full laundry basket",
                    "Unable to lift items overhead",
                    "Unable to reach high shelves independently",
                    "Unable to complete bed-making without pain",
                    "Unable to rake, sweep, or dig without pain"),
                Category("Work-Related Tasks",
                    "Unable to type or use mouse comfortably",
                    "Unable to write more than a few lines",
                    "Unable to carry heavy or bulky items",
                    "Unable to reach overhead or across desk",
                    "Unable to use tools without pain or support",
                    "Unable to drive without pain or arm repositioning",
                    "Unable to hold phone or tablet comfortably",
                    "Unable to lift, push, or pull over 10 lbs"),
                Category("Mobility & Transfers",
                    "Unable to bear full weight through arms",
                    "Unable to use walker, cane, or crutches comfortably",
                    "Unable to push a wheelchair for extended distance",
                    "Unable to push up from a surface using both arms",
                    "Unable to rise from floor without help"),
                Category("Leisure & Recreation",
                    "Unable to throw, catch, or swing without pain",
                    "Unable to perform overhead sports movements",
                    "Unable to play an instrument for long sessions",
                    "Unable to use tools for gardening or crafting without pain",
                    "Unable to lift upper-body weights",
                    "Unable to carry bags or equipment without support"),
                Category("Childcare / Caregiving",
                    "Unable to lift or carry child over 15 lbs",
                    "Unable to perform diapering at floor level",
                    "Unable to manage bathing or dressing a child alone",
                    "Unable to push stroller for prolonged time",
                    "Unable to hold baby for extended periods"),
                Category("Community Participation",
                    "Unable to carry multiple grocery bags",
                    "Unable to push or pull heavy doors",
                    "Unable to twist or insert keys easily",
                    "Unable to hold phone to ear for long calls",
                    "Unable to handle coins or bills without dropping",
                    "Unable to hug or shake hands using affected arm")
            },
            goalCategories: new List<CatalogCategory>(),
            specialTests: new List<string>
            {
                "Hawkins-Kennedy Test - Impingement",
                "Neer Test - Impingement",
                "Empty Can (Jobe's) Test - Supraspinatus tear",
                "Drop Arm Test - Full-thickness rotator cuff tear",
                "Speed's Test - Biceps tendonitis",
                "Yergason's Test - Biceps tendon pathology",
                "Apprehension Test - Anterior instability",
                "Sulcus Sign - Inferior instability",
                "O'Brien's Test - SLAP lesion",
                "Cozen's Test - Lateral epicondylitis",
                "Mill's Test - Lateral epicondylitis",
                "Maudsley's Test - Lateral epicondylitis",
                "Golfer's Elbow Test - Medial epicondylitis",
                "Tinel's Sign (elbow) - Ulnar nerve irritation",
                "Phalen's Test - Carpal tunnel syndrome",
                "Tinel's Sign (wrist) - Median nerve irritation",
                "Finkelstein's Test - De Quervain's tenosynovitis",
                "Allen's Test - Vascular insufficiency",
                "Froment's Sign - Ulnar nerve palsy"
            },
            outcomeMeasures: new List<string>
            {
                "DASH - Disabilities of the Arm, Shoulder, and Hand",
                "QuickDASH - Shortened DASH",
                "SPADI - Shoulder Pain and Disability Index",
                "ASES - American Shoulder and Elbow Surgeons Score",
                "PRWE - Patient-Rated Wrist Evaluation",
                "Michigan Hand Outcomes Questionnaire"
            },
            normalRom: new List<string>
            {
                "Shoulder Flexion: 0-180 degrees",
                "Shoulder Extension: 0-60 degrees",
                "Shoulder Abduction: 0-180 degrees",
                "Shoulder Horizontal Abduction: 0-45 degrees",
                "Shoulder Horizontal Adduction: 0-135 degrees",
                "Shoulder Internal Rotation: 0-70 degrees",
                "Shoulder External Rotation: 0-90 degrees",
                "Elbow Flexion: 0-150 degrees",
                "Elbow Extension: 0 degrees",
                "Elbow Pronation: 0-80 degrees",
                "Elbow Supination: 0-80 degrees",
                "Wrist Flexion: 0-80 degrees",
                "Wrist Extension: 0-70 degrees",
                "Wrist Radial Deviation: 0-20 degrees",
                "Wrist Ulnar Deviation: 0-30 degrees",
                "MCP Flexion: 0-90 degrees",
                "MCP Extension: 0-45 degrees",
                "PIP Flexion: 0-100 degrees",
                "DIP Flexion: 0-90 degrees",
                "Thumb MCP Flexion: 0-50 degrees",
                "Thumb IP Flexion: 0-80 degrees",
                "Thumb Abduction (Palmar): 0-70 degrees"
            },
            tenderMuscles: new List<string>
            {
                "Deltoid (anterior, middle, posterior)",
                "Supraspinatus",
                "Infraspinatus",
                "Teres minor",
                "Subscapularis",
                "Teres major",
                "Pectoralis major and minor",
                "Biceps brachii",
                "Triceps brachii",
                "Coracobrachialis",
                "Latissimus dorsi",
                "Brachialis",
                "Brachioradialis",
                "Pronator teres",
                "Pronator quadratus",
                "Supinator",
                "Anconeus",
                "Flexor carpi radialis",
                "Flexor carpi ulnaris",
                "Palmaris longus",
                "Extensor carpi radialis longus and brevis",
                "Extensor carpi ulnaris",
                "Flexor digitorum superficialis and profundus",
                "Flexor pollicis longus and brevis",
                "Extensor digitorum",
                "Extensor pollicis longus and brevis",
                "Abductor pollicis longus",
                "Thenar and hypothenar muscles",
                "Lumbricals",
                "Interossei (palmar and dorsal)"
            },
            exercises: new List<string>
            {
                "Pendulum swings",
                "Wall slides",
                "Shoulder external rotation with resistance band",
                "Shoulder flexion with wand",
                "Serratus punches",
                "Biceps curls (light resistance)",
                "Triceps extensions",
                "Wrist flexor stretch",
                "Wrist extensor stretch",
                "Eccentric wrist curls",
                "Wrist circles",
                "Finger opposition drills",
                "Grip strengthening with putty or ball",
                "Wrist flexion/extension with light weights",
                "Thumb abduction with rubber band"
            },
            treatmentFocuses: new List<string>
            {
                "Glenohumeral arthrokinematics",
                "Scapulohumeral rhythm",
                "Rotator cuff strength and neuromuscular control",
                "Scapular stabilization",
                "Shoulder elevation pattern",
                "Shoulder IR/ER strength ratio",
                "Posterior capsule mobility",
                "Joint mobilization (Grades I-IV)",
                "Thoracic spine influence",
                "Elbow flexion/extension mobility",
                "Valgus stress control",
                "Joint arthrokinematics (radiohumeral, ulnohumeral)",
                "Grip strength and forearm muscle endurance",
                "Neural mobility (radial, ulnar, median)",
                "Epicondylitis protocols",
                "Wrist joint mobility",
                "Carpal glide techniques",
                "Median/ulnar nerve gliding",
                "Thumb opposition and stability (CMC joint)",
                "Grip strength and pinch strength",
                "Fine motor control and dexterity",
                "Tendon gliding exercises"
            },
            goalTemplateAvailabilityOverride: CatalogAvailability.Missing(
                $"{UpperExtremityGoalSource} contains clear limitation categories, but the supplied goal section appears to repeat cervical-style goals. Backend seeds source-backed limitations and derived goals only until the UE goal source is clarified."));

        var pelvicFloor = CreateCatalog(
            BodyPart.PelvicFloor,
            PelvicFloorGoalSource,
            PelvicFloorGoalSource,
            new List<CatalogCategory>
            {
                Category("Mobility & Transfers",
                    "Difficulty standing or walking for prolonged periods due to pelvic discomfort",
                    "Limited ability to climb stairs or ramps because of pelvic pain or instability",
                    "Difficulty transitioning from sitting to standing or vice versa without pain",
                    "Trouble getting in and out of bed or car due to pelvic pain or tightness",
                    "Difficulty squatting, kneeling, or bending due to pelvic floor muscle tension or pain"),
                Category("Self-Care / ADLs",
                    "Difficulty with toileting, including sitting on or rising from the toilet",
                    "Pain or discomfort during wiping or hygiene tasks post-toileting",
                    "Challenges with sexual activity, including pain during intercourse or inability to engage comfortably",
                    "Difficulty with bathing or showering due to pain when positioning or moving",
                    "Difficulty dressing lower body due to pelvic or hip discomfort"),
                Category("Bowel and Bladder Function",
                    "Urinary urgency, frequency, or incontinence impacting ability to perform tasks uninterrupted",
                    "Constipation or straining leading to pain and limited bowel movements",
                    "Difficulty controlling or delaying bowel movements",
                    "Pain during urination or bowel movements causing avoidance behaviors",
                    "Inability to complete toileting tasks independently due to pain or weakness"),
                Category("Work-Related Functional Limitations",
                    "Difficulty standing or sitting for prolonged periods at workstation due to pelvic discomfort",
                    "Reduced tolerance for lifting, bending, or carrying heavy objects because of pelvic pain or instability",
                    "Limited ability to perform repetitive tasks involving trunk or core muscles",
                    "Challenges operating machinery or driving due to pelvic or lower abdominal pain"),
                Category("Household and Leisure Activities",
                    "Difficulty performing household chores requiring bending, lifting, or prolonged standing",
                    "Avoidance or inability to participate in recreational activities such as exercise, sports, or dancing due to pelvic pain",
                    "Difficulty with gardening, yard work, or other activities requiring squatting or kneeling",
                    "Reduced ability to travel or attend social events due to pain flare-ups or urgency"),
                Category("Psychosocial and Emotional Impact",
                    "Increased anxiety or fear related to pain flare-ups or functional limitations",
                    "Avoidance of intimate or social interactions due to discomfort or embarrassment",
                    "Sleep disturbances due to pain or urgency impacting overall functioning")
            },
            new List<CatalogCategory>
            {
                Category("Mobility & Transfers Goals",
                    "Patient will stand for 10 minutes without pelvic pain increase during physical therapy session by week 3.",
                    "Patient will transition from sitting to standing independently with minimal pelvic discomfort in 2 weeks.",
                    "Patient will stand and walk for 30 minutes without pelvic pain flare-up in community setting within 8 weeks."),
                Category("Bowel and Bladder Goals",
                    "Patient will report decreased urinary urgency episodes by 30% within 3 weeks.",
                    "Patient will achieve successful timed toileting schedule 80% of the time in 2 weeks.",
                    "Patient will maintain continence with no leakage episodes during daily activities in 8 weeks."),
                Category("Participation Goals",
                    "Patient will tolerate sitting for 30 minutes at work with pelvic pain managed to <3/10 in 2 weeks.",
                    "Patient will perform full meal preparation standing for 20 minutes without pelvic pain in 6 weeks.",
                    "Patient will report improved quality of life related to pelvic pain management on validated questionnaire by 7 weeks.")
            },
            specialTests: new List<string>(),
            outcomeMeasures: new List<string>(),
            normalRom: new List<string>(),
            tenderMuscles: new List<string>
            {
                "Gluteus maximus, medius, minimus",
                "Piriformis",
                "Tensor fasciae latae (TFL)",
                "Iliopsoas",
                "Adductor group",
                "Hamstrings",
                "Quadratus lumborum",
                "Transverse abdominis"
            },
            exercises: new List<string>(),
            treatmentFocuses: new List<string>
            {
                "SI joint mobility/stability",
                "Sacral nutation/counternutation control",
                "Lumbopelvic rhythm during movement",
                "Core-lumbopelvic coordination",
                "Pubic symphysis stability",
                "Pelvic floor activation/dysfunction"
            });

        return new Dictionary<BodyPart, BodyRegionCatalog>
        {
            [BodyPart.Cervical] = cervical,
            [BodyPart.Lumbar] = lumbar,
            [BodyPart.Shoulder] = CloneForBodyPart(upperExtremity, BodyPart.Shoulder),
            [BodyPart.Elbow] = CloneForBodyPart(upperExtremity, BodyPart.Elbow),
            [BodyPart.Wrist] = CloneForBodyPart(upperExtremity, BodyPart.Wrist),
            [BodyPart.Hand] = CloneForBodyPart(upperExtremity, BodyPart.Hand),
            [BodyPart.Hip] = CloneForBodyPart(lowerExtremity, BodyPart.Hip),
            [BodyPart.Knee] = CloneForBodyPart(lowerExtremity, BodyPart.Knee),
            [BodyPart.Ankle] = CloneForBodyPart(lowerExtremity, BodyPart.Ankle),
            [BodyPart.Foot] = CloneForBodyPart(lowerExtremity, BodyPart.Foot),
            [BodyPart.PelvicFloor] = pelvicFloor
        };
    }

    private BodyRegionCatalog CloneCatalog(BodyRegionCatalog catalog, BodyPart requestedBodyPart)
    {
        var cloned = CloneForBodyPart(catalog, requestedBodyPart);
        var registryMeasures = outcomeMeasureRegistry
            .GetMeasuresForBodyPart(requestedBodyPart)
            .Select(definition => $"{definition.Abbreviation} - {definition.FullName}");

        var mergedOutcomeMeasures = MergeDistinct(cloned.OutcomeMeasureOptions, registryMeasures);
        if (mergedOutcomeMeasures.Count > 0)
        {
            cloned.OutcomeMeasures = cloned.OutcomeMeasures.IsAvailable
                ? cloned.OutcomeMeasures
                : CatalogAvailability.Available("Outcome registry fallback");
            cloned.OutcomeMeasureOptions = mergedOutcomeMeasures;
        }

        return cloned;
    }

    private static BodyRegionCatalog CreateCatalog(
        BodyPart bodyPart,
        string functionalLimitationsSource,
        string goalSource,
        List<CatalogCategory> functionalCategories,
        List<CatalogCategory> goalCategories,
        IReadOnlyCollection<string> specialTests,
        IReadOnlyCollection<string> outcomeMeasures,
        IReadOnlyCollection<string> normalRom,
        IReadOnlyCollection<string> tenderMuscles,
        IReadOnlyCollection<string> exercises,
        IReadOnlyCollection<string> treatmentFocuses,
        CatalogAvailability? goalTemplateAvailabilityOverride = null)
    {
        specialTests ??= Array.Empty<string>();
        outcomeMeasures ??= Array.Empty<string>();
        normalRom ??= Array.Empty<string>();
        tenderMuscles ??= Array.Empty<string>();
        exercises ??= Array.Empty<string>();
        treatmentFocuses ??= Array.Empty<string>();

        return new BodyRegionCatalog
        {
            BodyPart = bodyPart,
            FunctionalLimitations = CatalogAvailability.Available(functionalLimitationsSource),
            GoalTemplates = goalTemplateAvailabilityOverride ?? AvailabilityFrom(goalCategories, goalSource),
            SpecialTests = AvailabilityFrom(specialTests, SpecialTestsSource),
            OutcomeMeasures = AvailabilityFrom(outcomeMeasures, OutcomeMeasuresSource),
            NormalRangeOfMotion = AvailabilityFrom(normalRom, NormalRomSource),
            TenderMuscles = AvailabilityFrom(tenderMuscles, TenderMusclesSource),
            Exercises = AvailabilityFrom(exercises, ExercisesSource),
            TreatmentFocuses = AvailabilityFrom(treatmentFocuses, TreatmentFocusSource),
            TreatmentInterventions = AvailabilityFrom(SharedTreatmentInterventions, TreatmentInterventionsSource),
            JointMobilityAndMmt = CatalogAvailability.Available(JointMobilitySource),
            FunctionalLimitationCategories = functionalCategories,
            GoalTemplateCategories = goalCategories,
            SpecialTestsOptions = specialTests.ToList(),
            OutcomeMeasureOptions = outcomeMeasures.ToList(),
            NormalRangeOfMotionOptions = normalRom.ToList(),
            TenderMuscleOptions = tenderMuscles.ToList(),
            ExerciseOptions = exercises.ToList(),
            TreatmentFocusOptions = treatmentFocuses.ToList(),
            TreatmentInterventionOptions = SharedTreatmentInterventions.ToList(),
            MmtGradeOptions = SharedMmtGrades.ToList(),
            JointMobilityGradeOptions = SharedJointMobilityGrades.ToList()
        };
    }

    private static CatalogAvailability AvailabilityFrom<T>(IReadOnlyCollection<T> items, string sourceNotes)
    {
        return items is { Count: > 0 }
            ? CatalogAvailability.Available(sourceNotes)
            : CatalogAvailability.Missing($"No entries seeded from {sourceNotes}");
    }

    private static List<string> MergeDistinct(IEnumerable<string> first, IEnumerable<string> second)
    {
        return first
            .Concat(second)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BodyRegionCatalog BuildMissingCatalog(BodyPart bodyPart) => new()
    {
        BodyPart = bodyPart,
        FunctionalLimitations = CatalogAvailability.Missing("No validated source-backed library for this body region yet."),
        GoalTemplates = CatalogAvailability.Missing("No validated source-backed goal library for this body region yet."),
        AssistiveDevices = CatalogAvailability.Missing("Awaiting source document"),
        Comorbidities = CatalogAvailability.Missing("Awaiting source document"),
        SpecialTests = CatalogAvailability.Missing("No seeded special tests for this body region"),
        OutcomeMeasures = CatalogAvailability.Missing("No seeded outcome-measure mapping for this body region"),
        NormalRangeOfMotion = CatalogAvailability.Missing("No seeded ROM norms for this body region"),
        TenderMuscles = CatalogAvailability.Missing("No seeded tenderness reference for this body region"),
        Exercises = CatalogAvailability.Missing("No seeded exercise library for this body region"),
        TreatmentFocuses = CatalogAvailability.Missing("No seeded treatment-focus mapping for this body region"),
        TreatmentInterventions = CatalogAvailability.Available(TreatmentInterventionsSource),
        JointMobilityAndMmt = CatalogAvailability.Available(JointMobilitySource),
        TreatmentInterventionOptions = SharedTreatmentInterventions.ToList(),
        MmtGradeOptions = SharedMmtGrades.ToList(),
        JointMobilityGradeOptions = SharedJointMobilityGrades.ToList()
    };

    private static BodyRegionCatalog CloneForBodyPart(BodyRegionCatalog source, BodyPart bodyPart) => new()
    {
        BodyPart = bodyPart,
        FunctionalLimitations = source.FunctionalLimitations,
        GoalTemplates = source.GoalTemplates,
        AssistiveDevices = source.AssistiveDevices,
        Comorbidities = source.Comorbidities,
        SpecialTests = source.SpecialTests,
        OutcomeMeasures = source.OutcomeMeasures,
        NormalRangeOfMotion = source.NormalRangeOfMotion,
        TenderMuscles = source.TenderMuscles,
        Exercises = source.Exercises,
        TreatmentFocuses = source.TreatmentFocuses,
        TreatmentInterventions = source.TreatmentInterventions,
        JointMobilityAndMmt = source.JointMobilityAndMmt,
        FunctionalLimitationCategories = source.FunctionalLimitationCategories
            .Select(category => Category(category.Name, category.Items.ToArray()))
            .ToList(),
        GoalTemplateCategories = source.GoalTemplateCategories
            .Select(category => Category(category.Name, category.Items.ToArray()))
            .ToList(),
        AssistiveDeviceOptions = source.AssistiveDeviceOptions.ToList(),
        ComorbidityOptions = source.ComorbidityOptions.ToList(),
        SpecialTestsOptions = source.SpecialTestsOptions.ToList(),
        OutcomeMeasureOptions = source.OutcomeMeasureOptions.ToList(),
        NormalRangeOfMotionOptions = source.NormalRangeOfMotionOptions.ToList(),
        TenderMuscleOptions = source.TenderMuscleOptions.ToList(),
        ExerciseOptions = source.ExerciseOptions.ToList(),
        TreatmentFocusOptions = source.TreatmentFocusOptions.ToList(),
        TreatmentInterventionOptions = source.TreatmentInterventionOptions.ToList(),
        MmtGradeOptions = source.MmtGradeOptions.ToList(),
        JointMobilityGradeOptions = source.JointMobilityGradeOptions.ToList()
    };

    private static CatalogCategory Category(string name, params string[] items) => new()
    {
        Name = name,
        Items = items.ToList()
    };
}
