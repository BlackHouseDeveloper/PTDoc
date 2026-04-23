using PTDoc.Application.AI;

namespace PTDoc.Api.AI;

public static class AzureRuntimeConfigurationValidator
{
    public static bool RequiresAzureOpenAiConfiguration(IConfiguration configuration)
    {
        return configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration");
    }

    public static string GetStartupValidationMode(IConfiguration configuration, bool isDevelopment)
    {
        if (!RequiresAzureOpenAiConfiguration(configuration))
        {
            return "Disabled";
        }

        return isDevelopment ? "LazyOnFirstRequest" : "EagerAtStartup";
    }

    public static IReadOnlyList<string> GetMissingAzureOpenAiConfigurationKeys(IConfiguration configuration)
    {
        List<string> missing = [];

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

        return missing;
    }

    public static bool HasCompleteAzureOpenAiConfiguration(IConfiguration configuration)
    {
        return GetMissingAzureOpenAiConfigurationKeys(configuration).Count == 0;
    }

    public static void ValidateAzureOpenAiConfiguration(IConfiguration configuration)
    {
        var missing = GetMissingAzureOpenAiConfigurationKeys(configuration);

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Azure OpenAI runtime configuration is incomplete. Missing settings: " + string.Join(", ", missing));
        }
    }
}
