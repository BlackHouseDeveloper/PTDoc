using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
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
/// Wibbi/Physiotec adapter. Provider payloads and delegated launch URLs are never logged.
/// </summary>
public sealed class WibbiHepService : IHomeExerciseProgramService, IWibbiProviderClient
{
    private const string WibbiSystemName = "Wibbi";
    private static readonly string[] CredentialQueryParameters = ["username", "password"];
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TokenLocks = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalSystemMappingService _mappingService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<WibbiHepService> _logger;
    private readonly WibbiHepOptions _options;
    private readonly IIntegrationSecretResolver? _secretResolver;
    private readonly IConfiguration _configuration;

    public WibbiHepService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IExternalSystemMappingService mappingService,
        IMemoryCache memoryCache,
        ILogger<WibbiHepService> logger,
        IIntegrationSecretResolver? secretResolver = null)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _mappingService = mappingService;
        _memoryCache = memoryCache;
        _logger = logger;
        _secretResolver = secretResolver;
        _options = configuration.GetSection(WibbiHepOptions.SectionName).Get<WibbiHepOptions>()
            ?? new WibbiHepOptions();
    }

    public async Task<ProviderConnectionHealth> VerifyAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await GetApiTokenAsync(connection, cancellationToken);
            return new ProviderConnectionHealth(true, "healthy");
        }
        catch (WibbiAuthenticationException exception)
        {
            return new ProviderConnectionHealth(
                false,
                $"authentication_{exception.UpstreamStatusCode?.ToString(CultureInfo.InvariantCulture) ?? "failed"}");
        }
    }

    public async Task EnsureUserAsync(
        IntegrationConnectionContext connection,
        WibbiUserProvisioning user,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = BuildV3Uri(settings, new Dictionary<string, string?>
        {
            ["action"] = user.Existing ? "ModifyUser" : "AddUser",
            ["entity"] = settings.Entity,
            ["clm_id"] = settings.ClinicLicenseId,
            ["user_id"] = user.UserId,
            ["lastname"] = user.LastName,
            ["firstname"] = user.FirstName,
            ["email"] = user.Email,
            ["locale"] = user.Locale,
            ["title"] = user.Title,
            ["accessOwnClient"] = "0",
            ["output_format"] = "json"
        });
        try
        {
            await SendSemanticRequestAsync(connection, HttpMethod.Get, uri, null, "ensure_user", cancellationToken);
        }
        catch (WibbiAuthenticationException exception) when (!user.Existing && exception.UpstreamStatusCode == 200)
        {
            var modifyUri = uri.Replace("action=AddUser", "action=ModifyUser", StringComparison.Ordinal);
            await SendSemanticRequestAsync(connection, HttpMethod.Get, modifyUri, null, "update_user", cancellationToken);
        }
    }

    public async Task EnsurePatientAsync(
        IntegrationConnectionContext connection,
        WibbiPatientProvisioning patient,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = BuildV3Uri(settings, new Dictionary<string, string?>
        {
            ["action"] = patient.Existing ? "ModifyClient" : "AddClient",
            ["entity"] = settings.Entity,
            ["clm_id"] = settings.ClinicLicenseId,
            ["user_id"] = patient.UserId,
            ["client_id"] = patient.PatientId,
            ["lastname"] = patient.LastName,
            ["firstname"] = patient.FirstName,
            ["email"] = patient.Email,
            ["phone"] = patient.Phone,
            ["insurance_provider"] = patient.InsuranceProvider,
            ["locale"] = patient.Locale,
            ["output_format"] = "json"
        });
        try
        {
            await SendSemanticRequestAsync(connection, HttpMethod.Get, uri, null, "ensure_patient", cancellationToken);
        }
        catch (WibbiAuthenticationException exception) when (!patient.Existing && exception.UpstreamStatusCode == 200)
        {
            var modifyUri = uri.Replace("action=AddClient", "action=ModifyClient", StringComparison.Ordinal);
            await SendSemanticRequestAsync(connection, HttpMethod.Get, modifyUri, null, "update_patient", cancellationToken);
        }
    }

    public async Task EnsureEpisodeAsync(
        IntegrationConnectionContext connection,
        WibbiEpisodeProvisioning episode,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = $"{settings.BaseUrl.TrimEnd('/')}/api/v4/EpisodeOfCare/CreateEpisodeOfCare";
        var body = new
        {
            entity = settings.Entity,
            clm_id = settings.ClinicLicenseId,
            client_id = episode.PatientId,
            case_id = episode.EpisodeId,
            case_name = episode.Name,
            case_start_date = episode.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        await SendSemanticRequestAsync(connection, HttpMethod.Post, uri, body, "ensure_episode", cancellationToken);
    }

    public async Task<IReadOnlyList<WibbiExerciseCatalogItem>> SearchExercisesAsync(
        IntegrationConnectionContext connection,
        string query,
        string locale,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = BuildUri(
            $"{settings.BaseUrl.TrimEnd('/')}/api/v4/emr/exercises/search-exercises",
            new Dictionary<string, string?>
            {
                ["keyword"] = query,
                ["locale"] = NormalizeLocale(locale, hyphenated: true),
                ["entity"] = settings.Entity,
                ["clm_id"] = settings.ClinicLicenseId
            });
        using var document = await SendSemanticRequestAsync(
            connection,
            HttpMethod.Get,
            uri,
            null,
            "search_exercises",
            cancellationToken);
        return FindExerciseItems(document.RootElement)
            .GroupBy(item => item.ExternalExerciseId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(100)
            .ToArray();
    }

    public async Task<WibbiProgramPublishResult> PublishProgramAsync(
        IntegrationConnectionContext connection,
        WibbiProgramPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var body = new
        {
            program_id = request.ProgramId,
            clm_id = settings.ClinicLicenseId,
            case_id = request.EpisodeId,
            user_id = request.UserId,
            entity = settings.Entity,
            client_id = request.PatientId,
            title = request.Title,
            start_date = (request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end_date = (request.EndDate ?? request.StartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            session_amount = 1,
            locale = NormalizeLocale(settings.Locale, hyphenated: false),
            exercises = request.Exercises.Select(exercise => new
            {
                exercise_id = ParseExerciseId(exercise.ExerciseId),
                title = exercise.Title,
                description = exercise.Description,
                sets = exercise.Sets,
                repetition = exercise.Repetitions,
                weight = exercise.Weight,
                frequency = exercise.Frequency,
                duration = exercise.Duration,
                hold = exercise.Hold,
                tempo = exercise.Tempo,
                rest = exercise.Rest,
                level = exercise.Level,
                other = exercise.Other,
                home = exercise.Home,
                mirror = exercise.Mirror,
                flip = exercise.Flip
            }).ToArray()
        };

        var isUpdate = !string.IsNullOrWhiteSpace(request.ExistingProviderProgramId);
        var programUri = isUpdate
            ? $"{settings.BaseUrl.TrimEnd('/')}/api/v4/emr/programs/{Uri.EscapeDataString(request.ExistingProviderProgramId!)}"
            : $"{settings.BaseUrl.TrimEnd('/')}/api/v4/emr/programs";
        using var document = await SendSemanticRequestAsync(
            connection,
            isUpdate ? HttpMethod.Put : HttpMethod.Post,
            programUri,
            body,
            isUpdate ? "update_program" : "create_program",
            cancellationToken);
        // Tracking uses a separate numeric legacy ID. Keep the stable tunnel
        // program_id here because updates and delegated launch links use it.
        var providerProgramId = FindFirstString(document.RootElement, "program_id", "programId")
            ?? request.ExistingProviderProgramId
            ?? request.ProgramId;
        var providerVersion = FindFirstString(document.RootElement, "version", "updated_at", "updatedAt");
        return new WibbiProgramPublishResult(providerProgramId, providerVersion);
    }

    public Task<string> GetClinicianLaunchUrlAsync(
        IntegrationConnectionContext connection,
        string userId,
        string? programId,
        bool flowSheet,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var action = flowSheet ? "AccessFlowsheetLink" : "AccessProgramLink";
        var uri = BuildV3Uri(settings, new Dictionary<string, string?>
        {
            ["action"] = action,
            ["entity"] = settings.Entity,
            ["clm_id"] = settings.ClinicLicenseId,
            ["user_id"] = userId,
            ["program_id"] = programId,
            ["date"] = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["output_format"] = "json"
        });
        return RequestLaunchUrlAsync(connection, uri, settings, flowSheet ? "flowsheet_launch" : "program_launch", cancellationToken);
    }

    public Task<string> GetPatientLaunchUrlAsync(
        IntegrationConnectionContext connection,
        string patientId,
        string? programId,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = BuildUri(
            $"{settings.BaseUrl.TrimEnd('/')}/api/v4/emr/getClientLink",
            new Dictionary<string, string?>
            {
                ["entity"] = settings.Entity,
                ["clm_id"] = settings.ClinicLicenseId,
                ["client_id"] = patientId,
                ["program_id"] = programId
            });
        return RequestLaunchUrlAsync(connection, uri, settings, "patient_launch", cancellationToken);
    }

    public async Task<IReadOnlyList<WibbiTrackingValue>> GetTrackingAsync(
        IntegrationConnectionContext connection,
        string patientId,
        string programId,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var trackingIds = await ResolveLegacyTrackingIdsAsync(
            connection,
            settings,
            patientId,
            programId,
            cancellationToken);
        var uri = $"{settings.BaseUrl.TrimEnd('/')}/api/v4/TrackingProgram/GetPatientProgramData";
        using var document = await SendSemanticRequestAsync(
            connection,
            HttpMethod.Get,
            uri,
            new
            {
                legacyPatientId = ParseLegacyId(trackingIds.PatientId),
                legacyProgramId = ParseLegacyId(trackingIds.ProgramId)
            },
            "tracking",
            cancellationToken);
        return FindTrackingValues(document.RootElement).ToArray();
    }

    private async Task<WibbiLegacyTrackingIds> ResolveLegacyTrackingIdsAsync(
        IntegrationConnectionContext connection,
        WibbiConnectionSettings settings,
        string patientId,
        string programId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"integrations:wibbi:tracking-ids:{connection.Id:N}:{patientId}:{programId}";
        if (_memoryCache.TryGetValue<WibbiLegacyTrackingIds>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var patientUri = BuildV3Uri(settings, new Dictionary<string, string?>
        {
            ["action"] = "GetClient",
            ["entity"] = settings.Entity,
            ["clm_id"] = settings.ClinicLicenseId,
            ["client_id"] = patientId,
            ["output_format"] = "json"
        });
        using var patientDocument = await SendSemanticRequestAsync(
            connection,
            HttpMethod.Get,
            patientUri,
            null,
            "resolve_tracking_patient",
            cancellationToken);
        var legacyPatientId = FindFirstNumericString(
            patientDocument.RootElement,
            "legacyPatientId",
            "idClient",
            "id_client",
            "patientId",
            "patientid",
            "legacyId");
        if (!IsNumericIdentifier(legacyPatientId))
        {
            throw new WibbiAuthenticationException(
                "Wibbi did not return the internal patient identifier required for tracking.",
                "resolve_tracking_patient");
        }

        var programsUri = $"{settings.BaseUrl.TrimEnd('/')}/api/v4/program/getPatientPrograms";
        using var programsDocument = await SendSemanticRequestAsync(
            connection,
            HttpMethod.Get,
            programsUri,
            new { patientid = ParseLegacyId(legacyPatientId!) },
            "resolve_tracking_program",
            cancellationToken);
        var legacyProgramId = FindLegacyProgramId(programsDocument.RootElement, programId);
        if (!IsNumericIdentifier(legacyProgramId))
        {
            throw new WibbiAuthenticationException(
                "Wibbi did not return the internal program identifier required for tracking.",
                "resolve_tracking_program");
        }

        var resolved = new WibbiLegacyTrackingIds(legacyPatientId!, legacyProgramId!);
        _memoryCache.Set(cacheKey, resolved, TimeSpan.FromMinutes(15));
        return resolved;
    }

    public async Task<IReadOnlyList<WibbiProgramChange>> GetChangesAsync(
        IntegrationConnectionContext connection,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var settings = ParseConnection(connection);
        var uri = BuildV3Uri(settings, new Dictionary<string, string?>
        {
            ["action"] = "GetChangesBetween",
            ["entity"] = settings.Entity,
            ["clm_id"] = settings.ClinicLicenseId,
            ["date_from"] = fromUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["date_to"] = toUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["output_format"] = "json"
        });
        using var document = await SendSemanticRequestAsync(connection, HttpMethod.Get, uri, null, "get_changes", cancellationToken);
        return FindProgramChanges(document.RootElement)
            .GroupBy(item => $"{item.ProgramId}:{item.ChangedAtUtc.Ticks}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public Task<HepAssignmentResult> AssignProgramAsync(
        HepAssignmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is disabled.", "assign_program");
        }
        return Task.FromResult(new HepAssignmentResult
        {
            Success = false,
            ErrorMessage = "Legacy clinician assignment is not enabled; use the versioned PTDoc HEP program endpoints."
        });
    }

    public async Task<HepAssignmentResult> GetPatientProgramAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is disabled.", "patient_launch");
        }
        if (!IsLegacyConfigured())
        {
            throw new WibbiConfigurationException("Wibbi HEP integration is not configured.", "patient_launch");
        }

        var mapping = (await _mappingService.GetPatientMappingsAsync(patientId, cancellationToken))
            .FirstOrDefault(m => string.Equals(m.ExternalSystemName, WibbiSystemName, StringComparison.OrdinalIgnoreCase) && m.IsActive);
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

        var connection = BuildLegacyConnection();
        var settings = ParseConnection(connection);
        var url = await GetPatientLaunchUrlAsync(connection, mapping.ExternalId, null, cancellationToken);
        ValidateLaunchUrl(url, settings, patientId, "patient_launch");
        return new HepAssignmentResult
        {
            Success = true,
            AssignmentId = mapping.ExternalId,
            PatientPortalUrl = url
        };
    }

    private async Task<string> RequestLaunchUrlAsync(
        IntegrationConnectionContext connection,
        string uri,
        WibbiConnectionSettings settings,
        string operation,
        CancellationToken cancellationToken)
    {
        using var document = await SendSemanticRequestAsync(connection, HttpMethod.Get, uri, null, operation, cancellationToken);
        var url = FindFirstString(document.RootElement, "URL", "url", "link")
            ?? throw new WibbiAuthenticationException("Wibbi did not return a launch URL.", operation);
        ValidateLaunchUrl(url, settings, null, operation);
        return url;
    }

    private async Task<JsonDocument> SendSemanticRequestAsync(
        IntegrationConnectionContext connection,
        HttpMethod method,
        string uri,
        object? body,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetApiTokenAsync(connection, cancellationToken);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await _httpClientFactory.CreateClient(nameof(WibbiHepService))
                .SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                InvalidateToken(connection.Id);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi request failed.",
                    operation,
                    (int)response.StatusCode,
                    retryAfter: GetRetryAfter(response));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            }
            catch (JsonException exception)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi returned an invalid response.",
                    operation,
                    (int)response.StatusCode,
                    exception);
            }
            var error = FindFirstString(document.RootElement, "error", "error_message", "messageError");
            if (!string.IsNullOrWhiteSpace(error))
            {
                document.Dispose();
                throw new WibbiAuthenticationException("Wibbi rejected the request.", operation, (int)response.StatusCode);
            }
            return document;
        }
        throw new WibbiAuthenticationException("Wibbi request could not be authorized.", operation, 401);
    }

    private async Task<string> GetApiTokenAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetTokenCacheKey(connection.Id);
        if (_memoryCache.TryGetValue<WibbiApiSession>(cacheKey, out var cached) &&
            cached is not null && cached.ExpiresAtUtc > DateTimeOffset.UtcNow.Add(_options.TokenRefreshSkew))
        {
            return cached.Token;
        }

        var tokenLock = TokenLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue<WibbiApiSession>(cacheKey, out cached) &&
                cached is not null && cached.ExpiresAtUtc > DateTimeOffset.UtcNow.Add(_options.TokenRefreshSkew))
            {
                return cached.Token;
            }

            var settings = ParseConnection(connection);
            var credentials = await ResolveCredentialsAsync(connection, cancellationToken);
            using var response = await _httpClientFactory.CreateClient(nameof(WibbiHepService)).PostAsJsonAsync(
                $"{settings.BaseUrl.TrimEnd('/')}/api/v4/authentication/login",
                new { username = credentials.Username, password = credentials.Password },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi API authentication failed.",
                    "authenticate",
                    (int)response.StatusCode,
                    retryAfter: GetRetryAfter(response));
            }

            var upstreamStatusCode = (int)response.StatusCode;
            WibbiLoginResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<WibbiLoginResponse>(cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi returned an invalid authentication response.",
                    "authenticate",
                    upstreamStatusCode,
                    exception);
            }
            if (payload is null)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi returned an invalid authentication response.",
                    "authenticate",
                    upstreamStatusCode);
            }
            if (string.IsNullOrWhiteSpace(payload.Token) || payload.Expires <= DateTimeOffset.UtcNow)
            {
                throw new WibbiAuthenticationException(
                    "Wibbi returned an invalid authentication session.",
                    "authenticate",
                    upstreamStatusCode);
            }

            var session = new WibbiApiSession(payload.Token, payload.Expires);
            _memoryCache.Set(cacheKey, session, session.ExpiresAtUtc);
            return session.Token;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Wibbi authentication transport failed for connection {ConnectionId}.", connection.Id);
            throw new WibbiAuthenticationException(
                "Unable to authenticate with Wibbi.",
                "authenticate",
                (int?)exception.StatusCode,
                exception);
        }
        finally
        {
            tokenLock.Release();
        }
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

    private Task<IntegrationSecretBundle> ResolveCredentialsAsync(
        IntegrationConnectionContext connection,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(connection.SecretReference) && _secretResolver is not null)
        {
            return _secretResolver.ResolveAsync(connection.SecretReference, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_options.ApiUsername) && !string.IsNullOrWhiteSpace(_options.ApiPassword))
        {
            return Task.FromResult(new IntegrationSecretBundle(_options.ApiUsername, _options.ApiPassword));
        }

        throw new WibbiConfigurationException("Wibbi credentials are not configured.", "authenticate");
    }

    private WibbiConnectionSettings ParseConnection(IntegrationConnectionContext connection)
    {
        WibbiConnectionConfiguration? configuration = null;
        if (!string.IsNullOrWhiteSpace(connection.ConfigurationJson))
        {
            try
            {
                configuration = JsonSerializer.Deserialize<WibbiConnectionConfiguration>(
                    connection.ConfigurationJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException)
            {
                throw new WibbiConfigurationException("Wibbi connection configuration is invalid.", "configuration");
            }
        }

        var baseUrl = configuration?.BaseUrl ?? _options.BaseUrl;
        var entity = configuration?.Entity ?? _options.Entity;
        var clinicLicenseId = configuration?.ClinicLicenseId ?? _options.ClinicLicenseId;
        var hosts = configuration?.AllowedLaunchHosts is { Length: > 0 }
            ? configuration.AllowedLaunchHosts
            : _options.AllowedLaunchHosts;
        var locale = configuration?.Locale ?? _options.DefaultLocale;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(entity) || string.IsNullOrWhiteSpace(clinicLicenseId))
        {
            throw new WibbiConfigurationException("Wibbi connection configuration is incomplete.", "configuration");
        }
        var allowedHosts = hosts
            .Append(baseUri.Host)
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new WibbiConnectionSettings(baseUrl, entity, clinicLicenseId, locale, allowedHosts);
    }

    private static string BuildV3Uri(WibbiConnectionSettings settings, IReadOnlyDictionary<string, string?> query) =>
        BuildUri($"{settings.BaseUrl.TrimEnd('/')}/api/v3/", query);

    private static string BuildUri(string baseUri, IReadOnlyDictionary<string, string?> query)
    {
        var values = query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value!)}");
        return $"{baseUri}?{string.Join("&", values)}";
    }

    private void ValidateLaunchUrl(string launchUrl, WibbiConnectionSettings settings, Guid? patientId, string operation)
    {
        if (!Uri.TryCreate(launchUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new WibbiAuthenticationException("Wibbi returned an invalid launch URL.", operation);
        }
        if (!settings.AllowedLaunchHosts.Any(host =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase)))
        {
            throw new WibbiUnsafeLaunchUrlException(
                "Wibbi returned a launch URL on an unapproved host.",
                operation,
                ["host"]);
        }

        var blocked = GetBlockedParameters(uri);
        if (blocked.Count == 0)
        {
            return;
        }
        _logger.LogWarning(
            "Rejected unsafe Wibbi launch URL for entity {EntityId}. Parameters={BlockedParameters}",
            patientId,
            string.Join(", ", blocked));
        throw new WibbiUnsafeLaunchUrlException(
            "Wibbi returned a credential-bearing launch URL, which is not allowed.",
            operation,
            blocked);
    }

    private static IReadOnlyCollection<string> GetBlockedParameters(Uri uri) =>
        string.IsNullOrWhiteSpace(uri.Query)
            ? Array.Empty<string>()
            : uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => Uri.UnescapeDataString(part.Split('=', 2)[0]))
                .Where(name => CredentialQueryParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static IEnumerable<WibbiExerciseCatalogItem> FindExerciseItems(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var id = FindDirectString(element, "exercise_id", "exerciseId", "idExercise", "id");
            var title = FindDirectString(element, "title", "name", "exercise_title");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(title))
            {
                yield return new WibbiExerciseCatalogItem(
                    id,
                    title,
                    FindDirectString(element, "description", "instructions"),
                    FindDirectString(element, "image", "image_url", "imageUrl"),
                    FindDirectString(element, "video", "video_url", "videoUrl"));
            }
            foreach (var property in element.EnumerateObject())
            {
                foreach (var item in FindExerciseItems(property.Value))
                {
                    yield return item;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var item in FindExerciseItems(child))
                {
                    yield return item;
                }
            }
        }
    }

    private static IEnumerable<WibbiTrackingValue> FindTrackingValues(
        JsonElement element,
        string? inheritedDate = null,
        string? inheritedExerciseId = null,
        string? inheritedEntityId = null)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var date = FindDirectString(element, "activityDate", "date", "startDate", "timestamp")
                ?? inheritedDate;
            var exerciseId = FindDirectString(element, "exerciseId", "legacyExerciseId")
                ?? inheritedExerciseId;
            var entityId = FindDirectString(element, "entityId", "programId")
                ?? inheritedEntityId;
            var code = FindDirectString(element, "code", "trackableCode", "type");
            var value = FindDirectString(element, "value", "trackValue");
            if (!string.IsNullOrWhiteSpace(code) && value is not null &&
                DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var activity))
            {
                var id = $"{entityId ?? exerciseId ?? "program"}:{code}:{activity.UtcDateTime.Ticks}";
                yield return new WibbiTrackingValue(
                    id,
                    exerciseId,
                    code,
                    value,
                    FindDirectString(element, "unitOfMeasure", "unit"),
                    activity.UtcDateTime);
            }
            foreach (var property in element.EnumerateObject())
            {
                foreach (var item in FindTrackingValues(property.Value, date, exerciseId, entityId))
                {
                    yield return item;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var item in FindTrackingValues(child, inheritedDate, inheritedExerciseId, inheritedEntityId))
                {
                    yield return item;
                }
            }
        }
    }

    private static IEnumerable<WibbiProgramChange> FindProgramChanges(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var programId = FindDirectString(element, "program_id", "programId", "idProgram");
            var changedText = FindDirectString(element, "updated_at", "updatedAt", "modified_at", "modifiedAt", "date");
            if (!string.IsNullOrWhiteSpace(programId) && DateTimeOffset.TryParse(
                    changedText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var changedAt))
            {
                yield return new WibbiProgramChange(
                    programId,
                    FindDirectString(element, "client_id", "clientId"),
                    changedAt.UtcDateTime,
                    FindDirectString(element, "version", "updated_at", "updatedAt"));
            }
            foreach (var property in element.EnumerateObject())
            {
                foreach (var change in FindProgramChanges(property.Value)) yield return change;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                foreach (var change in FindProgramChanges(child)) yield return change;
            }
        }
    }

    private static string? FindFirstString(JsonElement element, params string[] names)
    {
        var direct = FindDirectString(element, names);
        if (direct is not null)
        {
            return direct;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var nested = FindFirstString(property.Value, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindFirstString(child, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private static string? FindDirectString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                _ => null
            };
        }
        return null;
    }

    private static object ParseExerciseId(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) ? numeric : value;

    private static object ParseLegacyId(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric) ? numeric : value;

    private static bool IsNumericIdentifier(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static string? FindFirstNumericString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => null
                    };
                    if (IsNumericIdentifier(candidate))
                    {
                        return candidate;
                    }
                }

                var nested = FindFirstNumericString(property.Value, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindFirstNumericString(child, names);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private static string? FindLegacyProgramId(JsonElement element, string programId)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var tunnelProgramId = FindDirectString(element, "program_id", "programId", "tunnelProgramId");
            var legacyProgramId = FindDirectString(element, "legacyProgramId", "legacyId", "idProgram");
            if (string.Equals(tunnelProgramId, programId, StringComparison.Ordinal) &&
                IsNumericIdentifier(legacyProgramId))
            {
                return legacyProgramId;
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindLegacyProgramId(property.Value, programId);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var found = FindLegacyProgramId(child, programId);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private static string NormalizeLocale(string locale, bool hyphenated)
    {
        var normalized = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
        return hyphenated ? normalized.Replace('_', '-') : normalized.Replace('-', '_');
    }

    private bool IsLegacyConfigured() =>
        !string.IsNullOrWhiteSpace(_options.ApiUsername) &&
        !string.IsNullOrWhiteSpace(_options.ApiPassword) &&
        !string.IsNullOrWhiteSpace(_options.Entity) &&
        !string.IsNullOrWhiteSpace(_options.ClinicLicenseId);

    private IntegrationConnectionContext BuildLegacyConnection()
    {
        var json = JsonSerializer.Serialize(new WibbiConnectionConfiguration
        {
            BaseUrl = _options.BaseUrl,
            Entity = _options.Entity,
            ClinicLicenseId = _options.ClinicLicenseId,
            Locale = _options.DefaultLocale,
            AllowedLaunchHosts = _options.AllowedLaunchHosts
        });
        return new IntegrationConnectionContext(Guid.Empty, Guid.Empty, WibbiSystemName, json, string.Empty);
    }

    private static string GetTokenCacheKey(Guid connectionId) => $"integrations:wibbi:api-token:{connectionId:N}";
    private void InvalidateToken(Guid connectionId) => _memoryCache.Remove(GetTokenCacheKey(connectionId));

    private sealed class WibbiLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public DateTimeOffset Expires { get; set; }
    }

    private sealed record WibbiApiSession(string Token, DateTimeOffset ExpiresAtUtc);

    private sealed record WibbiLegacyTrackingIds(string PatientId, string ProgramId);

    private sealed class WibbiConnectionConfiguration
    {
        public string? BaseUrl { get; set; }
        public string? Entity { get; set; }
        public string? ClinicLicenseId { get; set; }
        public string? Locale { get; set; }
        public string[]? AllowedLaunchHosts { get; set; }
    }

    private sealed record WibbiConnectionSettings(
        string BaseUrl,
        string Entity,
        string ClinicLicenseId,
        string Locale,
        string[] AllowedLaunchHosts);
}
