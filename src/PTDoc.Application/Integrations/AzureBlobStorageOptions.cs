namespace PTDoc.Application.Integrations;

/// <summary>
/// Azure Blob Storage settings used by the API.
/// Container names are fixed defaults expected to already exist in Azure Storage.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    public const string ConnectionStringKey = "AzureStorageConnectionString";

    public string ConnectionString { get; set; } = string.Empty;

    public string PatientDocumentsContainer { get; set; } = "patient-documents";

    public string SoapExportsContainer { get; set; } = "soap-exports";

    public string AttachmentsContainer { get; set; } = "attachments";

    public string AiTempContainer { get; set; } = "ai-temp";
}
