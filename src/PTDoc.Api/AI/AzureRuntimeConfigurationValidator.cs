using PTDoc.Application.AI;

namespace PTDoc.Api.AI;

public static class AzureRuntimeConfigurationValidator
{
    public static bool RequiresAzureOpenAiConfiguration(IConfiguration configuration)
    {
        return configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration");
    }

    public static void ValidateAzureOpenAiConfiguration(IConfiguration configuration)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration[AzureOpenAiOptions.EndpointKey]))
        {
            missing.Add(AzureOpenAiOptions.EndpointKey);
        }

        if (string.IsNullOrWhiteSpace(configuration[AzureOpenAiOptions.ApiKeyKey]))
        {
            missing.Add(AzureOpenAiOptions.ApiKeyKey);
        }

        if (string.IsNullOrWhiteSpace(configuration[AzureOpenAiOptions.DeploymentKey]))
        {
            missing.Add(AzureOpenAiOptions.DeploymentKey);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Azure OpenAI runtime configuration is incomplete. Missing settings: " + string.Join(", ", missing));
        }
    }
}
