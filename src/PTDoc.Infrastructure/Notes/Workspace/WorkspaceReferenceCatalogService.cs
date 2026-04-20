using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;

namespace PTDoc.Infrastructure.Notes.Workspace;

public sealed class WorkspaceReferenceCatalogService(IOutcomeMeasureRegistry outcomeMeasureRegistry)
    : IWorkspaceReferenceCatalogService
{
    private const string WorkspaceCatalogResourceName = "PTDoc.Application.Data.WorkspaceReferenceCatalog.json";
    private const string WorkspaceLookupResourceName = "PTDoc.Application.Data.WorkspaceLookupReferenceData.json";

    private static readonly Lazy<WorkspaceReferenceCatalogAsset> WorkspaceCatalogAsset =
        new(() => EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<WorkspaceReferenceCatalogAsset>(WorkspaceCatalogResourceName));

    private static readonly Lazy<IReadOnlyDictionary<BodyPart, BodyRegionCatalog>> Catalogs =
        new(() => WorkspaceReferenceCatalogAssetMapper.Map(WorkspaceCatalogAsset.Value));

    private static readonly Lazy<WorkspaceLookupReferenceDataAsset> LookupCatalog =
        new(() => EmbeddedJsonResourceLoader.LoadFromApplicationAssembly<WorkspaceLookupReferenceDataAsset>(WorkspaceLookupResourceName));

    private static readonly Lazy<IReadOnlyList<SearchableCodeLookupEntry>> SourceIcd10Codes =
        new(() => BuildLookupEntries(LookupCatalog.Value.Icd10Codes, LookupCatalog.Value.Icd10Provenance));

    private static readonly Lazy<IReadOnlyList<SearchableCodeLookupEntry>> SourceCptCodes =
        new(() => BuildLookupEntries(
            LookupCatalog.Value.CptCodes,
            LookupCatalog.Value.CptProvenance,
            LookupCatalog.Value.DefaultCptModifierOptions,
            LookupCatalog.Value.DefaultPtSuggestedModifiers));

    public BodyRegionCatalog GetBodyRegionCatalog(BodyPart bodyPart)
    {
        if (!Enum.IsDefined(typeof(BodyPart), bodyPart))
        {
            throw new ArgumentOutOfRangeException(nameof(bodyPart), bodyPart, $"Unknown body part '{bodyPart}'.");
        }

        if (Catalogs.Value.TryGetValue(bodyPart, out var catalog))
        {
            return CloneCatalog(catalog, bodyPart);
        }

        throw new InvalidOperationException(
            $"Workspace reference catalog asset is missing body part '{bodyPart}'.");
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
                Provenance = WorkspaceCatalogCloneHelpers.CloneProvenance(searchable.Entry.Provenance),
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
                    Provenance = WorkspaceCatalogCloneHelpers.CloneProvenance(provenance),
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

    private BodyRegionCatalog CloneCatalog(BodyRegionCatalog catalog, BodyPart requestedBodyPart)
    {
        var cloned = CloneForBodyPart(catalog, requestedBodyPart);
        var canonicalOutcomeMeasures = outcomeMeasureRegistry
            .GetRecommendedMeasureAbbreviationsForBodyPart(requestedBodyPart)
            .Select(abbreviation => OutcomeMeasureCatalogResolver.TryFormatDisplayOption(abbreviation, out var displayOption)
                ? displayOption
                : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (canonicalOutcomeMeasures.Count > 0)
        {
            cloned.OutcomeMeasures = cloned.OutcomeMeasures.IsAvailable
                ? cloned.OutcomeMeasures
                : CatalogAvailability.Available("Outcome registry fallback");
            cloned.OutcomeMeasureOptions = canonicalOutcomeMeasures;
        }

        return cloned;
    }

    private sealed class SearchableCodeLookupEntry
    {
        public CodeLookupEntry Entry { get; init; } = new();
        public List<string> SearchTerms { get; init; } = new();
    }

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
