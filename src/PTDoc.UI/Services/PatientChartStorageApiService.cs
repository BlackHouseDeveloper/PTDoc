using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using PTDoc.Application.DTOs;

namespace PTDoc.UI.Services;

public sealed class PatientChartStorageApiService(HttpClient httpClient) : IPatientChartStorageService
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<PatientDocumentResponse>> ListDocumentsAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{patientId:D}/documents", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PatientDocumentResponse>>(SerializerOptions, cancellationToken)
            ?? new List<PatientDocumentResponse>();
    }

    public async Task<PatientDocumentResponse> UploadDocumentAsync(
        Guid patientId,
        IBrowserFile file,
        string documentType,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream(MaxUploadBytes, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var buffer = memory.GetBuffer();
        var length = checked((int)memory.Length);

        var request = new UploadPatientDocumentRequest
        {
            DocumentType = documentType,
            FileName = file.Name,
            ContentType = file.ContentType,
            Base64Content = Convert.ToBase64String(buffer, 0, length),
            Notes = notes
        };

        var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/patients/{patientId:D}/documents",
            request,
            SerializerOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PatientDocumentResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Patient document upload response was empty.");
    }

    public async Task<IReadOnlyList<PatientCommunicationLogEntryResponse>> ListCommunicationLogEntriesAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/patients/{patientId:D}/communications", cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<PatientCommunicationLogEntryResponse>>(SerializerOptions, cancellationToken)
            ?? new List<PatientCommunicationLogEntryResponse>();
    }

    public async Task<PatientCommunicationLogEntryResponse> CreateCommunicationLogEntryAsync(
        Guid patientId,
        CreatePatientCommunicationLogEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/v1/patients/{patientId:D}/communications",
            request,
            SerializerOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PatientCommunicationLogEntryResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Patient communication log response was empty.");
    }
}
