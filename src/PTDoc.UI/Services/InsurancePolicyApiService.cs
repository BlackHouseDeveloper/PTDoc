using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Insurance;
using PTDoc.Core.Models;

namespace PTDoc.UI.Services;

public sealed class InsurancePolicyApiService(HttpClient httpClient) : IInsurancePolicyService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<InsurancePolicyDto>> ListAsync(Guid patientId, CancellationToken ct) =>
        ListAsync(patientId, includeArchived: false, ct);

    public async Task<IReadOnlyList<InsurancePolicyDto>> ListAsync(
        Guid patientId,
        bool includeArchived,
        CancellationToken ct)
    {
        var url = $"/api/v1/patients/{patientId}/insurance-policies";
        if (includeArchived)
        {
            url += "?includeArchived=true";
        }

        return await httpClient.GetFromJsonAsync<List<InsurancePolicyDto>>(url, Json, ct) ?? [];
    }

    public Task<InsurancePolicyDto> UpsertPolicyAsync(
        Guid patientId,
        Guid? policyId,
        UpsertInsurancePolicyRequest request,
        CancellationToken ct) =>
        Send<InsurancePolicyDto>(
            policyId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            policyId.HasValue
                ? $"/api/v1/patients/{patientId}/insurance-policies/{policyId}"
                : $"/api/v1/patients/{patientId}/insurance-policies",
            request,
            ct);

    public async Task ArchivePolicyAsync(Guid patientId, Guid policyId, CancellationToken ct)
    {
        using var response = await httpClient.DeleteAsync($"/api/v1/patients/{patientId}/insurance-policies/{policyId}", ct);
        await Ensure(response, ct);
    }

    public async Task ReorderAsync(
        Guid patientId,
        IReadOnlyDictionary<Guid, InsuranceCoveragePriority> priorities,
        CancellationToken ct) =>
        _ = await Send<bool>(HttpMethod.Put, $"/api/v1/patients/{patientId}/insurance-policies/priority", priorities, ct);

    public Task<InsuranceAuthorizationDto> UpsertAuthorizationAsync(
        Guid patientId,
        Guid policyId,
        Guid? authorizationId,
        UpsertInsuranceAuthorizationRequest request,
        CancellationToken ct) =>
        Send<InsuranceAuthorizationDto>(
            authorizationId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            authorizationId.HasValue
                ? $"/api/v1/patients/{patientId}/insurance-policies/{policyId}/authorizations/{authorizationId}"
                : $"/api/v1/patients/{patientId}/insurance-policies/{policyId}/authorizations",
            request,
            ct);

    public async Task ArchiveAuthorizationAsync(Guid patientId, Guid policyId, Guid authorizationId, CancellationToken ct)
    {
        using var response = await httpClient.DeleteAsync(
            $"/api/v1/patients/{patientId}/insurance-policies/{policyId}/authorizations/{authorizationId}",
            ct);
        await Ensure(response, ct);
    }

    public Task<PayerBackfillReport> BackfillLegacyPayerDataAsync(CancellationToken ct) =>
        Send<PayerBackfillReport>(HttpMethod.Post, "/api/v1/admin/insurance-policies/backfill", new { }, ct);

    private async Task<T> Send<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body, options: Json)
        };
        using var response = await httpClient.SendAsync(request, ct);
        await Ensure(response, ct);

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException("The insurance service returned an empty response.");
    }

    private static async Task Ensure(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new HttpRequestException(
            await ApiErrorReader.ReadMessageAsync(response, ct) ?? "Insurance request failed.",
            null,
            response.StatusCode);
    }
}
