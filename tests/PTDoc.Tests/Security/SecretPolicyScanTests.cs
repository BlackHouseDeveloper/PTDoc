using Xunit;

namespace PTDoc.Tests.Security;

[Trait("Category", "SecretPolicy")]
public sealed class SecretPolicyScanTests
{
    [Fact]
    public void SecretPolicyScanner_Passes_ForRepositoryConfigurationFiles()
    {
        var repoRoot = ConfigurationValidationTests.FindRepoRoot();
        var failures = SecretPolicyScanner.ScanRepository(repoRoot);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
