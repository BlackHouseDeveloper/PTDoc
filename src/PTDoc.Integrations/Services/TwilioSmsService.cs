using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// Twilio-backed SMS delivery with config-gated disabled behavior.
/// </summary>
public sealed class TwilioSmsService : ISmsDeliveryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _accountSid;
    private readonly string? _authToken;
    private readonly string? _fromNumber;
    private readonly string _baseUrl;
    private readonly bool _isEnabled;

    public TwilioSmsService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _accountSid = configuration["Integrations:Sms:AccountSid"];
        _authToken = configuration["Integrations:Sms:AuthToken"];
        _fromNumber = configuration["Integrations:Sms:FromNumber"];
        _baseUrl = configuration["Integrations:Sms:BaseUrl"] ?? "https://api.twilio.com/";
        _isEnabled = configuration.GetValue<bool>("Integrations:Sms:Enabled");
    }

    public async Task<SmsDeliveryResult> SendAsync(SmsDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new SmsDeliveryResult
            {
                Success = false,
                ErrorMessage = "SMS delivery is disabled."
            };
        }

        if (string.IsNullOrWhiteSpace(_accountSid) || string.IsNullOrWhiteSpace(_authToken) || string.IsNullOrWhiteSpace(_fromNumber))
        {
            return new SmsDeliveryResult
            {
                Success = false,
                ErrorMessage = "SMS delivery is not configured."
            };
        }

        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_baseUrl);

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_accountSid}:{_authToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using var response = await client.PostAsync(
            $"/2010-04-01/Accounts/{_accountSid}/Messages.json",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["From"] = _fromNumber,
                ["To"] = request.ToNumber,
                ["Body"] = request.Message
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new SmsDeliveryResult
            {
                Success = false,
                ErrorMessage = $"Twilio returned {(int)response.StatusCode}."
            };
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? providerMessageId = null;

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("sid", out var sidProperty) && sidProperty.ValueKind == JsonValueKind.String)
            {
                providerMessageId = sidProperty.GetString();
            }
        }
        catch (JsonException)
        {
            providerMessageId = null;
        }

        return new SmsDeliveryResult
        {
            Success = true,
            ProviderMessageId = providerMessageId
        };
    }
}
