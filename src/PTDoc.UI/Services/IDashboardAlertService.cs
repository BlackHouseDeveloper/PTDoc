using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;

namespace PTDoc.UI.Services;

public interface IDashboardAlertService
{
    Task<DashboardAlertsResponse> GetAlertsAsync(int take = 10, CancellationToken cancellationToken = default);
    Task<DashboardSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed class HttpDashboardAlertService(HttpClient httpClient) : IDashboardAlertService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<DashboardAlertsResponse> GetAlertsAsync(int take = 10, CancellationToken cancellationToken = default)
    {
        var normalizedTake = Math.Clamp(take, 1, 50);
        var response = await httpClient.GetFromJsonAsync<DashboardAlertsResponse>(
            $"/api/v1/dashboard/alerts?take={normalizedTake}",
            SerializerOptions,
            cancellationToken);

        return response ?? new DashboardAlertsResponse();
    }

    public async Task<DashboardSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<DashboardSnapshotResponse>(
            "/api/v1/dashboard/snapshot",
            SerializerOptions,
            cancellationToken);

        return response ?? new DashboardSnapshotResponse();
    }
}
