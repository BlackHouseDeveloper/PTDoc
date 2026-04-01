using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// SendGrid-backed transactional email delivery with config-gated disabled behavior.
/// </summary>
public sealed class SendGridEmailService : IEmailDeliveryService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _baseUrl;
    private readonly bool _isEnabled;

    public SendGridEmailService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Integrations:Email:ApiKey"];
        _fromEmail = configuration["Integrations:Email:FromEmail"] ?? "noreply@ptdoc.local";
        _fromName = configuration["Integrations:Email:FromName"] ?? "PTDoc";
        _baseUrl = configuration["Integrations:Email:BaseUrl"] ?? "https://api.sendgrid.com/";
        _isEnabled = configuration.GetValue<bool>("Integrations:Email:Enabled");
    }

    public async Task<EmailDeliveryResult> SendAsync(EmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new EmailDeliveryResult
            {
                Success = false,
                ErrorMessage = "Email delivery is disabled."
            };
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return new EmailDeliveryResult
            {
                Success = false,
                ErrorMessage = "Email delivery is not configured."
            };
        }

        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await client.PostAsJsonAsync("/v3/mail/send", new
        {
            from = new
            {
                email = _fromEmail,
                name = _fromName
            },
            personalizations = new[]
            {
                new
                {
                    to = new[]
                    {
                        new
                        {
                            email = request.ToAddress
                        }
                    },
                    subject = request.Subject
                }
            },
            content = BuildContent(request)
        }, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new EmailDeliveryResult
            {
                Success = false,
                ErrorMessage = $"SendGrid returned {(int)response.StatusCode}."
            };
        }

        var providerMessageId = response.Headers.TryGetValues("X-Message-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        return new EmailDeliveryResult
        {
            Success = true,
            ProviderMessageId = providerMessageId
        };
    }

    private static object[] BuildContent(EmailDeliveryRequest request)
    {
        var content = new List<object>
        {
            new
            {
                type = "text/plain",
                value = request.PlainTextBody
            }
        };

        if (!string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            content.Add(new
            {
                type = "text/html",
                value = request.HtmlBody
            });
        }

        return content.ToArray();
    }
}
