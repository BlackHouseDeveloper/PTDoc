using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// Humble Fax HTTP adapter. Request/response bodies are deliberately not logged because
/// they contain recipient and document metadata.
/// </summary>
public sealed class HumbleFaxService : IFaxService, IFaxProviderClient
{
    private static readonly ConcurrentDictionary<Guid, RateState> RateStates = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIntegrationSecretResolver _secretResolver;
    private readonly ILogger<HumbleFaxService> _logger;
    private readonly HumbleFaxOptions _options;
    private readonly IConfiguration _configuration;

    public HumbleFaxService(
        IHttpClientFactory httpClientFactory,
        IIntegrationSecretResolver secretResolver,
        IConfiguration configuration,
        ILogger<HumbleFaxService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secretResolver = secretResolver;
        _configuration = configuration;
        _logger = logger;
        _options = configuration.GetSection(HumbleFaxOptions.SectionName).Get<HumbleFaxOptions>()
            ?? new HumbleFaxOptions();
    }

    public HumbleFaxService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        : this(
            httpClientFactory,
            new LegacyConfigurationSecretResolver(configuration),
            configuration,
            NullLogger<HumbleFaxService>.Instance)
    {
    }

    public async Task<ProviderConnectionHealth> VerifyAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(connection, $"/sentFaxes?timeFrom={now - 60}&timeTo={now}"),
            credentials);
        using var response = await SendAsync(connection.Id, request, cancellationToken);
        return response.IsSuccessStatusCode
            ? new ProviderConnectionHealth(true, "healthy")
            : new ProviderConnectionHealth(false, $"http_{(int)response.StatusCode}");
    }

    public async Task<ProviderFaxSubmission> SubmitFaxAsync(
        IntegrationConnectionContext connection,
        ProviderFaxSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        return await SubmitWithCredentialsAsync(connection, request, credentials, cancellationToken);
    }

    public async Task<ProviderFaxStatus> GetFaxStatusAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(connection, $"/sentFax/{Uri.EscapeDataString(providerFaxId)}"),
            credentials);
        using var response = await SendAsync(connection.Id, request, cancellationToken);
        await EnsureSuccessAsync(response, "status", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        var fax = GetRequiredObject(document.RootElement, "data", "sentFax");
        return new ProviderFaxStatus(
            GetString(fax, "id") ?? providerFaxId,
            GetString(fax, "status") ?? "unknown",
            GetNullableInt(fax, "numPages"),
            ParseRecipients(fax));
    }

    public async Task<ProviderInboundFax> GetInboundFaxAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(connection, $"/incomingFax/{Uri.EscapeDataString(providerFaxId)}"),
            credentials);
        using var response = await SendAsync(connection.Id, request, cancellationToken);
        await EnsureSuccessAsync(response, "inbound_metadata", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        var fax = GetRequiredObject(document.RootElement, "data", "incomingFax");
        return ParseInboundFax(fax, providerFaxId);
    }

    public async Task<IReadOnlyList<ProviderInboundFax>> GetInboundFaxesAsync(
        IntegrationConnectionContext connection,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        var configuration = ParseConfiguration(connection.ConfigurationJson);
        var from = new DateTimeOffset(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var to = new DateTimeOffset(DateTime.SpecifyKind(toUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var destination = string.IsNullOrWhiteSpace(configuration.FromNumber)
            ? string.Empty
            : $"&toNumber={Uri.EscapeDataString(NormalizeFaxNumber(configuration.FromNumber))}";
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(connection, $"/incomingFaxes?timeFrom={from}&timeTo={to}{destination}"),
            credentials);
        using var response = await SendAsync(connection.Id, request, cancellationToken);
        await EnsureSuccessAsync(response, "inbound_list", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        var data = GetRequiredObject(document.RootElement, "data");
        if (!TryGetPropertyIgnoreCase(data, "incomingFaxes", out var incoming) || incoming.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProviderInboundFax>();
        }
        return incoming.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => ParseInboundFax(item, GetString(item, "id") ?? string.Empty))
            .Where(item => !string.IsNullOrWhiteSpace(item.ProviderFaxId))
            .ToArray();
    }

    private static ProviderInboundFax ParseInboundFax(JsonElement fax, string fallbackId)
    {
        var unixTime = GetNullableLong(fax, "time") ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new ProviderInboundFax(
            GetString(fax, "id") ?? fallbackId,
            GetString(fax, "status") ?? "unknown",
            GetString(fax, "fromNumber") ?? string.Empty,
            GetString(fax, "toNumber") ?? string.Empty,
            GetString(fax, "fromNameAddressBook") ?? GetString(fax, "fromNameIdentity"),
            GetNullableInt(fax, "numPages") ?? 0,
            DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime);
    }

    public async Task<ProviderDocumentDownload> DownloadInboundFaxAsync(
        IntegrationConnectionContext connection,
        string providerFaxId,
        CancellationToken cancellationToken = default)
    {
        var credentials = await _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(connection, $"/incomingFax/{Uri.EscapeDataString(providerFaxId)}/download?fileFormat=pdf"),
            credentials);
        var response = await SendAsync(
            connection.Id,
            request,
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
        request.Dispose();
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            throw new HttpRequestException("Humble Fax inbound download failed.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var responseContentType = response.Content.Headers.ContentType?.MediaType;
        return new ProviderDocumentDownload(
            new ResponseOwnedStream(stream, response),
            $"inbound-fax-{providerFaxId}.pdf",
            string.Equals(responseContentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                ? responseContentType!
                : "application/pdf");
    }

    public async Task<FaxResult> SendFaxAsync(FaxRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new FaxResult { Success = false, ErrorMessage = "Fax service is disabled" };
        }

        var accessKey = _configuration["Integrations:Fax:ApiKey"];
        var secretKey = _configuration["Integrations:Fax:ApiSecret"];
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            return new FaxResult { Success = false, ErrorMessage = "Fax service not configured" };
        }

        try
        {
            await using var content = new MemoryStream(request.PdfContent, writable: false);
            var context = new IntegrationConnectionContext(
                Guid.Empty,
                Guid.Empty,
                PTDoc.Core.Models.IntegrationProviders.HumbleFax,
                "{}",
                string.Empty);
            var submission = await SubmitWithCredentialsAsync(
                context,
                new ProviderFaxSubmitRequest(
                    Guid.NewGuid().ToString("N"),
                    [request.RecipientNumber],
                    request.RecipientName,
                    "ptdoc-document.pdf",
                    "application/pdf",
                    content,
                    request.DocumentType,
                    request.CoverPageMessage,
                    true),
                new IntegrationSecretBundle(accessKey, secretKey),
                cancellationToken);

            return new FaxResult
            {
                Success = true,
                FaxId = submission.ProviderFaxId,
                Status = submission.ProviderStatus,
                SentAt = DateTime.UtcNow,
                PageCount = submission.PageCount
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(exception, "Humble Fax legacy submission failed.");
            return new FaxResult { Success = false, ErrorMessage = "Fax submission failed" };
        }
    }

    public async Task<FaxResult> GetFaxStatusAsync(string faxId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new FaxResult { Success = false, ErrorMessage = "Fax service is disabled" };
        }

        var accessKey = _configuration["Integrations:Fax:ApiKey"];
        var secretKey = _configuration["Integrations:Fax:ApiSecret"];
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            return new FaxResult { Success = false, ErrorMessage = "Fax service not configured" };
        }

        var context = new IntegrationConnectionContext(Guid.Empty, Guid.Empty, "HumbleFax", "{}", string.Empty);
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildUri(context, $"/sentFax/{Uri.EscapeDataString(faxId)}"),
            new IntegrationSecretBundle(accessKey, secretKey));
        using var response = await SendAsync(Guid.Empty, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new FaxResult { Success = false, FaxId = faxId, ErrorMessage = "Fax status lookup failed" };
        }

        using var document = await ParseJsonAsync(response, cancellationToken);
        var fax = GetRequiredObject(document.RootElement, "data", "sentFax");
        return new FaxResult
        {
            Success = true,
            FaxId = faxId,
            Status = GetString(fax, "status"),
            PageCount = GetNullableInt(fax, "numPages")
        };
    }

    private async Task<ProviderFaxSubmission> SubmitWithCredentialsAsync(
        IntegrationConnectionContext connection,
        ProviderFaxSubmitRequest request,
        IntegrationSecretBundle credentials,
        CancellationToken cancellationToken)
    {
        var configuration = ParseConfiguration(connection.ConfigurationJson);
        var normalizedRecipients = request.Recipients.Select(NormalizeFaxNumber).ToArray();
        if (normalizedRecipients.Length is < 1 or > 3)
        {
            throw new InvalidOperationException("Humble Fax supports one to three recipients per transmission.");
        }

        using var multipart = new MultipartFormDataContent();
        var payload = new Dictionary<string, object?>
        {
            ["recipients"] = normalizedRecipients.Select(long.Parse).ToArray(),
            ["toName"] = request.RecipientName,
            ["fromNumber"] = string.IsNullOrWhiteSpace(configuration.FromNumber)
                ? null
                : long.Parse(NormalizeFaxNumber(configuration.FromNumber)),
            ["subject"] = request.Subject,
            ["message"] = request.Message,
            ["resolution"] = "Fine",
            ["pageSize"] = "Letter",
            ["uuid"] = request.ClientCorrelationId,
            ["includeCoversheet"] = request.IncludeCoverSheet
        };
        multipart.Add(new StringContent(JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }), Encoding.UTF8), "jsonData");
        var fileContent = new StreamContent(request.Content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        multipart.Add(fileContent, request.FileName, request.FileName);

        using var message = CreateRequest(HttpMethod.Post, BuildUri(connection, "/quickSendFax"), credentials);
        message.Content = multipart;
        using var response = await SendAsync(connection.Id, message, cancellationToken);
        await EnsureSuccessAsync(response, "submit", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        var fax = GetRequiredObject(document.RootElement, "data", "fax");
        return new ProviderFaxSubmission(
            GetString(fax, "id") ?? throw new JsonException("Humble Fax response did not include a fax id."),
            GetString(fax, "status") ?? "in progress",
            GetNullableInt(fax, "numPages"),
            ParseRecipients(fax));
    }

    private async Task<HttpResponseMessage> SendAsync(
        Guid connectionId,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var state = RateStates.GetOrAdd(connectionId, _ => new RateState());
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            // Budget the documented five-request/second credential ceiling across the
            // configured maximum number of simultaneously running API instances.
            var instanceBudget = Math.Clamp(_options.RateLimitInstanceBudget, 1, 20);
            var wait = TimeSpan.FromMilliseconds(210 * instanceBudget) - (DateTimeOffset.UtcNow - state.LastRequestAt);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
            state.LastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            state.Gate.Release();
        }

        var client = _httpClientFactory.CreateClient(nameof(HumbleFaxService));
        return await client.SendAsync(request, completionOption, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string uri,
        IntegrationSecretBundle credentials)
    {
        var request = new HttpRequestMessage(method, uri);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return request;
    }

    private string BuildUri(IntegrationConnectionContext connection, string path)
    {
        var configuration = ParseConfiguration(connection.ConfigurationJson);
        var baseUrl = string.IsNullOrWhiteSpace(configuration.BaseUrl) ? _options.BaseUrl : configuration.BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Humble Fax base URL must be HTTPS.");
        }
        return $"{baseUrl.TrimEnd('/')}{path}";
    }

    private static HumbleConnectionConfiguration ParseConfiguration(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<HumbleConnectionConfiguration>(
                string.IsNullOrWhiteSpace(json) ? "{}" : json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HumbleConnectionConfiguration();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Humble Fax connection configuration is invalid.");
        }
    }

    private static string NormalizeFaxNumber(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 10)
        {
            digits = "1" + digits;
        }
        if (digits.Length != 11 || digits[0] != '1')
        {
            throw new InvalidOperationException("Fax numbers must be valid US or Canadian numbers.");
        }
        return digits;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
        var exception = new HttpRequestException($"Humble Fax {operation} request failed.", null, response.StatusCode);
        if (GetRetryAfter(response) is { } retryAfter)
        {
            exception.Data["RetryAfterMilliseconds"] = retryAfter.TotalMilliseconds;
        }
        throw exception;
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }
        if (retryAfter?.Date is { } retryAt)
        {
            var wait = retryAt - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }
        return null;
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static JsonElement GetRequiredObject(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!TryGetPropertyIgnoreCase(current, segment, out current) || current.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Humble Fax response shape was invalid.");
            }
        }
        return current;
    }

    private static IReadOnlyList<ProviderFaxRecipientStatus> ParseRecipients(JsonElement fax)
    {
        if (!TryGetPropertyIgnoreCase(fax, "recipients", out var recipients) || recipients.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ProviderFaxRecipientStatus>();
        }

        return recipients.EnumerateArray().Select(recipient => new ProviderFaxRecipientStatus(
            GetString(recipient, "toNumber") ?? string.Empty,
            GetString(recipient, "status") ?? "unknown",
            GetNullableInt(recipient, "numAttempts") ?? 0,
            FindStringRecursive(recipient, "failureReason"))).ToArray();
    }

    private static string? FindStringRecursive(JsonElement element, string property)
    {
        var direct = GetString(element, property);
        if (direct is not null)
        {
            return direct;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var child in element.EnumerateObject())
            {
                var value = FindStringRecursive(child.Value, property);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var value = FindStringRecursive(child, property);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!TryGetPropertyIgnoreCase(element, property, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? GetNullableInt(JsonElement element, string property) =>
        int.TryParse(GetString(element, property), out var value) ? value : null;

    private static long? GetNullableLong(JsonElement element, string property) =>
        long.TryParse(GetString(element, property), out var value) ? value : null;

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string property, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (string.Equals(candidate.Name, property, StringComparison.OrdinalIgnoreCase))
                {
                    value = candidate.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private sealed class HumbleConnectionConfiguration
    {
        public string? BaseUrl { get; set; }
        public string? FromNumber { get; set; }
    }

    private sealed class RateState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public DateTimeOffset LastRequestAt { get; set; } = DateTimeOffset.MinValue;
    }

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class LegacyConfigurationSecretResolver(IConfiguration configuration) : IIntegrationSecretResolver
    {
        public Task<IntegrationSecretBundle> ResolveAsync(
            string secretReference,
            CancellationToken cancellationToken = default)
        {
            var accessKey = configuration["Integrations:Fax:ApiKey"];
            var secretKey = configuration["Integrations:Fax:ApiSecret"];
            if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            {
                throw new InvalidOperationException("Humble Fax credentials are not configured.");
            }
            return Task.FromResult(new IntegrationSecretBundle(accessKey, secretKey));
        }
    }
}
