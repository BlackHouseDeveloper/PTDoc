using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-client implementation of <see cref="IAppointmentService"/> for Blazor UI.
/// Calls the appointments REST endpoints via the named server API client.
/// </summary>
public sealed class AppointmentApiService(HttpClient httpClient) : IAppointmentService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppointmentsOverviewResponse> GetOverviewAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v1/appointments?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var result = await httpClient.GetFromJsonAsync<AppointmentsOverviewResponse>(url, SerializerOptions, cancellationToken);
        return result ?? new AppointmentsOverviewResponse();
    }

    public async Task<AppointmentListItemResponse> CreateAsync(
        CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/appointments/", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AppointmentListItemResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Appointment creation response was empty.");
    }

    public async Task<AppointmentListItemResponse?> UpdateAsync(
        Guid id,
        UpdateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/v1/appointments/{id}", request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppointmentListItemResponse>(SerializerOptions, cancellationToken);
    }

    public async Task<AppointmentListItemResponse?> CheckInAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/v1/appointments/{id}/check-in", content: null, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AppointmentListItemResponse>(SerializerOptions, cancellationToken);
    }
}
