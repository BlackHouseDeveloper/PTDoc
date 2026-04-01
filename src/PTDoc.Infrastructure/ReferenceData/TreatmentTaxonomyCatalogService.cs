using PTDoc.Application.ReferenceData;

namespace PTDoc.Infrastructure.ReferenceData;

public sealed class TreatmentTaxonomyCatalogService : ITreatmentTaxonomyCatalogService
{
    private static readonly TreatmentTaxonomyCatalogDto Catalog = BuildCatalog();
    private static readonly Dictionary<string, TreatmentTaxonomyCategoryDto> CategoriesById = Catalog.Categories
        .ToDictionary(category => category.Id, StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string CategoryId, string ItemId), TreatmentTaxonomySelectionDto> SelectionsById = BuildSelections();

    public TreatmentTaxonomyCatalogDto GetCatalog() => Catalog;

    public TreatmentTaxonomyCategoryDto? GetCategory(string categoryId)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return null;
        }

        return CategoriesById.TryGetValue(categoryId, out var category) ? category : null;
    }

    public TreatmentTaxonomySelectionDto? ResolveSelection(string categoryId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(categoryId) || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        return SelectionsById.TryGetValue((categoryId, itemId), out var selection)
            ? new TreatmentTaxonomySelectionDto
            {
                CategoryId = selection.CategoryId,
                CategoryTitle = selection.CategoryTitle,
                CategoryKind = selection.CategoryKind,
                ItemId = selection.ItemId,
                ItemLabel = selection.ItemLabel
            }
            : null;
    }

    private static Dictionary<(string CategoryId, string ItemId), TreatmentTaxonomySelectionDto> BuildSelections()
    {
        var selections = new Dictionary<(string CategoryId, string ItemId), TreatmentTaxonomySelectionDto>();

        foreach (var category in Catalog.Categories)
        {
            foreach (var item in category.Items)
            {
                selections[(category.Id, item.Id)] = new TreatmentTaxonomySelectionDto
                {
                    CategoryId = category.Id,
                    CategoryTitle = category.Title,
                    CategoryKind = category.Kind,
                    ItemId = item.Id,
                    ItemLabel = item.Label
                };
            }
        }

        return selections;
    }

    private static TreatmentTaxonomyCatalogDto BuildCatalog()
    {
        return new TreatmentTaxonomyCatalogDto
        {
            Version = "2026-03-30",
            Categories =
            [
                Category("mobility-motion", "Mobility & Motion", TreatmentTaxonomyCategoryKind.GeneralDomain, 1,
                    Item("range-of-motion-rom", "Range of Motion (ROM) - active, passive, and active-assistive"),
                    Item("joint-mobilization", "Joint Mobilization - grade I-V, Maitland or Kaltenborn techniques"),
                    Item("soft-tissue-mobilization", "Soft Tissue Mobilization - myofascial release, trigger point therapy"),
                    Item("scar-tissue-mobilization", "Scar Tissue Mobilization - post-op or injury"),
                    Item("stretching", "Stretching - static, dynamic, PNF"),
                    Item("contracture-management", "Contracture Management")),

                Category("strength-endurance", "Strength & Endurance", TreatmentTaxonomyCategoryKind.GeneralDomain, 2,
                    Item("muscle-strengthening", "Muscle Strengthening - isolated or functional (e.g., quads, glutes)"),
                    Item("core-stabilization", "Core Stabilization - lumbar, pelvic, and trunk muscles"),
                    Item("endurance-training", "Endurance Training - aerobic conditioning, pacing strategies"),
                    Item("functional-strength-training", "Functional Strength Training - bodyweight or resistance-based for daily tasks"),
                    Item("neuromuscular-reeducation", "Neuromuscular Re-education - retraining movement patterns")),

                Category("posture-alignment", "Posture & Alignment", TreatmentTaxonomyCategoryKind.GeneralDomain, 3,
                    Item("postural-training", "Postural Training - static and dynamic"),
                    Item("ergonomic-education", "Ergonomic Education - workplace and home setup"),
                    Item("body-mechanics-retraining", "Body Mechanics Retraining - lifting, bending, squatting"),
                    Item("spinal-alignment-stabilization", "Spinal Alignment & Stabilization")),

                Category("balance-coordination", "Balance & Coordination", TreatmentTaxonomyCategoryKind.GeneralDomain, 4,
                    Item("static-dynamic-balance-training", "Static and Dynamic Balance Training"),
                    Item("vestibular-rehab", "Vestibular Rehab - BPPV, gaze stabilization, habituation"),
                    Item("fall-prevention", "Fall Prevention - strategies and balance progressions"),
                    Item("proprioceptive-training", "Proprioceptive Training - joint position sense, barefoot tasks"),
                    Item("coordination-motor-control", "Coordination & Motor Control - UE/LE control, fine motor skills")),

                Category("gait-functional-training", "Gait & Functional Training", TreatmentTaxonomyCategoryKind.GeneralDomain, 5,
                    Item("gait-training", "Gait Training - with/without assistive devices"),
                    Item("stair-training", "Stair Training"),
                    Item("transfer-training", "Transfer Training - bed, chair, toilet, car"),
                    Item("community-mobility-training", "Community Mobility Training - curbs, uneven terrain"),
                    Item("energy-conservation-pacing", "Energy Conservation & Pacing - especially in chronic fatigue conditions")),

                Category("pain-management", "Pain Management", TreatmentTaxonomyCategoryKind.GeneralDomain, 6,
                    Item("manual-therapy", "Manual Therapy - massage, mobilization, joint distraction"),
                    Item("modalities", "Modalities - heat, cold, TENS, ultrasound"),
                    Item("kinesiotaping-strapping", "Kinesiotaping / Strapping"),
                    Item("pain-neuroscience-education", "Pain Neuroscience Education"),
                    Item("positioning-offloading", "Positioning for Comfort or Offloading")),

                Category("function-daily-living", "Function & Daily Living", TreatmentTaxonomyCategoryKind.GeneralDomain, 7,
                    Item("functional-task-training", "Functional Task Training - reaching, lifting, getting off floor"),
                    Item("work-simulation-ergonomic-training", "Work Simulation & Ergonomic Training"),
                    Item("adl-retraining", "ADL Retraining - self-care, dressing, grooming"),
                    Item("return-to-work-or-sport", "Rehabilitation for Return to Work or Sport"),
                    Item("hep-development-instruction", "Home Exercise Program (HEP) Development & Instruction")),

                Category("specialty-focus-areas", "Specialty Focus Areas", TreatmentTaxonomyCategoryKind.GeneralDomain, 8,
                    Item("pelvic-floor-therapy", "Pelvic Floor Therapy - incontinence, pelvic pain, prolapse"),
                    Item("post-surgical-rehab", "Post-Surgical Rehab - total joints, rotator cuff, spinal surgeries"),
                    Item("chronic-condition-management", "Chronic Condition Management - arthritis, MS, Parkinson's"),
                    Item("neurological-rehab", "Neurological Rehab - stroke, balance disorders, post-concussion"),
                    Item("pediatric-milestone-development", "Pediatric Milestone Development - gross motor coordination")),

                Category("foot-ankle", "Foot & Ankle", TreatmentTaxonomyCategoryKind.BodyRegion, 9,
                    Item("ankle-dorsiflexion-plantarflexion-mobility", "Ankle dorsiflexion/plantarflexion mobility"),
                    Item("subtalar-joint-inversion-eversion-control", "Subtalar joint inversion/eversion control"),
                    Item("midfoot-mobility-stiffness", "Midfoot mobility/stiffness (e.g., cuboid/navicular glide)"),
                    Item("toe-flexor-extensor-strength", "Toe flexor/extensor strength"),
                    Item("ankle-proprioception-balance", "Ankle proprioception & balance"),
                    Item("talocrural-joint-arthrokinematics", "Talocrural joint arthrokinematics (e.g., posterior glide of talus)"),
                    Item("arch-support-dynamic-foot-posture", "Arch support & dynamic foot posture"),
                    Item("gait-pattern-push-off-mechanics", "Gait pattern / push-off mechanics"),
                    Item("achilles-tendon-loading", "Achilles tendon loading")),

                Category("knee", "Knee", TreatmentTaxonomyCategoryKind.BodyRegion, 10,
                    Item("patellar-tracking-alignment", "Patellar tracking & alignment"),
                    Item("tibiofemoral-joint-arthrokinematics", "Tibiofemoral joint arthrokinematics (e.g., screw-home mechanism)"),
                    Item("quad-control-activation", "Quad control & activation (VMO targeting, etc.)"),
                    Item("hamstring-quadriceps-strength-ratio", "Hamstring-quadriceps strength ratio"),
                    Item("joint-mobility-flexion-extension-rom", "Joint mobility (flexion/extension ROM)"),
                    Item("knee-valgus-varus-dynamic-control", "Knee valgus/varus dynamic control"),
                    Item("meniscal-loading-tolerance", "Meniscal loading tolerance"),
                    Item("terminal-knee-extension", "Terminal knee extension"),
                    Item("proximal-control-influence", "Proximal control influence (hip to knee)")),

                Category("hip", "Hip", TreatmentTaxonomyCategoryKind.BodyRegion, 11,
                    Item("hip-arthrokinematics", "Hip arthrokinematics (e.g., posterior glide for flexion)"),
                    Item("lateral-pelvic-control", "Lateral pelvic control (Trendelenburg)"),
                    Item("glute-med-max-activation-recruitment", "Glute med/max activation & recruitment"),
                    Item("hip-flexor-length-control", "Hip flexor length & control (iliopsoas, rectus femoris)"),
                    Item("hip-rotation-control", "Hip rotation control (IR/ER)"),
                    Item("femoral-head-positioning", "Femoral head positioning (centering)"),
                    Item("si-joint-mobility-stability-hip", "SI joint mobility/stability"),
                    Item("lumbopelvic-rhythm-coordination-hip", "Lumbopelvic rhythm coordination"),
                    Item("piriformis-syndrome-management", "Piriformis syndrome management")),

                Category("pelvis-si-joint", "Pelvis / SI Joint", TreatmentTaxonomyCategoryKind.BodyRegion, 12,
                    Item("si-joint-mobility-stability", "SI joint mobility/stability"),
                    Item("sacral-nutation-counternutation-control", "Sacral nutation/counternutation control"),
                    Item("lumbopelvic-rhythm-during-movement", "Lumbopelvic rhythm during movement"),
                    Item("core-lumbopelvic-coordination", "Core-lumbopelvic coordination"),
                    Item("pubic-symphysis-stability", "Pubic symphysis stability"),
                    Item("pelvic-floor-activation-dysfunction", "Pelvic floor activation/dysfunction")),

                Category("lumbar-spine", "Lumbar Spine", TreatmentTaxonomyCategoryKind.BodyRegion, 13,
                    Item("lumbar-segmental-mobility", "Lumbar segmental mobility"),
                    Item("core-activation-bracing", "Core activation & bracing (transversus abdominis, multifidus)"),
                    Item("flexion-vs-extension-bias-assessment", "Flexion vs extension bias assessment"),
                    Item("lumbopelvic-rhythm-lumbar", "Lumbopelvic rhythm (esp. with hip hinge or sit-to-stand)"),
                    Item("facet-joint-mobility", "Facet joint mobility"),
                    Item("traction-response", "Traction response (manual or mechanical)"),
                    Item("neural-mobility", "Neural mobility (sciatic, femoral nerve tension testing)"),
                    Item("directional-preference", "Directional preference (e.g., McKenzie)"),
                    Item("pain-centralization-strategies", "Pain centralization strategies")),

                Category("thoracic-spine", "Thoracic Spine", TreatmentTaxonomyCategoryKind.BodyRegion, 14,
                    Item("thoracic-extension-rotation-mobility", "Thoracic extension & rotation mobility"),
                    Item("thoracic-rib-cage-expansion", "Thoracic spine & rib cage expansion (breathing mechanics)"),
                    Item("scapulothoracic-rhythm", "Scapulothoracic rhythm"),
                    Item("postural-control-thoracic", "Postural control (kyphosis correction, etc.)"),
                    Item("segmental-mobility-manipulation", "Segmental mobility & manipulation/mobilization"),
                    Item("regional-interdependence-thoracic", "Regional interdependence with shoulder/cervical/lumbar")),

                Category("cervical-spine", "Cervical Spine", TreatmentTaxonomyCategoryKind.BodyRegion, 15,
                    Item("cervical-joint-mobility", "Cervical joint mobility (flexion/extension/rotation/side-bend)"),
                    Item("cervical-arthrokinematics", "Cervical arthrokinematics (e.g., facet glide, OA joint)"),
                    Item("cervical-traction", "Cervical traction (manual/mechanical)"),
                    Item("deep-neck-flexor-activation", "Deep neck flexor activation"),
                    Item("cervical-proprioception", "Cervical proprioception (joint position error testing)"),
                    Item("cervicogenic-headache-treatment", "Cervicogenic headache treatment"),
                    Item("thoracic-mobility-influence-on-cervical-loading", "Thoracic mobility influence on cervical loading")),

                Category("shoulder", "Shoulder", TreatmentTaxonomyCategoryKind.BodyRegion, 16,
                    Item("glenohumeral-arthrokinematics", "Glenohumeral arthrokinematics (anterior/posterior/inferior glide)"),
                    Item("scapulohumeral-rhythm", "Scapulohumeral rhythm"),
                    Item("rotator-cuff-strength-neuromuscular-control", "Rotator cuff strength & neuromuscular control"),
                    Item("scapular-stabilization", "Scapular stabilization (serratus anterior, lower trap)"),
                    Item("shoulder-elevation-pattern", "Shoulder elevation pattern (impingement vs dyskinesis)"),
                    Item("shoulder-ir-er-strength-ratio", "Shoulder IR/ER strength ratio"),
                    Item("posterior-capsule-mobility", "Posterior capsule mobility"),
                    Item("joint-mobilization-grades", "Joint mobilization (Grades I-IV)"),
                    Item("thoracic-spine-influence", "Thoracic spine influence")),

                Category("elbow", "Elbow", TreatmentTaxonomyCategoryKind.BodyRegion, 17,
                    Item("elbow-flexion-extension-mobility", "Elbow flexion/extension mobility"),
                    Item("valgus-stress-control", "Valgus stress control (throwing mechanics, UCL issues)"),
                    Item("elbow-joint-arthrokinematics", "Joint arthrokinematics (radiohumeral, ulnohumeral)"),
                    Item("grip-strength-forearm-endurance", "Grip strength & forearm muscle endurance"),
                    Item("neural-mobility-elbow", "Neural mobility (radial, ulnar, median)"),
                    Item("epicondylitis-protocols", "Epicondylitis protocols (eccentrics, bracing)")),

                Category("wrist-hand", "Wrist & Hand", TreatmentTaxonomyCategoryKind.BodyRegion, 18,
                    Item("wrist-joint-mobility", "Wrist joint mobility (flexion, extension, radial/ulnar deviation)"),
                    Item("carpal-glide-techniques", "Carpal glide techniques (e.g., lunate, scaphoid)"),
                    Item("median-ulnar-nerve-gliding", "Median/ulnar nerve gliding"),
                    Item("thumb-opposition-stability", "Thumb opposition & stability (CMC joint)"),
                    Item("grip-pinch-strength", "Grip strength & pinch strength"),
                    Item("fine-motor-control-dexterity", "Fine motor control & dexterity"),
                    Item("tendon-gliding-exercises", "Tendon gliding exercises (FDS/FDP, EPL)")),

                Category("neuromuscular-regional-concepts", "Neuromuscular & Regional Concepts", TreatmentTaxonomyCategoryKind.CrossCuttingConcept, 19,
                    Item("regional-interdependence", "Regional interdependence (e.g., hip -> knee, thoracic -> shoulder)"),
                    Item("proprioception-balance-strategies", "Proprioception & balance strategies (e.g., Y-Balance Test)"),
                    Item("motor-control-retraining", "Motor control retraining (e.g., lumbo-pelvic control)"),
                    Item("movement-pattern-analysis", "Movement pattern analysis (SFMA/FMS)"),
                    Item("core-to-extremity-sequencing", "Core-to-extremity sequencing"),
                    Item("breathing-mechanics-integration", "Breathing mechanics integration (diaphragm vs accessory muscles)"))
            ]
        };
    }

    private static TreatmentTaxonomyCategoryDto Category(
        string id,
        string title,
        TreatmentTaxonomyCategoryKind kind,
        int displayOrder,
        params TreatmentTaxonomyItemDto[] items)
    {
        return new TreatmentTaxonomyCategoryDto
        {
            Id = id,
            Title = title,
            Kind = kind,
            DisplayOrder = displayOrder,
            Items = items
        };
    }

    private static TreatmentTaxonomyItemDto Item(string id, string label)
    {
        return new TreatmentTaxonomyItemDto
        {
            Id = id,
            Label = label
        };
    }
}