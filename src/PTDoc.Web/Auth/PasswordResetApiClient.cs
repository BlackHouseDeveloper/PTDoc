using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Auth;

namespace PTDoc.Web.Auth;

public sealed class PasswordResetApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PasswordResetApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> RequestAsync(
        string contact,
        string channel,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PTDocAuthApi");
        var endpoint = string.Equals(channel, "sms", StringComparison.OrdinalIgnoreCase)
            ? "/api/communications/password-reset/send-sms"
            : "/api/communications/password-reset/send-email";

        using var response = await client.PostAsJsonAsync(
            endpoint,
            new { recipient = contact },
            cancellationToken);

        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.TooManyRequests;
    }

    public async Task<PinResetCompletionResult> CompleteAsync(
        string token,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PTDocAuthApi");
        using var response = await client.PostAsJsonAsync(
            "/api/communications/password-reset/complete",
            new { token, newPin },
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new PinResetCompletionResult(PinResetCompletionStatus.Succeeded);
        }

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<PasswordResetCompletionResponse>(
                cancellationToken: cancellationToken);
            if (string.Equals(payload?.Status, "InvalidPin", StringComparison.OrdinalIgnoreCase))
            {
                return new PinResetCompletionResult(
                    PinResetCompletionStatus.InvalidPin,
                    payload?.Error,
                    payload?.MinimumPinLength);
            }
        }
        catch (JsonException)
        {
            // Malformed or legacy error responses fail closed as invalid tokens.
        }

        return new PinResetCompletionResult(
            PinResetCompletionStatus.InvalidToken,
            "The reset link is invalid or expired.");
    }

    public async Task<PinResetTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PTDocAuthApi");
        using var response = await client.PostAsJsonAsync(
            "/api/communications/password-reset/validate",
            new { token },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PinResetTokenValidationResult(false);
        }

        try
        {
            var payload = await response.Content.ReadFromJsonAsync<PasswordResetTokenValidationResponse>(cancellationToken: cancellationToken);
            return payload?.IsValid == true
                ? new PinResetTokenValidationResult(true, Math.Clamp(payload.MinimumPinLength ?? 8, 8, 12))
                : new PinResetTokenValidationResult(false);
        }
        catch (JsonException)
        {
            return new PinResetTokenValidationResult(false);
        }
    }

    private sealed class PasswordResetTokenValidationResponse
    {
        public bool IsValid { get; set; }
        public int? MinimumPinLength { get; set; }
    }

    private sealed class PasswordResetCompletionResponse
    {
        public string? Status { get; set; }
        public string? Error { get; set; }
        public int? MinimumPinLength { get; set; }
    }
}
