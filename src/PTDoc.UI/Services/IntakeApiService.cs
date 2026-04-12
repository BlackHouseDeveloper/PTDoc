using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

public sealed class IntakeApiService(
    HttpClient httpClient,
    IIntakeSessionStore sessionStore,
    NavigationManager navigationManager) : IIntakeService
{
    private const string EligiblePatientsEndpoint = "/api/v1/intake/patients/eligible";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var response = await SendWithOptionalStandaloneAccessAsync(
            HttpMethod.Get,
            authenticatedPath: $"/api/v1/intake/patient/{patientId}/draft",
            standalonePath: $"/api/v1/intake/access/patient/{patientId}/draft",
            body: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }

        var intake = await response.Content.ReadFromJsonAsync<IntakeResponse>(SerializerOptions, cancellationToken);
        if (intake is null)
        {
            return null;
        }

        return ToDraft(intake);
    }

    public async Task<IntakeEnsureDraftResult> EnsureDraftAsync(
        Guid patientId,
        IntakeResponseDraft? seedState = null,
        CancellationToken cancellationToken = default)
    {
        var request = new EnsureIntakeDraftRequest
        {
            PainMapData = BuildPainMapJson(seedState),
            Consents = BuildConsentJson(seedState),
            ConsentPacket = BuildConsentPacket(seedState),
            ResponseJson = seedState is null
                ? "{}"
                : JsonSerializer.Serialize(seedState, SerializerOptions),
            StructuredData = seedState?.StructuredData,
            TemplateVersion = "1.0"
        };

        using var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/intake/drafts/{patientId}",
            request,
            SerializerOptions,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var message = await ReadErrorAsync(response, cancellationToken) ?? $"Patient {patientId} was not found.";
            return IntakeEnsureDraftResult.NotFound(message);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var message = await ReadErrorAsync(response, cancellationToken) ?? "Intake is locked for this patient and a new draft cannot be created.";
            return IntakeEnsureDraftResult.Locked(message);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }

        var intake = await response.Content.ReadFromJsonAsync<IntakeResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Ensure draft completed but the intake payload was empty.");

        return response.StatusCode == HttpStatusCode.Created
            ? IntakeEnsureDraftResult.Created(ToDraft(intake))
            : IntakeEnsureDraftResult.Existing(ToDraft(intake));
    }

    public async Task<IReadOnlyList<PatientListItemResponse>> SearchEligiblePatientsAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = take <= 0 ? 100 : take;
        var path = string.IsNullOrWhiteSpace(query)
            ? $"{EligiblePatientsEndpoint}?take={normalizedTake}"
            : $"{EligiblePatientsEndpoint}?query={Uri.EscapeDataString(query.Trim())}&take={normalizedTake}";

        var patients = await httpClient.GetFromJsonAsync<List<PatientListItemResponse>>(path, SerializerOptions, cancellationToken);
        return patients ?? [];
    }

    public async Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        var (firstName, lastName) = SplitName(state.FullName);

        if (state.DateOfBirth is null)
        {
            throw new ArgumentException("Date of birth is required to create a temporary patient.", nameof(state));
        }

        var createPatientRequest = new CreatePatientRequest
        {
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = state.DateOfBirth.Value,
            Email = state.EmailAddress,
            Phone = state.PhoneNumber,
            AddressLine1 = state.AddressLine1,
            AddressLine2 = state.AddressLine2,
            City = state.City,
            State = state.StateOrProvince,
            ZipCode = state.PostalCode,
            PayerInfoJson = "{}"
        };

        var patientResponse = await httpClient.PostAsJsonAsync("/api/v1/patients/", createPatientRequest, cancellationToken);
        if (!patientResponse.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(patientResponse, cancellationToken);
        }

        var patient = await patientResponse.Content.ReadFromJsonAsync<PatientResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Patient creation response payload was empty.");

        await CreateIntakeAsync(patient.Id, state, cancellationToken);
        return patient.Id;
    }

    public async Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (!state.PatientId.HasValue)
        {
            return;
        }

        var existing = await GetIntakeByPatientAsync(state.PatientId.Value, cancellationToken);
        if (existing is null)
        {
            if (await TryGetStandaloneAccessTokenAsync(cancellationToken) is not null)
            {
                throw new HttpRequestException(
                    "The intake form is not available.",
                    inner: null,
                    statusCode: HttpStatusCode.NotFound);
            }

            var ensured = await EnsureDraftAsync(state.PatientId.Value, state, cancellationToken);
            if (ensured.Status is IntakeEnsureDraftStatus.Locked or IntakeEnsureDraftStatus.PatientNotFound)
            {
                throw new HttpRequestException(
                    ensured.ErrorMessage ?? "Unable to ensure an intake draft for this patient.",
                    inner: null,
                    statusCode: ensured.Status == IntakeEnsureDraftStatus.Locked ? HttpStatusCode.Conflict : HttpStatusCode.NotFound);
            }

            if (ensured.Status == IntakeEnsureDraftStatus.Created)
            {
                return;
            }

            existing = await GetIntakeByPatientAsync(state.PatientId.Value, cancellationToken);
            if (existing is null)
            {
                throw new HttpRequestException(
                    "The intake form is not available.",
                    inner: null,
                    statusCode: HttpStatusCode.NotFound);
            }
        }

        if (existing.Locked)
        {
            return;
        }

        var updateRequest = new UpdateIntakeRequest
        {
            PainMapData = BuildPainMapJson(state),
            Consents = BuildConsentJson(state),
            ConsentPacket = BuildConsentPacket(state),
            ResponseJson = JsonSerializer.Serialize(state, SerializerOptions),
            StructuredData = state.StructuredData,
            TemplateVersion = "1.0"
        };

        var response = await SendWithOptionalStandaloneAccessAsync(
            HttpMethod.Put,
            authenticatedPath: $"/api/v1/intake/{existing.Id}",
            standalonePath: $"/api/v1/intake/access/{existing.Id}",
            body: updateRequest,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }
    }

    public async Task SubmitAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (!state.PatientId.HasValue)
        {
            throw new InvalidOperationException("Cannot submit intake because PatientId is missing.");
        }

        await SaveDraftAsync(state, cancellationToken);

        var existing = await GetIntakeByPatientAsync(state.PatientId.Value, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"Cannot submit intake; no draft intake found for patient {state.PatientId.Value}.");
        }

        if (existing.Locked)
        {
            throw new InvalidOperationException($"Cannot submit intake; intake {existing.Id} is already locked.");
        }

        var response = await SendWithOptionalStandaloneAccessAsync(
            HttpMethod.Post,
            authenticatedPath: $"/api/v1/intake/{existing.Id}/submit",
            standalonePath: $"/api/v1/intake/access/{existing.Id}/submit",
            body: null,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }
    }

    private async Task<IntakeResponse?> GetIntakeByPatientAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var response = await SendWithOptionalStandaloneAccessAsync(
            HttpMethod.Get,
            authenticatedPath: $"/api/v1/intake/patient/{patientId}/draft",
            standalonePath: $"/api/v1/intake/access/patient/{patientId}/draft",
            body: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadFromJsonAsync<IntakeResponse>(SerializerOptions, cancellationToken);
    }

    private async Task CreateIntakeAsync(Guid patientId, IntakeResponseDraft state, CancellationToken cancellationToken)
    {
        var createRequest = new CreateIntakeRequest
        {
            PatientId = patientId,
            PainMapData = BuildPainMapJson(state),
            Consents = BuildConsentJson(state),
            ConsentPacket = BuildConsentPacket(state),
            ResponseJson = JsonSerializer.Serialize(state, SerializerOptions),
            StructuredData = state.StructuredData,
            TemplateVersion = "1.0"
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/intake/", createRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateHttpRequestExceptionAsync(response, cancellationToken);
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await ApiErrorReader.ReadMessageAsync(response, cancellationToken);
    }

    private static async Task<HttpRequestException> CreateHttpRequestExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return new HttpRequestException(
            await ReadErrorAsync(response, cancellationToken) ?? "The intake request failed.",
            inner: null,
            response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWithOptionalStandaloneAccessAsync(
        HttpMethod method,
        string authenticatedPath,
        string standalonePath,
        object? body,
        CancellationToken cancellationToken)
    {
        var accessToken = await TryGetStandaloneAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            if (body is null)
            {
                using var authenticatedRequest = new HttpRequestMessage(method, authenticatedPath);
                return await httpClient.SendAsync(authenticatedRequest, cancellationToken);
            }

            return await SendJsonAsync(method, authenticatedPath, body, cancellationToken);
        }

        using var standaloneRequest = new HttpRequestMessage(method, standalonePath);
        standaloneRequest.Headers.Add(IntakeAccessHeaders.AccessToken, accessToken);
        if (body is not null)
        {
            standaloneRequest.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        return await httpClient.SendAsync(standaloneRequest, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };

        return await httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string?> TryGetStandaloneAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsStandalonePatientMode())
        {
            return null;
        }

        var session = await sessionStore.GetAsync(cancellationToken);
        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return session.Token;
    }

    private bool IsStandalonePatientMode()
    {
        if (!Uri.TryCreate(navigationManager.Uri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.AbsolutePath.StartsWith("/intake", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return query.Any(part => string.Equals(part, "mode=patient", StringComparison.OrdinalIgnoreCase));
    }

    private static IntakeResponseDraft ToDraft(IntakeResponse response)
    {
        IntakeResponseDraft draft;

        if (string.IsNullOrWhiteSpace(response.ResponseJson))
        {
            draft = new IntakeResponseDraft();
        }
        else
        {
            try
            {
                draft = JsonSerializer.Deserialize<IntakeResponseDraft>(response.ResponseJson, SerializerOptions) ?? new IntakeResponseDraft();
            }
            catch (JsonException)
            {
                draft = new IntakeResponseDraft();
            }
        }

        draft.PatientId = response.PatientId;
        draft.IntakeId = response.Id;
        draft.ConsentPacket = response.ConsentPacket ?? draft.ConsentPacket;
        if (draft.ConsentPacket is not null)
        {
            draft.ConsentPacket.RevokedConsentKeys ??= new List<string>();
            draft.ConsentPacket.AuthorizedContacts ??= new List<AuthorizedContact>();
            draft.HipaaAcknowledged = draft.ConsentPacket.HipaaAcknowledged == true;
            draft.ConsentToTreatAcknowledged = draft.ConsentPacket.TreatmentConsentAccepted == true;
            draft.RevokeHipaaPrivacyNotice = draft.ConsentPacket.RevokedConsentKeys.Contains("hipaaAcknowledged", StringComparer.OrdinalIgnoreCase);
            draft.RevokeTreatmentConsent = draft.ConsentPacket.RevokedConsentKeys.Contains("treatmentConsentAccepted", StringComparer.OrdinalIgnoreCase);
            draft.RevokePhiRelease = draft.ConsentPacket.RevokedConsentKeys.Contains("phiReleaseAuthorized", StringComparer.OrdinalIgnoreCase);
            draft.RevokeMarketingCommunications =
                draft.ConsentPacket.RevokedConsentKeys.Contains("communicationCallConsent", StringComparer.OrdinalIgnoreCase)
                && draft.ConsentPacket.RevokedConsentKeys.Contains("communicationTextConsent", StringComparer.OrdinalIgnoreCase)
                && draft.ConsentPacket.RevokedConsentKeys.Contains("communicationEmailConsent", StringComparer.OrdinalIgnoreCase);
            draft.AllowPhoneCalls = draft.ConsentPacket.CommunicationCallConsent ?? draft.AllowPhoneCalls;
            draft.AllowTextMessages = draft.ConsentPacket.CommunicationTextConsent ?? draft.AllowTextMessages;
            draft.AllowEmailMessages = draft.ConsentPacket.CommunicationEmailConsent ?? draft.AllowEmailMessages;
            draft.DryNeedlingEligible = draft.ConsentPacket.DryNeedlingConsentAccepted ?? draft.DryNeedlingEligible;
            draft.PelvicFloorTherapyEligible = draft.ConsentPacket.PelvicFloorConsentAccepted ?? draft.PelvicFloorTherapyEligible;
            draft.PhiReleaseAuthorized = draft.ConsentPacket.PhiReleaseAuthorized ?? draft.PhiReleaseAuthorized;
            draft.BillingConsentAuthorized = draft.ConsentPacket.CreditCardAuthorizationAccepted ?? draft.BillingConsentAuthorized;
            draft.AccuracyConfirmed = draft.ConsentPacket.FinalAttestationAccepted ?? draft.AccuracyConfirmed;
        }

        draft.StructuredData = response.StructuredData ?? draft.StructuredData;
        draft.IsSubmitted = response.SubmittedAt.HasValue;
        draft.IsLocked = response.Locked;
        return draft;
    }

    private static string BuildPainMapJson(IntakeResponseDraft? state)
    {
        if (state is null)
        {
            return "{}";
        }

        var payload = new
        {
            selectedBodyRegion = state.SelectedBodyRegion,
            selectedRegions = state.PainDetailDrafts.Keys.ToArray()
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static string BuildConsentJson(IntakeResponseDraft? state)
    {
        return state is null
            ? "{}"
            : IntakeConsentJson.Serialize(BuildConsentPacket(state));
    }

    private static IntakeConsentPacket BuildConsentPacket(IntakeResponseDraft? state)
    {
        if (state is null)
        {
            return new IntakeConsentPacket();
        }

        var packet = CloneConsentPacket(state.ConsentPacket);
        packet.HipaaAcknowledged ??= state.HipaaAcknowledged;
        packet.TreatmentConsentAccepted ??= state.ConsentToTreatAcknowledged;
        packet.PhiReleaseAuthorized ??= state.PhiReleaseAuthorized;
        packet.CommunicationCallConsent ??= state.AllowPhoneCalls;
        packet.CommunicationTextConsent ??= state.AllowTextMessages;
        packet.CommunicationEmailConsent ??= state.AllowEmailMessages;
        packet.DryNeedlingConsentAccepted ??= state.DryNeedlingEligible;
        packet.PelvicFloorConsentAccepted ??= state.PelvicFloorTherapyEligible;
        packet.CreditCardAuthorizationAccepted ??= state.BillingConsentAuthorized;
        packet.FinalAttestationAccepted ??= state.AccuracyConfirmed;

        if (!string.IsNullOrWhiteSpace(state.PhoneNumber))
        {
            packet.CommunicationPhoneNumber = state.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(state.EmailAddress))
        {
            packet.CommunicationEmail = state.EmailAddress;
        }

        SetRevoked(packet, "hipaaAcknowledged", state.RevokeHipaaPrivacyNotice);
        SetRevoked(packet, "treatmentConsentAccepted", state.RevokeTreatmentConsent);
        SetRevoked(packet, "phiReleaseAuthorized", state.RevokePhiRelease);
        SetRevoked(packet, "communicationCallConsent", state.RevokeMarketingCommunications);
        SetRevoked(packet, "communicationTextConsent", state.RevokeMarketingCommunications);
        SetRevoked(packet, "communicationEmailConsent", state.RevokeMarketingCommunications);

        return packet;
    }

    private static IntakeConsentPacket CloneConsentPacket(IntakeConsentPacket? packet)
    {
        if (packet is null)
        {
            return new IntakeConsentPacket();
        }

        packet.AuthorizedContacts ??= new List<AuthorizedContact>();
        packet.RevokedConsentKeys ??= new List<string>();
        var json = IntakeConsentJson.Serialize(packet);
        return IntakeConsentJson.TryParse(json, out var clone, out _)
            ? clone
            : new IntakeConsentPacket();
    }

    private static void SetRevoked(IntakeConsentPacket packet, string key, bool revoked)
    {
        if (revoked)
        {
            if (!packet.RevokedConsentKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                packet.RevokedConsentKeys.Add(key);
            }

            return;
        }

        packet.RevokedConsentKeys.RemoveAll(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase));
    }

    private static (string FirstName, string LastName) SplitName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return ("Intake", "Patient");
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            return (parts[0], "Patient");
        }

        var firstName = parts[0];
        var lastName = string.Join(' ', parts.Skip(1));
        return (firstName, lastName);
    }
}
