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
            if (!Enum.TryParse<BodyPart>(rawBodyPart, ignoreCase: true, out var bodyPart))
            {
                throw new InvalidOperationException(
                    $"Workspace reference catalog template '{template.TemplateId}' contains invalid body part '{rawBodyPart}'.");
            }

            parsed.Add(bodyPart);
        }

        return parsed;
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
        var provenance = CloneProvenance(section?.Provenance);

        return section?.IsAvailable == true
            ? CatalogAvailability.Available(notes, provenance)
            : CatalogAvailability.Missing(notes, provenance);
    }

    private static CatalogAvailability MapAvailability(WorkspaceJointMobilityAndMmtAsset? section)
    {
        var notes = section?.Notes ?? string.Empty;
        var provenance = CloneProvenance(section?.Provenance);

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

    private static ReferenceDataProvenance? CloneProvenance(ReferenceDataProvenance? provenance)
        => provenance is null
            ? null
            : new ReferenceDataProvenance
            {
                DocumentPath = provenance.DocumentPath,
                Version = provenance.Version,
                Notes = provenance.Notes
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
