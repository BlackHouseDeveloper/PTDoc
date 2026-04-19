using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Notes.Workspace;

internal static class WorkspaceReferenceCatalogAssetMapper
{
    public static IReadOnlyDictionary<BodyPart, BodyRegionCatalog> Map(WorkspaceReferenceCatalogAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.Shared is null)
        {
            throw new InvalidOperationException("Workspace reference catalog asset is missing shared catalog sections.");
        }

        ValidateSharedSections(asset.Shared);

        var catalogs = new Dictionary<BodyPart, BodyRegionCatalog>();
        IEnumerable<WorkspaceReferenceCatalogTemplateAsset> templates = asset.Templates is null
            ? Array.Empty<WorkspaceReferenceCatalogTemplateAsset>()
            : asset.Templates;

        foreach (var template in templates)
        {
            ArgumentNullException.ThrowIfNull(template);

            var appliedBodyParts = ParseBodyParts(template);
            var templateCatalog = CreateTemplateCatalog(template, asset.Shared, appliedBodyParts[0]);

            foreach (var bodyPart in appliedBodyParts)
            {
                if (!catalogs.TryAdd(bodyPart, CloneForBodyPart(templateCatalog, bodyPart)))
                {
                    throw new InvalidOperationException(
                        $"Workspace reference catalog template '{template.TemplateId}' assigns body part '{bodyPart}' more than once.");
                }
            }
        }

        var missingBodyParts = Enum.GetValues<BodyPart>()
            .Except(catalogs.Keys)
            .ToArray();
        if (missingBodyParts.Length > 0)
        {
            throw new InvalidOperationException(
                $"Workspace reference catalog asset must assign every body part exactly once. Missing: {string.Join(", ", missingBodyParts)}.");
        }

