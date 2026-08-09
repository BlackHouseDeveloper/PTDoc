using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Providers;
using PTDoc.Core.Models;

namespace PTDoc.UI.Services;

public sealed class ProviderDirectoryApiService(HttpClient httpClient) : IProviderDirectoryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchAsync(
        string? query,
        ProviderDirectoryStatus? status,
        int take,
        CancellationToken ct)
    {
        var url = BuildSearchUrl("/api/v1/providers", query, status, take);
        return await httpClient.GetFromJsonAsync<List<ProviderDirectoryEntryDto>>(url, Json, ct) ?? [];
    }

    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchForAdministrationAsync(
        string? query,
        ProviderDirectoryStatus? status,
        int take,
        CancellationToken ct)
    {
        var url = BuildSearchUrl("/api/v1/admin/providers", query, status, take);
        return await httpClient.GetFromJsonAsync<List<ProviderDirectoryEntryDto>>(url, Json, ct) ?? [];
    }

    public Task<ProviderDirectoryEntryDto?> GetAsync(Guid id, CancellationToken ct) =>
        httpClient.GetFromJsonAsync<ProviderDirectoryEntryDto>($"/api/v1/providers/{id}", Json, ct);

    public Task<ProviderDirectoryEntryDto> SubmitAsync(SubmitProviderCandidateRequest request, CancellationToken ct) =>
        Send<ProviderDirectoryEntryDto>(HttpMethod.Post, "/api/v1/providers/candidates", request, ct);

    public Task<ProviderDirectoryEntryDto> UpdateAsync(Guid id, UpdateProviderCandidateRequest request, CancellationToken ct) =>
        Send<ProviderDirectoryEntryDto>(HttpMethod.Put, $"/api/v1/providers/candidates/{id}", request, ct);

    public Task<ProviderDirectoryEntryDto> ApproveAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct) =>
        Send<ProviderDirectoryEntryDto>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/approve", request, ct);

    public Task<ProviderDirectoryEntryDto> RejectAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct) =>
        Send<ProviderDirectoryEntryDto>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/reject", request, ct);

    public async Task ArchiveAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct)
    {
        _ = await Send<bool>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/archive", request, ct);
    }

    public async Task<IReadOnlyList<PatientProviderRelationshipDto>> ListPatientRelationshipsAsync(
        Guid patientId,
        CancellationToken ct)
    {
        return await httpClient.GetFromJsonAsync<List<PatientProviderRelationshipDto>>(
            $"/api/v1/providers/patients/{patientId}",
            Json,
            ct) ?? [];
    }

    public Task<PatientProviderRelationshipDto> UpsertPatientRelationshipAsync(
        Guid patientId,
        Guid? relationshipId,
        UpsertPatientProviderRelationshipRequest request,
        CancellationToken ct)
    {
        var method = relationshipId.HasValue ? HttpMethod.Put : HttpMethod.Post;
        var url = relationshipId.HasValue
            ? $"/api/v1/providers/patients/{patientId}/{relationshipId}"
            : $"/api/v1/providers/patients/{patientId}";

        return Send<PatientProviderRelationshipDto>(method, url, request, ct);
    }

    public async Task ArchivePatientRelationshipAsync(Guid patientId, Guid relationshipId, CancellationToken ct)
    {
        using var response = await httpClient.DeleteAsync($"/api/v1/providers/patients/{patientId}/{relationshipId}", ct);
        await Ensure(response, ct);
    }

    private static string BuildSearchUrl(
        string path,
        string? query,
        ProviderDirectoryStatus? status,
        int take)
    {
        var url = $"{path}?take={Math.Clamp(take, 1, 100)}";

        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&q={Uri.EscapeDataString(query)}";
        }

        if (status.HasValue)
        {
            url += $"&status={status.Value}";
        }

        return url;
    }

    private async Task<T> Send<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        using var response = await httpClient.SendAsync(request, ct);

        await Ensure(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException("The provider service returned an empty response.");
    }

    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            await ApiErrorReader.ReadMessageAsync(response, ct) ?? "Provider directory request failed.",
            null,
            response.StatusCode);
    }
}
