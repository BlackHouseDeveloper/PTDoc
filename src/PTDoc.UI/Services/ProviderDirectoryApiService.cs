using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Providers;
using PTDoc.Core.Models;

namespace PTDoc.UI.Services;

public sealed class ProviderDirectoryApiService(HttpClient httpClient) : IProviderDirectoryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken ct) { var url = $"/api/v1/providers?take={Math.Clamp(take, 1, 100)}" + (string.IsNullOrWhiteSpace(query) ? "" : $"&q={Uri.EscapeDataString(query)}") + (status.HasValue ? $"&status={status.Value}" : ""); return await httpClient.GetFromJsonAsync<List<ProviderDirectoryEntryDto>>(url, Json, ct) ?? []; }
    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchForAdministrationAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken ct) { var url = $"/api/v1/admin/providers?take={Math.Clamp(take, 1, 100)}" + (string.IsNullOrWhiteSpace(query) ? "" : $"&q={Uri.EscapeDataString(query)}") + (status.HasValue ? $"&status={status.Value}" : ""); return await httpClient.GetFromJsonAsync<List<ProviderDirectoryEntryDto>>(url, Json, ct) ?? []; }
    public async Task<ProviderDirectoryEntryDto?> GetAsync(Guid id, CancellationToken ct) => await httpClient.GetFromJsonAsync<ProviderDirectoryEntryDto>($"/api/v1/providers/{id}", Json, ct);
    public Task<ProviderDirectoryEntryDto> SubmitAsync(SubmitProviderCandidateRequest request, CancellationToken ct) => Send<ProviderDirectoryEntryDto>(HttpMethod.Post, "/api/v1/providers/candidates", request, ct);
    public Task<ProviderDirectoryEntryDto> UpdateAsync(Guid id, UpdateProviderCandidateRequest request, CancellationToken ct) => Send<ProviderDirectoryEntryDto>(HttpMethod.Put, $"/api/v1/providers/candidates/{id}", request, ct);
    public Task<ProviderDirectoryEntryDto> ApproveAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct) => Send<ProviderDirectoryEntryDto>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/approve", request, ct);
    public Task<ProviderDirectoryEntryDto> RejectAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct) => Send<ProviderDirectoryEntryDto>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/reject", request, ct);
    public async Task ArchiveAsync(Guid id, ProviderDecisionRequest request, CancellationToken ct) => _ = await Send<bool>(HttpMethod.Post, $"/api/v1/admin/providers/{id}/archive", request, ct);
    public async Task<IReadOnlyList<PatientProviderRelationshipDto>> ListPatientRelationshipsAsync(Guid patientId, CancellationToken ct) => await httpClient.GetFromJsonAsync<List<PatientProviderRelationshipDto>>($"/api/v1/providers/patients/{patientId}", Json, ct) ?? [];
    public Task<PatientProviderRelationshipDto> UpsertPatientRelationshipAsync(Guid patientId, Guid? relationshipId, UpsertPatientProviderRelationshipRequest request, CancellationToken ct) => Send<PatientProviderRelationshipDto>(relationshipId.HasValue ? HttpMethod.Put : HttpMethod.Post, relationshipId.HasValue ? $"/api/v1/providers/patients/{patientId}/{relationshipId}" : $"/api/v1/providers/patients/{patientId}", request, ct);
    public async Task ArchivePatientRelationshipAsync(Guid patientId, Guid relationshipId, CancellationToken ct) { using var response = await httpClient.DeleteAsync($"/api/v1/providers/patients/{patientId}/{relationshipId}", ct); await Ensure(response, ct); }
    private async Task<T> Send<T>(HttpMethod method, string url, object body, CancellationToken ct) { using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, options: Json) }; using var response = await httpClient.SendAsync(request, ct); await Ensure(response, ct); return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidOperationException("The provider service returned an empty response."); }
    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct) { if (response.IsSuccessStatusCode) return; throw new HttpRequestException(await ApiErrorReader.ReadMessageAsync(response, ct) ?? "Provider directory request failed.", null, response.StatusCode); }
}
