using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-client implementation of <see cref="IPatientService"/> for Blazor UI.
/// Delegates to the PTDoc REST API via the named "ServerAPI" HttpClient.
/// </summary>
public sealed class PatientApiService(HttpClient httpClient) : IPatientService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PatientResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{id}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PatientResponse>(SerializerOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientListItemResponse>> SearchAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? $"/api/v1/patients/?take={take}"
            : $"/api/v1/patients/?query={Uri.EscapeDataString(query.Trim())}&take={take}";

        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PatientListItemResponse>>(SerializerOptions, cancellationToken)
            ?? new List<PatientListItemResponse>();
    }

    public async Task<PatientResponse> CreateAsync(
        CreatePatientRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/patients/", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PatientResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Patient creation response was empty.");
    }

    public async Task<PatientResponse?> UpdateAsync(
        Guid id,
        UpdatePatientRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/v1/patients/{id}", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PatientResponse>(SerializerOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientDiagnosisDto>?> GetDiagnosesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{patientId}/diagnoses", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<PatientDiagnosisDto>>(SerializerOptions, cancellationToken)
            ?? new List<PatientDiagnosisDto>();
    }

    public async Task<bool> AddDiagnosisAsync(
        Guid patientId,
        string icdCode,
        string description,
        bool isPrimary,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/patients/{patientId}/diagnoses",
            new { icdCode, description, isPrimary },
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<bool> RemoveDiagnosisAsync(
        Guid patientId,
        string icdCode,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/api/v1/patients/{patientId}/diagnoses/{Uri.EscapeDataString(icdCode)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();
        return true;
    }
}
