using PTDoc.Core.Models;

namespace PTDoc.Application.Integrations;

public sealed record IntegrationConnectionContext(
    Guid Id,
    Guid ClinicId,
    string Provider,
    string ConfigurationJson,
    string SecretReference);

public sealed record IntegrationSecretBundle(string Username, string Password);

public interface IIntegrationSecretResolver
{
    Task<IntegrationSecretBundle> ResolveAsync(string secretReference, CancellationToken cancellationToken = default);
}

public interface IIntegrationDocumentStore
{
    Task<StoredIntegrationDocument> SaveAsync(
        Guid clinicId,
        string category,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public interface IIntegrationDocumentScanner
{
    Task ScanAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
}

public interface IIntegrationLaunchTicketStore
{
    Task StoreAsync(
        string token,
        string providerLaunchUrl,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default);

    Task<string?> ConsumeAsync(
        string token,
        CancellationToken cancellationToken = default);
}

public sealed record StoredIntegrationDocument(
    string StorageKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string HashSha256);

public interface IIntegrationJobProcessor
{
    Task<int> ProcessAvailableAsync(int batchSize, CancellationToken cancellationToken = default);
    Task EnqueueRecurringWorkAsync(CancellationToken cancellationToken = default);
}

public interface IFaxProviderClient
{
    Task<ProviderConnectionHealth> VerifyAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken = default);

    Task<ProviderFaxSubmission> SubmitFaxAsync(
        IntegrationConnectionContext connection,
        ProviderFaxSubmitRequest request,
        CancellationToken cancellationToken = default);

    Task<ProviderFaxStatus> GetFaxStatusAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default);

    Task<ProviderInboundFax> GetInboundFaxAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderInboundFax>> GetInboundFaxesAsync(
        IntegrationConnectionContext connection,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    Task<ProviderDocumentDownload> DownloadInboundFaxAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderFaxSubmitRequest(
    string ClientCorrelationId,
    IReadOnlyList<string> Recipients,
    string? RecipientName,
    string FileName,
    string ContentType,
    Stream Content,
    string? Subject,
    string? Message,
    bool IncludeCoverSheet);

public sealed record ProviderFaxSubmission(
    string ProviderFaxId,
    string ProviderStatus,
    int? PageCount,
    IReadOnlyList<ProviderFaxRecipientStatus> Recipients);

public sealed record ProviderFaxStatus(
    string ProviderFaxId,
    string ProviderStatus,
    int? PageCount,
    IReadOnlyList<ProviderFaxRecipientStatus> Recipients);

public sealed record ProviderFaxRecipientStatus(
    string FaxNumber,
    string Status,
    int AttemptCount,
    string? FailureCode);

public sealed record ProviderInboundFax(
    string ProviderFaxId,
    string Status,
    string FromNumber,
    string ToNumber,
    string? SenderName,
    int PageCount,
    DateTime ReceivedAtUtc);

public sealed record ProviderDocumentDownload(
    Stream Content,
    string FileName,
    string ContentType) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record ProviderConnectionHealth(bool Success, string Code);

public interface IWibbiProviderClient
{
    Task<ProviderConnectionHealth> VerifyAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken = default);

    Task EnsureUserAsync(
        IntegrationConnectionContext connection,
        WibbiUserProvisioning user,
        CancellationToken cancellationToken = default);

    Task EnsurePatientAsync(
        IntegrationConnectionContext connection,
        WibbiPatientProvisioning patient,
        CancellationToken cancellationToken = default);

    Task EnsureEpisodeAsync(
        IntegrationConnectionContext connection,
        WibbiEpisodeProvisioning episode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchExercisesAsync(
        IntegrationConnectionContext connection,
        string query,
        string locale,
        CancellationToken cancellationToken = default);

    Task<WibbiProgramPublishResult> PublishProgramAsync(
        IntegrationConnectionContext connection,
        WibbiProgramPublishRequest request,
        CancellationToken cancellationToken = default);

    Task<string> GetClinicianLaunchUrlAsync(
        IntegrationConnectionContext connection,
        string userId,
        string? programId,
        bool flowSheet,
        CancellationToken cancellationToken = default);

    Task<string> GetPatientLaunchUrlAsync(
        IntegrationConnectionContext connection,
        string patientId,
        string? programId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WibbiTrackingValue>> GetTrackingAsync(
        IntegrationConnectionContext connection,
        string patientId,
        string programId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WibbiProgramChange>> GetChangesAsync(
        IntegrationConnectionContext connection,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}

public sealed record WibbiUserProvisioning(
    string UserId,
    string FirstName,
    string LastName,
    string? Email,
    string Locale,
    string Title,
    bool Existing = false);

public sealed record WibbiPatientProvisioning(
    string PatientId,
    string UserId,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string? InsuranceProvider,
    string Locale,
    bool Existing = false);

public sealed record WibbiEpisodeProvisioning(
    string PatientId,
    string EpisodeId,
    string Name,
    DateOnly StartDate);

public sealed record WibbiExerciseCatalogItem(
    string ExternalExerciseId,
    string Title,
    string? Description,
    string? ImageUrl,
    string? VideoUrl);

public sealed record WibbiProgramPublishRequest(
    string ProgramId,
    string? ExistingProviderProgramId,
    string PatientId,
    string UserId,
    string EpisodeId,
    string Title,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? Notes,
    IReadOnlyList<WibbiProgramExercise> Exercises);

public sealed record WibbiProgramExercise(
    string ExerciseId,
    string Title,
    string? Description,
    string? Sets,
    string? Repetitions,
    string? Weight,
    string? Frequency,
    string? Duration,
    string? Hold,
    string? Tempo,
    string? Rest,
    string? Level,
    string? Other,
    bool Home,
    bool Mirror,
    bool Flip);

public sealed record WibbiProgramPublishResult(string ProgramId, string? ProviderVersion);

public sealed record WibbiTrackingValue(
    string ObservationId,
    string? ExerciseId,
    string Code,
    string Value,
    string? UnitOfMeasure,
    DateTime ActivityAtUtc);

public sealed record WibbiProgramChange(
    string ProgramId,
    string? PatientId,
    DateTime ChangedAtUtc,
    string? ProviderVersion);
