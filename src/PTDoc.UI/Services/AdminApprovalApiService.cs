using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Identity;

namespace PTDoc.UI.Services;

public interface IAdminApprovalService
{
    Task<AdminApprovalPage> GetPendingAsync(AdminApprovalQuery query, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> ApproveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> RejectAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> HoldAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<AdminApprovalActionResult> CancelAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed record AdminApprovalActionResult(
    bool Succeeded,
    string? Status,
    string? Error);

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
            : "Unable to complete the approval action.";

        return new AdminApprovalActionResult(
            false,
            failurePayload?.Status,
            string.IsNullOrWhiteSpace(failurePayload?.Error) ? fallbackError : failurePayload.Error);
    }

    private sealed record AdminApprovalPayload(
        string? Status,
        string? Error,
        Guid? UserId);

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
