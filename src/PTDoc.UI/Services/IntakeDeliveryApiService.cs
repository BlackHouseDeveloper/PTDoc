using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Intake;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-backed intake delivery service for staff share-link, QR, email, and SMS workflows.
/// </summary>
public sealed class IntakeDeliveryApiService(HttpClient httpClient) : IIntakeDeliveryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IntakeDeliveryBundleResponse> GetDeliveryBundleAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/v1/intake/{intakeId}/delivery/link", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IntakeDeliveryBundleResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Delivery bundle payload was empty.");
    }

    public async Task<IntakeDeliverySendResult> SendInviteAsync(IntakeSendInviteRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync($"/api/v1/intake/{request.IntakeId}/delivery/send", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = request.IntakeId,
                Channel = request.Channel,
                ErrorMessage = await ReadErrorAsync(response, cancellationToken)
            };
        }

        return await response.Content.ReadFromJsonAsync<IntakeDeliverySendResult>(SerializerOptions, cancellationToken)
            ?? new IntakeDeliverySendResult
            {
                Success = false,
                IntakeId = request.IntakeId,
                Channel = request.Channel,
                ErrorMessage = "Delivery response payload was empty."
            };
    }

    public async Task<IntakeDeliveryStatusResponse> GetDeliveryStatusAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/intake/{intakeId}/delivery/status", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<IntakeDeliveryStatusResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Delivery status payload was empty.");
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("error", out var errorProperty) && errorProperty.ValueKind == JsonValueKind.String)
            {
                return errorProperty.GetString() ?? "Request failed.";
            }
        }
        catch (JsonException)
        {
        }

        return $"Request failed with status {(int)response.StatusCode}.";
    }
}
