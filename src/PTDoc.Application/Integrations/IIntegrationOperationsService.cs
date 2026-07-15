namespace PTDoc.Application.Integrations;

public interface IIntegrationOperationsService
{
    Task QueuePatientSynchronizationAsync(
        Guid patientId,
        Guid? requestedByUserId,
        DateTime patientVersionUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationConnectionResponse>> GetConnectionsAsync(CancellationToken cancellationToken = default);

    Task<IntegrationConnectionResponse> UpsertConnectionAsync(
        Guid clinicId,
        string provider,
        UpsertIntegrationConnectionRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderConnectionHealth> VerifyConnectionAsync(
        Guid clinicId,
        string provider,
        CancellationToken cancellationToken = default);

    Task<WebhookTokenRotationResponse> RotateWebhookTokenAsync(
        Guid clinicId,
        string provider,
        CancellationToken cancellationToken = default);

    Task<FaxTransmissionResponse> QueueFaxAsync(
        CreateFaxTransmissionRequest request,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaxTransmissionResponse>> GetFaxTransmissionsAsync(
        Guid? patientId,
        CancellationToken cancellationToken = default);

    Task<FaxTransmissionResponse?> GetFaxTransmissionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<FaxTransmissionResponse> ResendFaxAsync(
        Guid id,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InboundFaxResponse>> GetInboundFaxesAsync(CancellationToken cancellationToken = default);

    Task<InboundFaxResponse?> GetInboundFaxAsync(Guid id, CancellationToken cancellationToken = default);

    Task<InboundFaxResponse> AssignInboundFaxAsync(
        Guid id,
        AssignInboundFaxRequest request,
        Guid assignedByUserId,
        CancellationToken cancellationToken = default);

    Task<HumbleWebhookAcceptanceResponse> AcceptHumbleWebhookAsync(
        string connectionToken,
        string payloadJson,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchHepExercisesAsync(
        string query,
        string locale,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HepProgramResponse>> GetHepProgramsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<HepProgramResponse> CreateHepProgramAsync(
        Guid patientId,
        CreateHepProgramRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default);

    Task<HepProgramResponse> UpdateHepProgramAsync(
        Guid programId,
        CreateHepProgramRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default);

    Task<HepProgramResponse> PublishHepProgramAsync(
        Guid programId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HepTrackingObservationResponse>> GetHepTrackingAsync(
        Guid programId,
        CancellationToken cancellationToken = default);

    Task<ProviderLaunchResponse> CreateClinicianLaunchAsync(
        Guid programId,
        Guid userId,
        bool flowSheet,
        CancellationToken cancellationToken = default);

    Task<ProviderLaunchResponse> CreatePatientLaunchAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IntegrationDeadLetterResponse>> GetDeadLettersAsync(
        CancellationToken cancellationToken = default);

    Task<IntegrationDeadLetterResponse> ReplayDeadLetterAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);
}
