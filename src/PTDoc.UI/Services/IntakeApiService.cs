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

        response.EnsureSuccessStatusCode();

        var intake = await response.Content.ReadFromJsonAsync<IntakeResponse>(SerializerOptions, cancellationToken);
        if (intake is null)
        {
            return null;
        }

        return ToDraft(intake);
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
        patientResponse.EnsureSuccessStatusCode();

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
            await CreateIntakeAsync(state.PatientId.Value, state, cancellationToken);
            return;
        }

        if (existing.Locked)
        {
            return;
        }

        var updateRequest = new UpdateIntakeRequest
        {
            PainMapData = BuildPainMapJson(state),
            Consents = BuildConsentJson(state),
            ResponseJson = JsonSerializer.Serialize(state, SerializerOptions),
            TemplateVersion = "1.0"
        };

        var response = await SendWithOptionalStandaloneAccessAsync(
            HttpMethod.Put,
            authenticatedPath: $"/api/v1/intake/{existing.Id}",
            standalonePath: $"/api/v1/intake/access/{existing.Id}",
            body: updateRequest,
            cancellationToken);
        response.EnsureSuccessStatusCode();
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
        response.EnsureSuccessStatusCode();
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

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntakeResponse>(SerializerOptions, cancellationToken);
    }

    private async Task CreateIntakeAsync(Guid patientId, IntakeResponseDraft state, CancellationToken cancellationToken)
    {
        var createRequest = new CreateIntakeRequest
        {
            PatientId = patientId,
            PainMapData = BuildPainMapJson(state),
            Consents = BuildConsentJson(state),
            ResponseJson = JsonSerializer.Serialize(state, SerializerOptions),
            TemplateVersion = "1.0"
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/intake/", createRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
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
        draft.IsSubmitted = response.SubmittedAt.HasValue;
        draft.IsLocked = response.Locked;
        return draft;
    }

    private static string BuildPainMapJson(IntakeResponseDraft state)
    {
        var payload = new
        {
            selectedBodyRegion = state.SelectedBodyRegion,
            selectedRegions = state.PainDetailDrafts.Keys.ToArray()
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static string BuildConsentJson(IntakeResponseDraft state)
    {
        var payload = new
        {
            hipaaAcknowledged = state.HipaaAcknowledged && !state.RevokeHipaaPrivacyNotice,
            termsOfServiceAccepted = state.TermsOfServiceAccepted,
            accuracyConfirmed = state.AccuracyConfirmed,
            treatmentConsentAccepted = !state.RevokeTreatmentConsent,
            phiReleaseAuthorized = state.PhiReleaseAuthorized && !state.RevokePhiRelease,
            billingConsentAuthorized = state.BillingConsentAuthorized,
            communicationPreferences = new
            {
                phoneCalls = state.AllowPhoneCalls,
                textMessages = state.AllowTextMessages,
                emailMessages = state.AllowEmailMessages
            },
            revocations = new
            {
                hipaaPrivacyNotice = state.RevokeHipaaPrivacyNotice,
                treatmentConsent = state.RevokeTreatmentConsent,
                marketingCommunications = state.RevokeMarketingCommunications,
                phiRelease = state.RevokePhiRelease
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
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
