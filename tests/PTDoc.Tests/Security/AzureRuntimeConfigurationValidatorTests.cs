using Microsoft.Extensions.Configuration;
using PTDoc.Api.AI;
using PTDoc.Application.AI;

namespace PTDoc.Tests.Security;

[Trait("Category", "CoreCi")]
public class AzureRuntimeConfigurationValidatorTests
{
    [Fact]
    public void RequiresAzureOpenAiConfiguration_ReturnsFalse_WhenAiFeatureDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "false"
            })
            .Build();

        Assert.False(AzureRuntimeConfigurationValidator.RequiresAzureOpenAiConfiguration(configuration));
    }

    [Fact]
    public void RequiresAzureOpenAiConfiguration_ReturnsTrue_WhenAiFeatureEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true"
            })
            .Build();

        Assert.True(AzureRuntimeConfigurationValidator.RequiresAzureOpenAiConfiguration(configuration));
    }

    [Fact]
    public void GetStartupValidationMode_ReturnsDisabled_WhenAiFeatureDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "false"
            })
            .Build();

        Assert.Equal("Disabled", AzureRuntimeConfigurationValidator.GetStartupValidationMode(configuration, isDevelopment: false));
    }

    [Fact]
    public void GetStartupValidationMode_ReturnsEagerAtStartup_WhenAiFeatureEnabled_InNonDevelopment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true"
            })
            .Build();

        Assert.Equal("EagerAtStartup", AzureRuntimeConfigurationValidator.GetStartupValidationMode(configuration, isDevelopment: false));
    }

    [Fact]
    public void GetStartupValidationMode_ReturnsLazyOnFirstRequest_WhenAiFeatureEnabled_InDevelopment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true"
            })
            .Build();

        Assert.Equal("LazyOnFirstRequest", AzureRuntimeConfigurationValidator.GetStartupValidationMode(configuration, isDevelopment: true));
    }

    [Fact]
    public void GetMissingAzureOpenAiConfigurationKeys_ReturnsExpectedKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true",
                ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/"
            })
            .Build();

        var missingKeys = AzureRuntimeConfigurationValidator.GetMissingAzureOpenAiConfigurationKeys(configuration);

        Assert.Equal([AzureOpenAiOptions.ApiKeyKey, AzureOpenAiOptions.DeploymentKey], missingKeys);
        Assert.False(AzureRuntimeConfigurationValidator.HasCompleteAzureOpenAiConfiguration(configuration));
    }

    [Fact]
    public void GetMissingAzureOpenAiConfigurationKeys_ReturnsEmpty_WhenAllRequiredKeysPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true",
                ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/",
                ["AzureOpenAIKey"] = "secret",
                ["AzureOpenAIDeployment"] = "gpt-4o"
            })
            .Build();

        var missingKeys = AzureRuntimeConfigurationValidator.GetMissingAzureOpenAiConfigurationKeys(configuration);

        Assert.Empty(missingKeys);
        Assert.True(AzureRuntimeConfigurationValidator.HasCompleteAzureOpenAiConfiguration(configuration));
    }

    [Fact]
    public void ValidateAzureOpenAiConfiguration_Throws_WhenRequiredKeysMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true",
                ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AzureRuntimeConfigurationValidator.ValidateAzureOpenAiConfiguration(configuration));

        Assert.Contains("AzureOpenAIKey", exception.Message);
        Assert.Contains("AzureOpenAIDeployment", exception.Message);
    }

    [Fact]
    public void ValidateAzureOpenAiConfiguration_Succeeds_WhenAllRequiredKeysPresent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureFlags:EnableAiGeneration"] = "true",
                ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/",
                ["AzureOpenAIKey"] = "secret",
                ["AzureOpenAIDeployment"] = "gpt-4o"
            })
            .Build();

        AzureRuntimeConfigurationValidator.ValidateAzureOpenAiConfiguration(configuration);
    }
}
