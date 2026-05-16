using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Tests.Security;
using Xunit;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "CoreCi")]
public sealed class ClinicReferenceDataGovernanceTests
{
    private const string ClinicReferencePrefix = "docs/clinicrefdata/";
    private const string ClinicReferenceArchivePrefix = "docs/clinicrefdata/archive/";

    [Fact]
    public void Readme_ListsEveryClinicReferenceMarkdownFileWithAStatus()
    {
        var root = ConfigurationValidationTests.FindRepoRoot();
        var readmeEntries = LoadReadmeEntries(root);
        var docFiles = Directory.GetFiles(Path.Combine(root, "docs", "clinicrefdata"), "*.md", SearchOption.AllDirectories)
            .Select(path => ToRepoRelativePath(root, path))
            .Where(path => !string.Equals(path, "docs/clinicrefdata/README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(docFiles, readmeEntries.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void RuntimeReferenceData_OnlyUsesActiveClinicReferenceDocs()
    {
        var root = ConfigurationValidationTests.FindRepoRoot();
        var readmeEntries = LoadReadmeEntries(root);
        var activeClinicDocs = readmeEntries
            .Where(entry => !string.Equals(entry.Value, "archived", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var runtimePaths = GetRuntimeDocumentPaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.DoesNotContain(
            runtimePaths,
            path => path.StartsWith(ClinicReferenceArchivePrefix, StringComparison.OrdinalIgnoreCase));

        foreach (var path in runtimePaths.Where(path => path.StartsWith(ClinicReferencePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Contains(path, activeClinicDocs);
        }
    }

    private static IReadOnlyDictionary<string, string> LoadReadmeEntries(string root)
    {
        var readmePath = Path.Combine(root, "docs", "clinicrefdata", "README.md");
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(readmePath))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("| `docs/clinicrefdata/", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = trimmed.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 6)
            {
                continue;
            }

            var file = cells[1].Trim('`');
            var status = cells[2].Trim('`');
            entries[file] = status;
        }

        Assert.NotEmpty(entries);
        return entries;
    }

    private static IEnumerable<string?> GetRuntimeDocumentPaths()
    {
        var intakeCatalog = new IntakeReferenceDataCatalogService().GetCatalog();
        foreach (var source in intakeCatalog.Sources)
        {
            yield return source.DocumentPath;
        }

        var registry = new OutcomeMeasureRegistry();
        foreach (var definition in registry.GetAllMeasures())
        {
            yield return definition.Provenance?.DocumentPath;
        }

        var workspaceCatalogs = new WorkspaceReferenceCatalogService(registry);
        foreach (var bodyPart in Enum.GetValues<BodyPart>())
        {
            var catalog = workspaceCatalogs.GetBodyRegionCatalog(bodyPart);
            foreach (var availability in GetAvailabilities(catalog))
            {
                yield return availability.Provenance?.DocumentPath;
            }
        }

        yield return workspaceCatalogs.SearchIcd10("M62.81", take: 1).Single().Provenance?.DocumentPath;
        yield return workspaceCatalogs.SearchCpt("97110", take: 1).Single().Provenance?.DocumentPath;
    }

    private static IEnumerable<CatalogAvailability> GetAvailabilities(BodyRegionCatalog catalog)
    {
        yield return catalog.FunctionalLimitations;
        yield return catalog.GoalTemplates;
        yield return catalog.AssistiveDevices;
        yield return catalog.Comorbidities;
        yield return catalog.SpecialTests;
        yield return catalog.OutcomeMeasures;
        yield return catalog.NormalRangeOfMotion;
        yield return catalog.TenderMuscles;
        yield return catalog.Exercises;
        yield return catalog.TreatmentFocuses;
        yield return catalog.TreatmentInterventions;
        yield return catalog.JointMobilityAndMmt;
    }

    private static string ToRepoRelativePath(string root, string fullPath)
        => Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}
