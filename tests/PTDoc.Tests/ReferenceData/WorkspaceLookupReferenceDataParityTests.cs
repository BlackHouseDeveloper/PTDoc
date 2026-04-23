using System.Text.Json;
using System.Text.RegularExpressions;
using PTDoc.Application.ReferenceData;
using PTDoc.Tests.Security;
using Xunit;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "CoreCi")]
public sealed partial class WorkspaceLookupReferenceDataParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Icd10MarkdownAndRuntimeAsset_HaveUniqueMatchingCodeDescriptions()
    {
        var root = FindRepoRoot();
        var markdownEntries = ParseMarkdownCodeList(
            Path.Combine(root, "docs", "clinicrefdata", "ICD-10 codes.md"),
            IcdMarkdownCodeRegex());
        var asset = LoadAsset(root);
        var assetEntries = ToUniqueDictionary(asset.Icd10Codes, "JSON ICD-10 asset");

        Assert.Equal(
            markdownEntries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
            assetEntries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void CptMarkdownAndRuntimeAsset_HaveUniqueMatchingCodeDescriptionsAndModifiers()
    {
        var root = FindRepoRoot();
        var markdownEntries = ParseMarkdownCodeList(
            Path.Combine(root, "docs", "clinicrefdata", "Commonly used CPT codes and modifiers.md"),
            CptMarkdownCodeRegex());
        var markdownModifiers = ParseModifierRows(
            Path.Combine(root, "docs", "clinicrefdata", "Commonly used CPT codes and modifiers.md"));
        var asset = LoadAsset(root);
        var assetEntries = ToUniqueDictionary(asset.CptCodes, "JSON CPT asset");

        Assert.Equal(
            markdownEntries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
            assetEntries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Equal(
            markdownModifiers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            asset.DefaultCptModifierOptions.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static WorkspaceLookupReferenceDataAsset LoadAsset(string root)
    {
        var assetPath = Path.Combine(root, "src", "PTDoc.Application", "Data", "WorkspaceLookupReferenceData.json");
        return JsonSerializer.Deserialize<WorkspaceLookupReferenceDataAsset>(
            File.ReadAllText(assetPath),
            JsonOptions) ?? throw new InvalidOperationException("Workspace lookup asset could not be deserialized.");
    }

    private static Dictionary<string, string> ParseMarkdownCodeList(string path, Regex regex)
    {
        var entries = File.ReadLines(path)
            .Select(line => regex.Match(line))
            .Where(match => match.Success)
            .Select(match => (
                Code: match.Groups["code"].Value.Trim(),
                Description: NormalizeDescription(match.Groups["description"].Value)))
            .ToList();

        var duplicates = entries
            .GroupBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Empty(duplicates);

        return entries.ToDictionary(
            entry => entry.Code,
            entry => entry.Description,
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ToUniqueDictionary(
        IEnumerable<WorkspaceLookupCodeAsset> codes,
        string sourceName)
    {
        var entries = codes
            .Select(code => (
                Code: code.Code.Trim(),
                Description: NormalizeDescription(code.Description)))
            .ToList();
        var duplicates = entries
            .GroupBy(entry => entry.Code, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(duplicates.Length == 0, $"{sourceName} contains duplicate code(s): {string.Join(", ", duplicates)}");

        return entries.ToDictionary(
            entry => entry.Code,
            entry => entry.Description,
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseModifierRows(string path)
    {
        var modifiers = File.ReadLines(path)
            .Select(line => ModifierRowRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["modifier"].Value.Trim())
            .Where(value => !string.Equals(value, "Modifier", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var duplicates = modifiers
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
        return modifiers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string NormalizeDescription(string value) =>
        Regex.Replace(
                value.Replace("\\*", string.Empty).Replace('–', '-').Trim(),
                "\\s+",
                " ")
            .Trim();

    private static string FindRepoRoot()
        => ConfigurationValidationTests.FindRepoRoot();

    [GeneratedRegex("^\\s*\\*\\s*\\*\\*(?<code>[A-Z][A-Z0-9.]+)\\*\\*\\s*[–-]\\s*(?<description>.*?)\\s*$")]
    private static partial Regex IcdMarkdownCodeRegex();

    [GeneratedRegex("^\\s*\\|\\s*\\*\\*(?<code>[0-9]{5})\\*\\*\\s*\\|\\s*(?<description>.*?)\\s*\\|\\s*$")]
    private static partial Regex CptMarkdownCodeRegex();

    [GeneratedRegex("^\\s*\\|\\s*(?<modifier>[A-Z0-9]{2})\\s*\\|")]
    private static partial Regex ModifierRowRegex();
}
