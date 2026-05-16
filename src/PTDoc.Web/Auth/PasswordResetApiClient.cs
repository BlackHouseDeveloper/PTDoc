namespace PTDoc.Web.Auth;

using System.Net.Http.Json;

public sealed class PasswordResetApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PasswordResetApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> RequestAsync(
        string contact,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PTDocAuthApi");
        var endpoint = string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase)
            ? "/api/communications/password-reset/send-sms"
            : "/api/communications/password-reset/send-email";

        var response = await client.PostAsJsonAsync(
            endpoint,
            new { recipient = contact },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CompleteAsync(
        string token,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PTDocAuthApi");
        var response = await client.PostAsJsonAsync(
            "/api/communications/password-reset/complete",
            new { token, newPin },
            cancellationToken);

        return response.IsSuccessStatusCode;
    }
}
