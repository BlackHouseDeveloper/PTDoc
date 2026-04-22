namespace PTDoc.Application.AI;

/// <summary>
/// Azure OpenAI settings loaded from App Service configuration.
/// </summary>
public sealed class AzureOpenAiOptions
{
    public const string EndpointKey = "AzureOpenAIEndpoint";
    public const string ApiKeyKey = "AzureOpenAIKey";
    public const string DeploymentKey = "AzureOpenAIDeployment";
    public const string ApiVersionKey = "AzureOpenAIApiVersion";
    public const string DefaultApiVersion = "2024-06-01";

    public string Endpoint { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Deployment { get; set; } = string.Empty;

    public string ApiVersion { get; set; } = string.Empty;
}
