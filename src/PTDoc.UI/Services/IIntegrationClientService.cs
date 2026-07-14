using PTDoc.Application.Integrations;

namespace PTDoc.UI.Services;

public interface IIntegrationClientService
{
    Task<IReadOnlyList<IntegrationConnectionResponse>> GetConnectionsAsync(CancellationToken cancellationToken = default);
    Task<IntegrationConnectionResponse> SaveConnectionAsync(Guid clinicId, string provider, UpsertIntegrationConnectionRequest request, CancellationToken cancellationToken = default);
    Task<ProviderConnectionHealth> VerifyConnectionAsync(Guid clinicId, string provider, CancellationToken cancellationToken = default);
    Task<WebhookTokenRotationResponse> RotateWebhookTokenAsync(Guid clinicId, string provider, CancellationToken cancellationToken = default);
    Task<FaxTransmissionResponse> QueueFaxAsync(CreateFaxTransmissionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FaxTransmissionResponse>> GetFaxTransmissionsAsync(Guid? patientId = null, CancellationToken cancellationToken = default);
    Task<FaxTransmissionResponse> ResendFaxAsync(Guid transmissionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InboundFaxResponse>> GetInboundFaxesAsync(CancellationToken cancellationToken = default);
    Task<IntegrationContentDownload> DownloadInboundFaxAsync(Guid inboundFaxId, CancellationToken cancellationToken = default);
    Task<InboundFaxResponse> AssignInboundFaxAsync(Guid inboundFaxId, AssignInboundFaxRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchExercisesAsync(string query, string locale = "en-US", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HepProgramResponse>> GetHepProgramsAsync(Guid patientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HepProgramResponse>> GetCurrentPatientHepProgramsAsync(CancellationToken cancellationToken = default);
    Task<HepProgramResponse> CreateHepProgramAsync(Guid patientId, CreateHepProgramRequest request, CancellationToken cancellationToken = default);
    Task<HepProgramResponse> UpdateHepProgramAsync(Guid programId, CreateHepProgramRequest request, CancellationToken cancellationToken = default);
    Task<HepProgramResponse> PublishHepProgramAsync(Guid programId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HepTrackingObservationResponse>> GetHepTrackingAsync(Guid programId, CancellationToken cancellationToken = default);
    Task<ProviderLaunchResponse> CreateLaunchAsync(Guid programId, bool flowSheet, CancellationToken cancellationToken = default);
    Task<ProviderLaunchResponse> CreatePatientLaunchAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IntegrationDeadLetterResponse>> GetDeadLettersAsync(CancellationToken cancellationToken = default);
    Task<IntegrationDeadLetterResponse> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default);
}

public sealed class IntegrationContentDownload : IAsyncDisposable
{
    private readonly HttpResponseMessage response;

    public IntegrationContentDownload(HttpResponseMessage response, Stream content, string fileName, string contentType)
    {
        this.response = response;
        Content = content;
        FileName = fileName;
        ContentType = contentType;
    }

    public Stream Content { get; }
    public string FileName { get; }
    public string ContentType { get; }

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
        response.Dispose();
    }
}
