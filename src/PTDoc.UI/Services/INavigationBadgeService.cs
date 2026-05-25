using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.DTOs;

namespace PTDoc.UI.Services;

public interface INavigationBadgeService
{
    Task<NavigationBadgeCountsResponse> GetCountsAsync(CancellationToken cancellationToken = default);
}

public interface INavigationBadgeRefreshNotifier
{
    event Action? RefreshRequested;
    void RequestRefresh();
}

public sealed class HttpNavigationBadgeService(HttpClient httpClient) : INavigationBadgeService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<NavigationBadgeCountsResponse> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetFromJsonAsync<NavigationBadgeCountsResponse>(
            "/api/v1/navigation/badges",
            SerializerOptions,
            cancellationToken);

        return response ?? new NavigationBadgeCountsResponse();
    }
}

public sealed class NavigationBadgeRefreshNotifier : INavigationBadgeRefreshNotifier
{
    public event Action? RefreshRequested;

    public void RequestRefresh()
    {
        RefreshRequested?.Invoke();
    }
}
