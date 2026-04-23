using System.Net;
using System.Text.Json;
using PTDoc.Application.Services;

namespace PTDoc.Tests.Integration;

[Collection("EnvironmentVariables")]
[Trait("Category", "CoreCi")]
public sealed class RuntimeDiagnosticsIntegrationTests
{
    [Fact]
    public async Task RuntimeDiagnostics_AsAdmin_ReturnsReleaseMetadata_AndConfiguredAiRuntime()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_RELEASE_ID"] = "release-42",
            ["PTDOC_SOURCE_SHA"] = "abc123def456",
            ["PTDOC_IMAGE_TAG"] = "ptdoc-api:release-42",
            ["PTDOC_DEVELOPER_MODE"] = "true",
            ["FeatureFlags__EnableAiGeneration"] = "true",
            ["AzureOpenAIEndpoint"] = "https://ptdoc-ai.cognitiveservices.azure.com/openai/deployments/ptdoc-gpt-4o-mini/chat/completions?api-version=2025-01-01-preview",
            ["AzureOpenAIKey"] = "integration-test-azure-key",
            ["AzureOpenAIDeployment"] = "ptdoc-gpt-4o-mini",
            ["AzureOpenAIApiVersion"] = "2025-01-01-preview"
        });

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);

            using var response = await client.GetAsync("/diagnostics/runtime");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rawPayload = await response.Content.ReadAsStringAsync();
            using var payload = JsonDocument.Parse(rawPayload);
            var root = payload.RootElement;
            var release = root.GetProperty("release");
            var aiRuntime = root.GetProperty("aiRuntime");

            Assert.Equal("Testing", root.GetProperty("environmentName").GetString());
            Assert.False(root.GetProperty("isDevelopment").GetBoolean());

            Assert.Equal("release-42", release.GetProperty("releaseId").GetString());
            Assert.Equal("abc123def456", release.GetProperty("sourceSha").GetString());
            Assert.Equal("ptdoc-api:release-42", release.GetProperty("imageTag").GetString());
            Assert.False(string.IsNullOrWhiteSpace(release.GetProperty("assemblyVersion").GetString()));
            Assert.True(release.TryGetProperty("informationalVersion", out _));

            Assert.True(aiRuntime.GetProperty("featureEnabled").GetBoolean());
            Assert.True(aiRuntime.GetProperty("developerDiagnosticsEnabled").GetBoolean());
            Assert.Equal("EagerAtStartup", aiRuntime.GetProperty("startupValidationMode").GetString());
            Assert.Equal(
                "https://ptdoc-ai.cognitiveservices.azure.com/openai/deployments/ptdoc-gpt-4o-mini/chat/completions",
                aiRuntime.GetProperty("effectiveAzureOpenAiEndpoint").GetString());
            Assert.Equal("ptdoc-gpt-4o-mini", aiRuntime.GetProperty("effectiveAzureOpenAiDeployment").GetString());
            Assert.Equal("2025-01-01-preview", aiRuntime.GetProperty("effectiveAzureOpenAiApiVersion").GetString());
            Assert.Equal("Complete", aiRuntime.GetProperty("configurationState").GetString());
            Assert.True(aiRuntime.GetProperty("azureOpenAiConfigurationComplete").GetBoolean());
            Assert.Equal(0, aiRuntime.GetProperty("missingAzureOpenAiSettings").GetArrayLength());
            Assert.True(aiRuntime.GetProperty("requiresAuthenticatedSavedNoteAiProbe").GetBoolean());
            Assert.Equal("AuthenticatedSavedNoteAiRequestRequired", aiRuntime.GetProperty("runtimeHealthGate").GetString());
            Assert.DoesNotContain("integration-test-azure-key", rawPayload, StringComparison.Ordinal);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task RuntimeDiagnostics_WhenAiFeatureDisabled_ReportsDisabledGate()
    {
        using var env = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["PTDOC_DEVELOPER_MODE"] = "false",
            ["FeatureFlags__EnableAiGeneration"] = "false",
            ["AzureOpenAIEndpoint"] = "https://test.openai.azure.com/",
            ["AzureOpenAIApiVersion"] = string.Empty
        });

        var factory = new PtDocApiFactory();
        try
        {
            await factory.InitializeAsync();
            using var client = factory.CreateClientWithRole(Roles.Admin);

            using var response = await client.GetAsync("/diagnostics/runtime");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var aiRuntime = payload.RootElement.GetProperty("aiRuntime");

            Assert.False(aiRuntime.GetProperty("featureEnabled").GetBoolean());
            Assert.False(aiRuntime.GetProperty("developerDiagnosticsEnabled").GetBoolean());
            Assert.Equal("Disabled", aiRuntime.GetProperty("startupValidationMode").GetString());
            Assert.Equal("https://test.openai.azure.com", aiRuntime.GetProperty("effectiveAzureOpenAiEndpoint").GetString());
            Assert.Equal("2024-06-01", aiRuntime.GetProperty("effectiveAzureOpenAiApiVersion").GetString());
            Assert.Equal("NotRequired", aiRuntime.GetProperty("configurationState").GetString());
            Assert.False(aiRuntime.GetProperty("requiresAuthenticatedSavedNoteAiProbe").GetBoolean());
            Assert.Equal("DisabledByFeatureFlag", aiRuntime.GetProperty("runtimeHealthGate").GetString());
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> previousValues = new(StringComparer.Ordinal);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (name, value) in values)
            {
                previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, previousValue) in previousValues)
            {
                Environment.SetEnvironmentVariable(name, previousValue);
            }
        }
    }
}