        return catalogs;
    }

    private static List<BodyPart> ParseBodyParts(WorkspaceReferenceCatalogTemplateAsset template)
    {
        if (template.AppliesToBodyParts is not { Count: > 0 })
        {
            throw new InvalidOperationException(
                $"Workspace reference catalog template '{template.TemplateId}' must declare at least one body part.");
        }

        var parsed = new List<BodyPart>(template.AppliesToBodyParts.Count);
        foreach (var rawBodyPart in template.AppliesToBodyParts)
        {
            var trimmedBodyPart = rawBodyPart?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedBodyPart) ||
                !Enum.GetNames<BodyPart>().Any(name => string.Equals(name, trimmedBodyPart, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Workspace reference catalog template '{template.TemplateId}' contains invalid body part '{rawBodyPart}'.");
            }

            parsed.Add(Enum.Parse<BodyPart>(trimmedBodyPart, ignoreCase: true));
        }

        return parsed;
    }

    private static void ValidateSharedSections(WorkspaceReferenceCatalogSharedAsset shared)
    {
        ValidateSharedCatalogSection(
            sectionName: "treatment interventions",
            isAvailable: shared.TreatmentInterventions?.IsAvailable,
            notes: shared.TreatmentInterventions?.Notes,
            provenance: shared.TreatmentInterventions?.Provenance,
            hasValues: (shared.TreatmentInterventions?.Options?.Count ?? 0) > 0);

        ValidateSharedCatalogSection(
            sectionName: "joint mobility/MMT",
            isAvailable: shared.JointMobilityAndMmt?.IsAvailable,
            notes: shared.JointMobilityAndMmt?.Notes,
            provenance: shared.JointMobilityAndMmt?.Provenance,
            hasValues: (shared.JointMobilityAndMmt?.MmtGrades?.Count ?? 0) > 0 &&
                       (shared.JointMobilityAndMmt?.JointMobilityGrades?.Count ?? 0) > 0);
    }

    private static void ValidateSharedCatalogSection(
        string sectionName,
        bool? isAvailable,
        string? notes,
        ReferenceDataProvenance? provenance,
        bool hasValues)
    {
        if (isAvailable != true ||
            string.IsNullOrWhiteSpace(notes) ||
            string.IsNullOrWhiteSpace(provenance?.DocumentPath) ||
            string.IsNullOrWhiteSpace(provenance?.Version) ||
            !hasValues)
        {
            throw new InvalidOperationException(
                $"Workspace reference catalog asset is missing required shared {sectionName} metadata.");
        }
    }

    private static BodyRegionCatalog CreateTemplateCatalog(
        WorkspaceReferenceCatalogTemplateAsset template,
        WorkspaceReferenceCatalogSharedAsset shared,
        BodyPart templateBodyPart) => new()
        {
            BodyPart = templateBodyPart,
            FunctionalLimitations = MapAvailability(template.FunctionalLimitations),
            GoalTemplates = MapAvailability(template.GoalTemplates),
            SpecialTests = MapAvailability(template.SpecialTests),
            OutcomeMeasures = MapAvailability(template.OutcomeMeasures),
            NormalRangeOfMotion = MapAvailability(template.NormalRangeOfMotion),
            TenderMuscles = MapAvailability(template.TenderMuscles),
            Exercises = MapAvailability(template.Exercises),
            TreatmentFocuses = MapAvailability(template.TreatmentFocuses),
            TreatmentInterventions = MapAvailability(shared.TreatmentInterventions),
            JointMobilityAndMmt = MapAvailability(shared.JointMobilityAndMmt),
            FunctionalLimitationCategories = MapCategories(template.FunctionalLimitations?.Categories),
            GoalTemplateCategories = MapCategories(template.GoalTemplates?.Categories),
            SpecialTestsOptions = MapOptions(template.SpecialTests?.Options),
            OutcomeMeasureOptions = MapOptions(template.OutcomeMeasures?.Options),
            NormalRangeOfMotionOptions = MapOptions(template.NormalRangeOfMotion?.Options),
            TenderMuscleOptions = MapOptions(template.TenderMuscles?.Options),
            ExerciseOptions = MapOptions(template.Exercises?.Options),
            TreatmentFocusOptions = MapOptions(template.TreatmentFocuses?.Options),
            TreatmentInterventionOptions = MapOptions(shared.TreatmentInterventions?.Options),
            MmtGradeOptions = MapOptions(shared.JointMobilityAndMmt?.MmtGrades),
            JointMobilityGradeOptions = MapOptions(shared.JointMobilityAndMmt?.JointMobilityGrades)
        };

    private static CatalogAvailability MapAvailability(WorkspaceCatalogSectionAsset? section)
    {
        var notes = section?.Notes ?? string.Empty;
        var provenance = WorkspaceCatalogCloneHelpers.CloneProvenance(section?.Provenance);

        return section?.IsAvailable == true
            ? CatalogAvailability.Available(notes, provenance)
            : CatalogAvailability.Missing(notes, provenance);
    }

    private static CatalogAvailability MapAvailability(WorkspaceJointMobilityAndMmtAsset? section)
    {
        var notes = section?.Notes ?? string.Empty;
        var provenance = WorkspaceCatalogCloneHelpers.CloneProvenance(section?.Provenance);

        return section?.IsAvailable == true
            ? CatalogAvailability.Available(notes, provenance)
            : CatalogAvailability.Missing(notes, provenance);
    }

    private static List<CatalogCategory> MapCategories(IEnumerable<WorkspaceCatalogCategoryAsset>? categories) =>
        (categories ?? Array.Empty<WorkspaceCatalogCategoryAsset>())
        .Select(category => new CatalogCategory
        {
            Name = category.Name,
            Items = category.Items?.ToList() ?? new List<string>()
        })
        .ToList();

    private static List<string> MapOptions(IEnumerable<string>? options) =>
        (options ?? Array.Empty<string>()).ToList();

    private static BodyRegionCatalog CloneForBodyPart(BodyRegionCatalog source, BodyPart bodyPart) => new()
    {
        BodyPart = bodyPart,
        FunctionalLimitations = WorkspaceCatalogCloneHelpers.CloneAvailability(source.FunctionalLimitations),
        GoalTemplates = WorkspaceCatalogCloneHelpers.CloneAvailability(source.GoalTemplates),
        AssistiveDevices = WorkspaceCatalogCloneHelpers.CloneAvailability(source.AssistiveDevices),
        Comorbidities = WorkspaceCatalogCloneHelpers.CloneAvailability(source.Comorbidities),
        SpecialTests = WorkspaceCatalogCloneHelpers.CloneAvailability(source.SpecialTests),
        OutcomeMeasures = WorkspaceCatalogCloneHelpers.CloneAvailability(source.OutcomeMeasures),
        NormalRangeOfMotion = WorkspaceCatalogCloneHelpers.CloneAvailability(source.NormalRangeOfMotion),
        TenderMuscles = WorkspaceCatalogCloneHelpers.CloneAvailability(source.TenderMuscles),
        Exercises = WorkspaceCatalogCloneHelpers.CloneAvailability(source.Exercises),
        TreatmentFocuses = WorkspaceCatalogCloneHelpers.CloneAvailability(source.TreatmentFocuses),
        TreatmentInterventions = WorkspaceCatalogCloneHelpers.CloneAvailability(source.TreatmentInterventions),
        JointMobilityAndMmt = WorkspaceCatalogCloneHelpers.CloneAvailability(source.JointMobilityAndMmt),
        FunctionalLimitationCategories = source.FunctionalLimitationCategories
            .Select(category => new CatalogCategory
            {
                Name = category.Name,
                Items = category.Items.ToList()
            })
            .ToList(),
        GoalTemplateCategories = source.GoalTemplateCategories
            .Select(category => new CatalogCategory
            {
                Name = category.Name,
                Items = category.Items.ToList()
            })
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
}
