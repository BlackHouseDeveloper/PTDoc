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
        return GetMissingAzureOpenAiConfigurationKeys(configuration).Count == 0
            && GetInvalidAzureOpenAiConfigurationErrors(configuration).Count == 0;
    }

    public static IReadOnlyList<string> GetInvalidAzureOpenAiConfigurationErrors(IConfiguration configuration)
    {
        List<string> errors = [];
        var endpoint = configuration[AzureOpenAiOptions.EndpointKey];

        if (!string.IsNullOrWhiteSpace(endpoint) && !IsValidBaseEndpoint(endpoint))
        {
            errors.Add(
                $"{AzureOpenAiOptions.EndpointKey} must be an absolute HTTPS base resource URL with no path, query string, fragment, or embedded credentials.");
        }

        return errors;
    }

    public static void ValidateAzureOpenAiConfiguration(IConfiguration configuration)
    {
        var missing = GetMissingAzureOpenAiConfigurationKeys(configuration);
        var invalid = GetInvalidAzureOpenAiConfigurationErrors(configuration);

        if (missing.Count == 0 && invalid.Count == 0)
        {
            return;
        }

        List<string> validationFailures = [];
        if (missing.Count > 0)
        {
            validationFailures.Add("Missing settings: " + string.Join(", ", missing));
        }

        validationFailures.AddRange(invalid);

        throw new InvalidOperationException(
            "Azure OpenAI runtime configuration is invalid. " + string.Join(" ", validationFailures));
    }

    private static bool IsValidBaseEndpoint(string value)
    {
        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && (string.IsNullOrEmpty(uri.AbsolutePath) || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
