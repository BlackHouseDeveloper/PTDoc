using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Settings;

namespace PTDoc.UI.Services;

/// <summary>Web-only HTTP façade for clinic Settings administration.</summary>
public sealed class SettingsAdministrationApiClient(HttpClient httpClient) :
    IRolePermissionAdministrationService,
    ISecurityPolicyAdministrationService,
    ISchedulingAdministrationService,
    IAutoCheckInAdministrationService,
    IKioskCheckInService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<RolePermissionsResponse> GetAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        await GetRequiredAsync<RolePermissionsResponse>("/api/v1/admin/roles/permissions", cancellationToken);

    public Task<SettingsOperationResult<RolePermissionSet>> UpdateAsync(Guid clinicId, string roleKey, UpdateRolePermissionsRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<RolePermissionSet>(HttpMethod.Put, $"/api/v1/admin/roles/{Uri.EscapeDataString(roleKey)}/permissions", request, cancellationToken);

    public Task<SettingsOperationResult<RolePermissionSet>> CloneAsync(Guid clinicId, string targetRoleKey, CloneRolePermissionsRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<RolePermissionSet>(HttpMethod.Post, $"/api/v1/admin/roles/{Uri.EscapeDataString(targetRoleKey)}/clone", request, cancellationToken);

    Task<SecurityPolicyDto> ISecurityPolicyAdministrationService.GetAsync(Guid clinicId, CancellationToken cancellationToken) =>
        GetRequiredAsync<SecurityPolicyDto>("/api/v1/admin/security-policy", cancellationToken);

    public Task<SettingsOperationResult<SecurityPolicyDto>> UpdateAsync(Guid clinicId, UpdateSecurityPolicyRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<SecurityPolicyDto>(HttpMethod.Put, "/api/v1/admin/security-policy", request, cancellationToken);

    public Task<MfaReadinessDto> GetMfaReadinessAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<MfaReadinessDto>("/api/v1/admin/security-policy/mfa-readiness", cancellationToken);

    public Task<SettingsOperationResult<bool>> ForcePinChangeAsync(Guid clinicId, Guid userId, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Post, $"/api/v1/admin/users/{userId}/force-pin-change", body: null, cancellationToken);

    public Task<SettingsOperationResult<bool>> ResetMfaAsync(Guid clinicId, Guid userId, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Post, $"/api/v1/admin/users/{userId}/reset-mfa", body: null, cancellationToken);

    public Task<IReadOnlyList<VisitTypeDto>> GetVisitTypesAsync(Guid clinicId, bool includeInactive, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<IReadOnlyList<VisitTypeDto>>($"/api/v1/admin/scheduling/visit-types?includeInactive={includeInactive.ToString().ToLowerInvariant()}", cancellationToken);

    public Task<SettingsOperationResult<VisitTypeDto>> CreateVisitTypeAsync(Guid clinicId, SaveVisitTypeRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<VisitTypeDto>(HttpMethod.Post, "/api/v1/admin/scheduling/visit-types", request, cancellationToken);

    public Task<SettingsOperationResult<VisitTypeDto>> UpdateVisitTypeAsync(Guid clinicId, Guid visitTypeId, SaveVisitTypeRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<VisitTypeDto>(HttpMethod.Put, $"/api/v1/admin/scheduling/visit-types/{visitTypeId}", request, cancellationToken);

    public Task<SettingsOperationResult<VisitTypeDto>> DeactivateVisitTypeAsync(Guid clinicId, Guid visitTypeId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<VisitTypeDto>(HttpMethod.Delete, $"/api/v1/admin/scheduling/visit-types/{visitTypeId}?expectedVersion={expectedVersion}", body: null, cancellationToken);

    public Task<SchedulingPreferencesDto> GetPreferencesAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<SchedulingPreferencesDto>("/api/v1/admin/scheduling/preferences", cancellationToken);

    public Task<SettingsOperationResult<SchedulingPreferencesDto>> UpdatePreferencesAsync(Guid clinicId, UpdateSchedulingPreferencesRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<SchedulingPreferencesDto>(HttpMethod.Put, "/api/v1/admin/scheduling/preferences", request, cancellationToken);

    public Task<ClinicHoursDto> GetClinicHoursAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<ClinicHoursDto>("/api/v1/admin/scheduling/clinic-hours", cancellationToken);

    public Task<SettingsOperationResult<ClinicHoursDto>> UpdateClinicHoursAsync(Guid clinicId, UpdateClinicHoursRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<ClinicHoursDto>(HttpMethod.Put, "/api/v1/admin/scheduling/clinic-hours", request, cancellationToken);

    public Task<IReadOnlyList<ScheduleBlockDto>> GetScheduleBlocksAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<IReadOnlyList<ScheduleBlockDto>>("/api/v1/admin/scheduling/blocks", cancellationToken);

    public Task<SettingsOperationResult<ScheduleBlockDto>> CreateScheduleBlockAsync(Guid clinicId, SaveScheduleBlockRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<ScheduleBlockDto>(HttpMethod.Post, "/api/v1/admin/scheduling/blocks", request, cancellationToken);

    public Task<SettingsOperationResult<ScheduleBlockDto>> UpdateScheduleBlockAsync(Guid clinicId, Guid blockId, SaveScheduleBlockRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<ScheduleBlockDto>(HttpMethod.Put, $"/api/v1/admin/scheduling/blocks/{blockId}", request, cancellationToken);

    public Task<SettingsOperationResult<ScheduleBlockDto>> DeactivateScheduleBlockAsync(Guid clinicId, Guid blockId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<ScheduleBlockDto>(HttpMethod.Delete, $"/api/v1/admin/scheduling/blocks/{blockId}?expectedVersion={expectedVersion}", body: null, cancellationToken);

    Task<AutoCheckInPolicyDto> IAutoCheckInAdministrationService.GetAsync(Guid clinicId, CancellationToken cancellationToken) =>
        GetRequiredAsync<AutoCheckInPolicyDto>("/api/v1/admin/auto-check-in", cancellationToken);

    public Task<SettingsOperationResult<AutoCheckInPolicyDto>> UpdateAsync(Guid clinicId, UpdateAutoCheckInPolicyRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<AutoCheckInPolicyDto>(HttpMethod.Put, "/api/v1/admin/auto-check-in", request, cancellationToken);

    public Task<IReadOnlyList<KioskStationDto>> GetStationsAsync(Guid clinicId, CancellationToken cancellationToken = default) =>
        GetRequiredAsync<IReadOnlyList<KioskStationDto>>("/api/v1/admin/kiosk/stations", cancellationToken);

    public Task<SettingsOperationResult<KioskEnrollmentCodeDto>> CreateStationAsync(Guid clinicId, CreateKioskStationRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<KioskEnrollmentCodeDto>(HttpMethod.Post, "/api/v1/admin/kiosk/stations", request, cancellationToken);

    public Task<SettingsOperationResult<KioskStationDto>> UpdateStationAsync(Guid clinicId, Guid stationId, UpdateKioskStationRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<KioskStationDto>(HttpMethod.Put, $"/api/v1/admin/kiosk/stations/{stationId}", request, cancellationToken);

    public Task<SettingsOperationResult<KioskEnrollmentCodeDto>> RotateEnrollmentAsync(Guid clinicId, Guid stationId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<KioskEnrollmentCodeDto>(HttpMethod.Post, $"/api/v1/admin/kiosk/stations/{stationId}/rotate", new { expectedVersion }, cancellationToken);

    public Task<SettingsOperationResult<bool>> RevokeStationAsync(Guid clinicId, Guid stationId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"/api/v1/admin/kiosk/stations/{stationId}?expectedVersion={expectedVersion}", body: null, cancellationToken);

    public Task<SettingsOperationResult<KioskEnrollmentResult>> EnrollAsync(string enrollmentCode, CancellationToken cancellationToken = default) =>
        SendAsync<KioskEnrollmentResult>(HttpMethod.Post, "/api/v1/kiosk/enroll", new { enrollmentCode }, cancellationToken);

    public Task<SettingsOperationResult<KioskCheckInTokenDto>> CreateCheckInTokenAsync(Guid clinicId, Guid appointmentId, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default) =>
        SendAsync<KioskCheckInTokenDto>(HttpMethod.Post, $"/api/v1/admin/kiosk/stations/appointments/{appointmentId}/token", body: null, cancellationToken);

    public Task<SettingsOperationResult<KioskCheckInResult>> CheckInAsync(string deviceCredential, string appointmentToken, CancellationToken cancellationToken = default) =>
        SendAsync<KioskCheckInResult>(HttpMethod.Post, "/api/v1/kiosk/check-in", new { deviceCredential, appointmentToken }, cancellationToken);

    private async Task<T> GetRequiredAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await CreateExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Settings API returned an empty response.");
    }

    private async Task<SettingsOperationResult<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value is null
                ? new SettingsOperationResult<T>(SettingsOperationStatus.Succeeded)
                : SettingsOperationResult<T>.Success(value);
        }

        var problem = await TryReadProblemAsync(response, cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.UnprocessableEntity => SettingsOperationResult<T>.Validation(problem.ValidationErrors ?? new Dictionary<string, string[]>()),
            HttpStatusCode.Conflict => SettingsOperationResult<T>.Conflict(problem.Error ?? "version_conflict"),
            HttpStatusCode.Forbidden => SettingsOperationResult<T>.Forbidden(problem.Error ?? "forbidden"),
            HttpStatusCode.NotFound => SettingsOperationResult<T>.NotFound(),
            _ => throw new SettingsApiException((int)response.StatusCode, problem.Error ?? "settings_request_failed")
        };
    }

    private static async Task<SettingsApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await TryReadProblemAsync(response, cancellationToken);
        return new SettingsApiException((int)response.StatusCode, problem.Error ?? "settings_request_failed");
    }

    private static async Task<ApiProblem> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblem>(JsonOptions, cancellationToken) ?? new ApiProblem();
        }
        catch (JsonException)
        {
            return new ApiProblem();
        }
    }

    private sealed record ApiProblem
    {
        public string? Error { get; init; }
        public Dictionary<string, string[]>? ValidationErrors { get; init; }
    }
}

public sealed class SettingsApiException(int statusCode, string errorCode) : Exception(errorCode)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}
