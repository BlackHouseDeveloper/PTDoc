using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace PTDoc.Tests.Security;

[Trait("Category", "CoreCi")]
public sealed class CiCategoryConventionsTests
{
    private static readonly HashSet<string> AllowedOwnerCategories =
    [
        "CoreCi",
        "DatabaseProvider",
        "Observability",
        "SecretPolicy",
        "RBAC",
        "Tenancy",
        "OfflineSync",
        "Compliance",
        "EndToEnd"
    ];

    [Fact]
    public void TestFiles_MustDeclareExactlyOneOwnerCategory()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var testRoot = Path.Combine(repoRoot, "tests", "PTDoc.Tests");
        var categoryRegex = new Regex(
            "^\\s*\\[\\s*(?:[A-Za-z_][A-Za-z0-9_]*\\.)*Trait\\(\"Category\",\\s*\"([^\"]+)\"\\)\\s*\\]",
            RegexOptions.Compiled | RegexOptions.Multiline);
        var failures = new List<string>();

        foreach (var file in Directory.EnumerateFiles(testRoot, "*Tests.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var categories = categoryRegex.Matches(File.ReadAllText(file))
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();

            if (categories.Count == 0)
            {
                failures.Add($"{Path.GetRelativePath(repoRoot, file)}: missing owner category");
                continue;
            }

            var invalidCategories = categories.Where(category => !AllowedOwnerCategories.Contains(category)).ToList();
            if (invalidCategories.Count > 0)
            {
                failures.Add(
                    $"{Path.GetRelativePath(repoRoot, file)}: contains unsupported categories [{string.Join(", ", invalidCategories)}]");
                continue;
            }

            if (categories.Count != 1)
            {
                failures.Add(
                    $"{Path.GetRelativePath(repoRoot, file)}: expected exactly one owner category, found [{string.Join(", ", categories)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
