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

    public async Task<IReadOnlyList<AppointmentListItemResponse>> GetByPatientAsync(
        Guid patientId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v1/appointments/by-patient/{patientId}?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
        var result = await httpClient.GetFromJsonAsync<IReadOnlyList<AppointmentListItemResponse>>(url, SerializerOptions, cancellationToken);
        return result ?? Array.Empty<AppointmentListItemResponse>();
    }

    public async Task<IReadOnlyList<AppointmentClinicianResponse>> GetCliniciansAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await httpClient.GetFromJsonAsync<IReadOnlyList<AppointmentClinicianResponse>>(
            "/api/v1/appointments/clinicians",
            SerializerOptions,
            cancellationToken);
        return result ?? Array.Empty<AppointmentClinicianResponse>();
    }

    public async Task<AppointmentListItemResponse> CreateAsync(
        CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/appointments/", request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken);

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

        await EnsureSuccessStatusCodeAsync(response, cancellationToken);
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

    private static async Task EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorMessage = await TryReadErrorMessageAsync(response, cancellationToken);
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> TryReadErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        try
        {
            using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var errorsElement)
                && errorsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in errorsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            var message = item.GetString();
                            if (!string.IsNullOrWhiteSpace(message))
                            {
                                return message;
                            }
                        }
                    }
                }
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                var message = errorElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }

            if (document.RootElement.TryGetProperty("title", out var titleElement))
            {
                var message = titleElement.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
