using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// Wibbi Home Exercise Program service implementation.
/// </summary>
public class WibbiHepService : IHomeExerciseProgramService
{
    private const string WibbiSystemName = "Wibbi";
    private const string TokenCacheKey = "integrations:wibbi:api-token";
    private static readonly string[] CredentialQueryParameters = ["username", "password"];
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalSystemMappingService _mappingService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<WibbiHepService> _logger;
    private readonly WibbiHepOptions _options;

    public WibbiHepService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IExternalSystemMappingService mappingService,
        IMemoryCache memoryCache,
        ILogger<WibbiHepService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _mappingService = mappingService;
        _memoryCache = memoryCache;
        _logger = logger;
        _options = configuration.GetSection(WibbiHepOptions.SectionName).Get<WibbiHepOptions>() ?? new WibbiHepOptions();
    }

    public Task<HepAssignmentResult> AssignProgramAsync(HepAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is disabled.", "assign_program");
        }

        return Task.FromResult(new HepAssignmentResult
        {
            Success = false,
            ErrorMessage = "Clinician-side Wibbi assignment is not enabled in this phase."
        });
    }

    public async Task<HepAssignmentResult> GetPatientProgramAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is disabled.", "patient_launch");
        }

        if (!IsConfigured())
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is not configured.", "patient_launch");
        }

        var mapping = (await _mappingService.GetPatientMappingsAsync(patientId, cancellationToken))
            .FirstOrDefault(m => string.Equals(m.ExternalSystemName, WibbiSystemName, StringComparison.OrdinalIgnoreCase)
                                 && m.IsActive);

        if (mapping is null)
        {
            throw new ProvisioningException(new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = false,
                PrincipalType = PrincipalTypes.Patient,
                Provider = WibbiSystemName,
                FailureCode = "patient_external_mapping_missing",
                FailureReason = "Authenticated patient is not linked to Wibbi."
            });
        }

        try
        {
            var apiToken = await GetApiTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient(nameof(WibbiHepService));
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildClientLinkUri(mapping.ExternalId));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi patient launch request failed.",
                    "patient_launch",
                    (int)response.StatusCode);
            }

            WibbiLaunchResponse? launchPayload;
            try
            {
                launchPayload = await ParseLaunchResponseAsync(response, cancellationToken);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Wibbi launch response was invalid for patient {PatientId}.",
                    patientId);
                throw new WibbiAuthenticationException(
                    "Wibbi returned an invalid launch response.",
                    "patient_launch",
                    (int)response.StatusCode,
                    exception);
            }

            if (launchPayload is null || string.IsNullOrWhiteSpace(launchPayload.Url))
            {
                throw new WibbiAuthenticationException(
                    launchPayload?.Error ?? "Wibbi did not return a launch URL.",
                    "patient_launch");
            }

            ValidateLaunchUrl(launchPayload.Url, patientId);

            return new HepAssignmentResult
            {
                Success = true,
                AssignmentId = mapping.ExternalId,
                PatientPortalUrl = launchPayload.Url
            };
        }
        catch (WibbiAuthenticationException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Wibbi launch request failed for patient {PatientId}. StatusCode={StatusCode}",
                patientId,
                (int?)exception.StatusCode);
            throw new WibbiAuthenticationException(
                "Unable to launch Wibbi portal.",
                "patient_launch",
                (int?)exception.StatusCode,
                exception);
        }
    }

    private void ValidateLaunchUrl(string launchUrl, Guid patientId)
    {
        if (!Uri.TryCreate(launchUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new WibbiAuthenticationException(
                "Wibbi returned an invalid launch URL.",
                "patient_launch");
        }

        var blockedParameters = GetBlockedParameters(uri);
        if (blockedParameters.Count == 0)
        {
            return;
        }

        if (_options.AllowCredentialBearingRedirects)
        {
            _logger.LogWarning(
                "Allowing Wibbi launch URL with credential-bearing query parameters for patient {PatientId}. Parameters={BlockedParameters}",
                patientId,
                string.Join(", ", blockedParameters));
            return;
        }

        _logger.LogWarning(
            "Rejected unsafe Wibbi launch URL for patient {PatientId}. Parameters={BlockedParameters}",
            patientId,
            string.Join(", ", blockedParameters));

        throw new WibbiUnsafeLaunchUrlException(
            "Wibbi returned a credential-bearing launch URL, which is not allowed by PTDoc configuration.",
            "patient_launch",
            blockedParameters);
    }

    private static IReadOnlyCollection<string> GetBlockedParameters(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return Array.Empty<string>();
        }

        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Select(Uri.UnescapeDataString)
            .Where(name => CredentialQueryParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_options.ApiUsername)
            && !string.IsNullOrWhiteSpace(_options.ApiPassword)
            && !string.IsNullOrWhiteSpace(_options.Entity)
            && !string.IsNullOrWhiteSpace(_options.ClinicLicenseId);
    }

    private async Task<string> GetApiTokenAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue<WibbiApiSession>(TokenCacheKey, out var cachedSession)
            && cachedSession is not null
            && cachedSession.ExpiresAtUtc > DateTimeOffset.UtcNow.Add(_options.TokenRefreshSkew))
        {
            return cachedSession.Token;
        }

        var client = _httpClientFactory.CreateClient(nameof(WibbiHepService));
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(
                $"{_options.BaseUrl.TrimEnd('/')}/api/v4/authentication/login",
                new
                {
                    username = _options.ApiUsername,
                    password = _options.ApiPassword
                },
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Wibbi API authentication request failed. StatusCode={StatusCode}",
                (int?)exception.StatusCode);
            throw new WibbiAuthenticationException(
                "Unable to authenticate with Wibbi.",
                "authenticate",
                (int?)exception.StatusCode,
                exception);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new WibbiAuthenticationException(
                "Wibbi API authentication failed.",
                "authenticate",
                (int)response.StatusCode);
        }

        WibbiLoginResponse loginPayload;
        try
        {
            loginPayload = await ParseLoginResponseAsync(response, cancellationToken);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Wibbi login response was invalid.");
            throw new WibbiAuthenticationException(
                "Wibbi returned an invalid authentication response.",
                "authenticate",
                (int)response.StatusCode,
                exception);
        }

        if (string.IsNullOrWhiteSpace(loginPayload.Token))
        {
            throw new WibbiAuthenticationException("Wibbi login response did not include a bearer token.", "authenticate");
        }

        var session = new WibbiApiSession(loginPayload.Token, loginPayload.Expires);
        _memoryCache.Set(TokenCacheKey, session, session.ExpiresAtUtc);
        return session.Token;
    }

    private static async Task<WibbiLoginResponse> ParseLoginResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("token", out var tokenElement))
        {
            throw new JsonException("Wibbi login response did not include a token property.");
        }

        if (!document.RootElement.TryGetProperty("expires", out var expiresElement))
        {
            throw new JsonException("Wibbi login response did not include an expires property.");
        }

        var token = tokenElement.GetString()
            ?? throw new WibbiAuthenticationException("Wibbi login response did not include a bearer token.", "authenticate");

        var expires = expiresElement.GetDateTimeOffset();
        return new WibbiLoginResponse
        {
            Token = token,
            Expires = expires
        };
    }

    private static async Task<WibbiLaunchResponse?> ParseLaunchResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("URL", out var urlElement);
        root.TryGetProperty("error", out var errorElement);

        return new WibbiLaunchResponse
        {
            URL = urlElement.ValueKind == JsonValueKind.String ? urlElement.GetString() : null,
            Error = errorElement.ValueKind == JsonValueKind.String ? errorElement.GetString() : null
        };
    }

    private string BuildClientLinkUri(string externalClientId)
    {
        var query = new Dictionary<string, string?>
        {
            ["action"] = "GetClientLink",
            ["entity"] = _options.Entity,
            ["clm_id"] = _options.ClinicLicenseId,
            ["client_id"] = externalClientId
        };

        var queryString = string.Join(
            "&",
            query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}"));

        return $"{_options.BaseUrl.TrimEnd('/')}/api/v3/?{queryString}";
    }

    private sealed class WibbiLoginResponse
    {
        public string Token { get; set; } = string.Empty;

        public DateTimeOffset Expires { get; set; }
    }

    private sealed class WibbiLaunchResponse
    {
        public string? URL { get; set; }

        public string? Error { get; set; }

        public string? Url => URL;
    }

    private sealed record WibbiApiSession(string Token, DateTimeOffset ExpiresAtUtc);
}
