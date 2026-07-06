using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Integrations;

namespace PTDoc.UI.Services;

public sealed class PaymentClientApiService(HttpClient httpClient) : IPaymentClientService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<PaymentClientConfigurationResponse> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<PaymentClientConfigurationResponse>(
            "/api/v1/integrations/payment/configuration",
            SerializerOptions,
            cancellationToken);

        return response ?? new PaymentClientConfigurationResponse();
    }
}
