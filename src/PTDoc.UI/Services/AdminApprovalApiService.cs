using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Identity;

namespace PTDoc.UI.Services;

public interface IAdminApprovalService
{
    Task<AdminApprovalPage> GetPendingAsync(AdminApprovalQuery query, CancellationToken cancellationToken = default);

    Task<PendingUserDetail?> GetPendingDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalUpdateResult> UpdateAsync(Guid userId, AdminRegistrationUpdateRequest request, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> ApproveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> RejectAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> HoldAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> CancelAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record AdminApprovalActionResult(
    bool Succeeded,
    string? Status,
    string? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record AdminApprovalUpdateResult(
    bool Succeeded,
    PendingUserDetail? Detail,
    string? Status,
    string? Error,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record AdminApprovalPage(
    IReadOnlyList<PendingUserSummary> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record AdminApprovalQuery(
    string? Search,
    string? Status,
    string? Role,
    string? Clinic,
    DateTime? FromDate,
    DateTime? ToDate,
    string? SortBy,
    int Page,
    int PageSize);

public sealed class AdminApprovalApiService(HttpClient httpClient) : IAdminApprovalService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AdminApprovalPage> GetPendingAsync(AdminApprovalQuery query, CancellationToken cancellationToken = default)
    {
        var requestUri = BuildPendingRequestUri(query);
        var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new AdminApprovalPage([], 0, query.Page, query.PageSize);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AdminApprovalPage>(SerializerOptions, cancellationToken)
            ?? new AdminApprovalPage([], 0, query.Page, query.PageSize);
    }

    public async Task<PendingUserDetail?> GetPendingDetailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/v1/admin/registrations/{userId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PendingUserDetail>(SerializerOptions, cancellationToken);
    }

    public async Task<AdminApprovalUpdateResult> UpdateAsync(
        Guid userId,
        AdminRegistrationUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/v1/admin/registrations/{userId}", request, SerializerOptions, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<AdminApprovalUpdatePayload>(SerializerOptions, cancellationToken);
            return new AdminApprovalUpdateResult(true, payload?.Detail, payload?.Status, null);
        }

        var failurePayload = await response.Content.ReadFromJsonAsync<AdminApprovalPayload>(SerializerOptions, cancellationToken);
        var fallbackError = await ApiErrorReader.ReadMessageAsync(response, cancellationToken)
            ?? "Unable to save registration changes.";

        return new AdminApprovalUpdateResult(
            false,
            null,
            failurePayload?.Status,
            string.IsNullOrWhiteSpace(failurePayload?.Error) ? fallbackError : failurePayload.Error,
            failurePayload?.ValidationErrors);
    }

    public Task<AdminApprovalActionResult> ApproveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SubmitActionAsync($"/api/v1/admin/registrations/{userId}/approve", cancellationToken);

    public Task<AdminApprovalActionResult> RejectAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SubmitActionAsync($"/api/v1/admin/registrations/{userId}/reject", cancellationToken);

    public Task<AdminApprovalActionResult> HoldAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SubmitActionAsync($"/api/v1/admin/registrations/{userId}/hold", cancellationToken);

    public Task<AdminApprovalActionResult> CancelAsync(Guid userId, CancellationToken cancellationToken = default) =>
        SubmitActionAsync($"/api/v1/admin/registrations/{userId}/cancel", cancellationToken);

    private async Task<AdminApprovalActionResult> SubmitActionAsync(string route, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync(route, content: null, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadFromJsonAsync<AdminApprovalPayload>(SerializerOptions, cancellationToken);
            return new AdminApprovalActionResult(true, payload?.Status, null);
        }

        var failurePayload = await response.Content.ReadFromJsonAsync<AdminApprovalPayload>(SerializerOptions, cancellationToken);
        var fallbackError = response.StatusCode == HttpStatusCode.Forbidden
            ? "You do not have permission to perform this admin action."
            : await ApiErrorReader.ReadMessageAsync(response, cancellationToken)
                ?? "Unable to complete the approval action.";

        return new AdminApprovalActionResult(
            false,
            failurePayload?.Status,
            string.IsNullOrWhiteSpace(failurePayload?.Error) ? fallbackError : failurePayload.Error,
            failurePayload?.ValidationErrors);
    }

    private sealed record AdminApprovalPayload(
        string? Status,
        string? Error,
        Guid? UserId,
        IReadOnlyDictionary<string, string[]>? ValidationErrors);

    private sealed record AdminApprovalUpdatePayload(
        string? Status,
        Guid? UserId,
        PendingUserDetail? Detail);

    private static string BuildPendingRequestUri(AdminApprovalQuery query)
    {
        var parameters = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters["q"] = query.Search.Trim();
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !string.Equals(query.Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            parameters["status"] = query.Status;
        }

        if (!string.IsNullOrWhiteSpace(query.Role) && !string.Equals(query.Role, "All Roles", StringComparison.OrdinalIgnoreCase))
        {
            parameters["role"] = query.Role;
        }

        if (!string.IsNullOrWhiteSpace(query.Clinic) && !string.Equals(query.Clinic, "All Clinics", StringComparison.OrdinalIgnoreCase))
        {
            parameters["clinic"] = query.Clinic;
        }

        if (query.FromDate.HasValue)
        {
            parameters["from"] = query.FromDate.Value.ToString("yyyy-MM-dd");
        }

        if (query.ToDate.HasValue)
        {
            parameters["to"] = query.ToDate.Value.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrWhiteSpace(query.SortBy) && !string.Equals(query.SortBy, "Date submitted", StringComparison.OrdinalIgnoreCase))
        {
            parameters["sort"] = query.SortBy;
        }

        parameters["page"] = query.Page.ToString();
        parameters["pageSize"] = query.PageSize.ToString();

        var queryString = string.Join(
            "&",
            parameters
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value!)}"));

        return string.IsNullOrWhiteSpace(queryString)
            ? "/api/v1/admin/registrations/pending"
            : $"/api/v1/admin/registrations/pending?{queryString}";
    }
}
