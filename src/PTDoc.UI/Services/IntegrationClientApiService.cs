using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Integrations;

namespace PTDoc.UI.Services;

public sealed class IntegrationClientApiService(HttpClient httpClient) : IIntegrationClientService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<IReadOnlyList<IntegrationConnectionResponse>> GetConnectionsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IntegrationConnectionResponse>("/api/v1/integrations/connections", cancellationToken);

    public Task<IntegrationConnectionResponse> SaveConnectionAsync(Guid clinicId, string provider, UpsertIntegrationConnectionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<IntegrationConnectionResponse>(HttpMethod.Put, $"/api/v1/integrations/connections/{Uri.EscapeDataString(provider)}/{clinicId:D}", request, cancellationToken);

    public Task<ProviderConnectionHealth> VerifyConnectionAsync(Guid clinicId, string provider, CancellationToken cancellationToken = default) =>
        SendAsync<ProviderConnectionHealth>(HttpMethod.Post, $"/api/v1/integrations/connections/{Uri.EscapeDataString(provider)}/{clinicId:D}/verify", null, cancellationToken);

    public Task<WebhookTokenRotationResponse> RotateWebhookTokenAsync(Guid clinicId, string provider, CancellationToken cancellationToken = default) =>
        SendAsync<WebhookTokenRotationResponse>(HttpMethod.Post, $"/api/v1/integrations/connections/{Uri.EscapeDataString(provider)}/{clinicId:D}/rotate", null, cancellationToken);

    public Task<FaxTransmissionResponse> QueueFaxAsync(CreateFaxTransmissionRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<FaxTransmissionResponse>(HttpMethod.Post, "/api/v1/integrations/fax/transmissions", request, cancellationToken);

    public Task<IReadOnlyList<FaxTransmissionResponse>> GetFaxTransmissionsAsync(Guid? patientId = null, CancellationToken cancellationToken = default) =>
        GetListAsync<FaxTransmissionResponse>($"/api/v1/integrations/fax/transmissions{(patientId.HasValue ? $"?patientId={patientId:D}" : string.Empty)}", cancellationToken);

    public Task<FaxTransmissionResponse> ResendFaxAsync(Guid transmissionId, CancellationToken cancellationToken = default) =>
        SendAsync<FaxTransmissionResponse>(HttpMethod.Post, $"/api/v1/integrations/fax/transmissions/{transmissionId:D}/resend", null, cancellationToken);

    public Task<IReadOnlyList<InboundFaxResponse>> GetInboundFaxesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<InboundFaxResponse>("/api/v1/integrations/fax/inbox", cancellationToken);

    public async Task<IntegrationContentDownload> DownloadInboundFaxAsync(Guid inboundFaxId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/integrations/fax/inbox/{inboundFaxId:D}/content");
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
            var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                           ?? response.Content.Headers.ContentDisposition?.FileName
                           ?? $"inbound-fax-{inboundFaxId:D}.pdf";
            return new IntegrationContentDownload(
                response,
                content,
                fileName.Trim('"'),
                response.Content.Headers.ContentType?.MediaType ?? "application/pdf");
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public Task<InboundFaxResponse> AssignInboundFaxAsync(Guid inboundFaxId, AssignInboundFaxRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<InboundFaxResponse>(HttpMethod.Post, $"/api/v1/integrations/fax/inbox/{inboundFaxId:D}/assign", request, cancellationToken);

    public Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchExercisesAsync(string query, string locale = "en-US", CancellationToken cancellationToken = default) =>
        GetListAsync<WibbiExerciseCatalogItem>($"/api/v1/integrations/hep/catalog/exercises?query={Uri.EscapeDataString(query)}&locale={Uri.EscapeDataString(locale)}", cancellationToken);

    public Task<IReadOnlyList<HepProgramResponse>> GetHepProgramsAsync(Guid patientId, CancellationToken cancellationToken = default) =>
        GetListAsync<HepProgramResponse>($"/api/v1/integrations/hep/patients/{patientId:D}/programs", cancellationToken);

    public Task<IReadOnlyList<HepProgramResponse>> GetCurrentPatientHepProgramsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<HepProgramResponse>("/api/v1/integrations/hep/patient-programs", cancellationToken);

    public Task<HepProgramResponse> CreateHepProgramAsync(Guid patientId, CreateHepProgramRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<HepProgramResponse>(HttpMethod.Post, $"/api/v1/integrations/hep/patients/{patientId:D}/programs", request, cancellationToken);

    public Task<HepProgramResponse> UpdateHepProgramAsync(Guid programId, CreateHepProgramRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<HepProgramResponse>(HttpMethod.Put, $"/api/v1/integrations/hep/programs/{programId:D}", request, cancellationToken);

    public Task<HepProgramResponse> PublishHepProgramAsync(Guid programId, CancellationToken cancellationToken = default) =>
        SendAsync<HepProgramResponse>(HttpMethod.Post, $"/api/v1/integrations/hep/programs/{programId:D}/publish", null, cancellationToken);

    public Task<IReadOnlyList<HepTrackingObservationResponse>> GetHepTrackingAsync(Guid programId, CancellationToken cancellationToken = default) =>
        GetListAsync<HepTrackingObservationResponse>($"/api/v1/integrations/hep/programs/{programId:D}/tracking", cancellationToken);

    public Task<ProviderLaunchResponse> CreateLaunchAsync(Guid programId, bool flowSheet, CancellationToken cancellationToken = default) =>
        SendAsync<ProviderLaunchResponse>(HttpMethod.Post, $"/api/v1/integrations/hep/programs/{programId:D}/{(flowSheet ? "flowsheet" : "clinician")}-launch", null, cancellationToken);

    public Task<ProviderLaunchResponse> CreatePatientLaunchAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ProviderLaunchResponse>(HttpMethod.Post, "/api/v1/integrations/hep/patient-launch-ticket", null, cancellationToken);

    public Task<IReadOnlyList<IntegrationDeadLetterResponse>> GetDeadLettersAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IntegrationDeadLetterResponse>("/api/v1/integrations/operations/dead-letters", cancellationToken);

    public Task<IntegrationDeadLetterResponse> ReplayDeadLetterAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        SendAsync<IntegrationDeadLetterResponse>(HttpMethod.Post, $"/api/v1/integrations/operations/dead-letters/{jobId:D}/replay", null, cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, cancellationToken) ?? [];
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Integration response was empty.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var message = "The integration request could not be completed.";
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                message = error.GetString() ?? message;
            }
            else if (document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
            {
                message = title.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException(message);
    }
}
