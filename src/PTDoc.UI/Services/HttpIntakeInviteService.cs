using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Intake;

namespace PTDoc.UI.Services;

/// <summary>
/// HTTP-backed standalone intake invite/session service for shared Blazor UI surfaces.
/// </summary>
public sealed class HttpIntakeInviteService(HttpClient httpClient) : IIntakeInviteService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IntakeInviteLinkResult> CreateInviteAsync(Guid intakeId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/v1/intake/{intakeId}/delivery/link", content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new IntakeInviteLinkResult(false, intakeId, Guid.Empty, null, null, await ReadErrorAsync(response, cancellationToken));
        }

        var bundle = await response.Content.ReadFromJsonAsync<IntakeDeliveryBundleResponse>(SerializerOptions, cancellationToken);
        if (bundle is null)
        {
            return new IntakeInviteLinkResult(false, intakeId, Guid.Empty, null, null, "Invite bundle payload was empty.");
        }

        return new IntakeInviteLinkResult(true, bundle.IntakeId, bundle.PatientId, bundle.InviteUrl, bundle.ExpiresAt, null);
    }

    public async Task<IntakeInviteValidationResponse> ValidateInviteTokenAsync(string inviteToken, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/intake/access/validate-invite",
            new ValidateIntakeInviteRequest { InviteToken = inviteToken },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntakeInviteValidationResponse>(SerializerOptions, cancellationToken)
            ?? new IntakeInviteValidationResponse(false, null, "Invite validation payload was empty.");
    }

    public async Task<bool> SendOtpAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        CancellationToken cancellationToken = default)
        => (await SendOtpWithDiagnosticsAsync(inviteToken, contact, channel, cancellationToken)).Success;

    public async Task<IntakeOtpSendResult> SendOtpWithDiagnosticsAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/intake/access/send-otp",
            new SendIntakeOtpRequest
            {
                InviteToken = inviteToken,
                Contact = contact,
                Channel = channel
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SendIntakeOtpResponse>(SerializerOptions, cancellationToken);
        var success = payload?.Success == true;
        return new IntakeOtpSendResult(
            success,
            payload?.RequestId ?? string.Empty,
            success ? IntakeOtpSendOutcome.Delivered : IntakeOtpSendOutcome.ProviderRejected);
    }

    public async Task<IntakeInviteResult> VerifyOtpAndIssueAccessTokenAsync(
        string inviteToken,
        string contact,
        OtpChannel channel,
        string otpCode,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/intake/access/verify-otp",
            new VerifyIntakeOtpRequest
            {
                InviteToken = inviteToken,
                Contact = contact,
                Channel = channel,
                OtpCode = otpCode
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntakeInviteResult>(SerializerOptions, cancellationToken)
            ?? new IntakeInviteResult(false, null, null, "OTP verification payload was empty.");
    }

    public async Task<bool> ValidateAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/intake/access/validate-session",
            new IntakeAccessTokenRequest { AccessToken = accessToken },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IntakeAccessTokenValidationResponse>(SerializerOptions, cancellationToken);
        return payload?.IsValid == true;
    }

    public async Task RevokeAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/intake/access/revoke-session",
            new IntakeAccessTokenRequest { AccessToken = accessToken },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("error", out var errorProperty) && errorProperty.ValueKind == JsonValueKind.String)
            {
                return errorProperty.GetString() ?? "Request failed.";
            }
        }
        catch (JsonException)
        {
        }

        return $"Request failed with status {(int)response.StatusCode}.";
    }
}
