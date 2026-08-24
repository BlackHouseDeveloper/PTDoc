namespace PTDoc.Infrastructure.Services;

using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;

public sealed class TokenService : ITokenService
{
    private readonly HttpClient httpClient;

    public TokenService(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<TokenLoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/token", request, cancellationToken);
        return await ReadLoginResultAsync(response, cancellationToken);
    }

    public async Task<TokenLoginResult> CompletePinChangeAsync(
        string challengeToken,
        string newPin,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/pin-change",
            new { ChallengeToken = challengeToken, NewPin = newPin },
            cancellationToken);
        return await ReadLoginResultAsync(response, cancellationToken);
    }

    public async Task<AuthenticatorEnrollmentStart?> BeginMfaEnrollmentAsync(
        string challengeToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/enroll",
            new { ChallengeToken = challengeToken },
            cancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<AuthenticatorEnrollmentStart>(cancellationToken: cancellationToken)
            : null;
    }

    public async Task<AuthenticatorEnrollmentCompletion?> VerifyMfaEnrollmentAsync(
        string challengeToken,
        string code,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/verify-enrollment",
            new { ChallengeToken = challengeToken, Code = code },
            cancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<AuthenticatorEnrollmentCompletion>(cancellationToken: cancellationToken)
            : null;
    }

    public async Task<MfaCodeVerificationResult> VerifyMfaAsync(
        string challengeToken,
        string code,
        bool useRecoveryCode,
        CancellationToken cancellationToken = default)
    {
        var requestUri = useRecoveryCode
            ? "/api/v1/auth/mfa/recovery"
            : "/api/v1/auth/mfa/verify";
        var body = useRecoveryCode
            ? new { ChallengeToken = challengeToken, RecoveryCode = code }
            : (object)new { ChallengeToken = challengeToken, Code = code };
        using var response = await httpClient.PostAsJsonAsync(requestUri, body, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<MfaCodeVerificationResult>(cancellationToken: cancellationToken);
        return result ?? new MfaCodeVerificationResult(false, ErrorCode: "invalid_code");
    }

    public async Task<TokenResponse?> CompleteAuthenticationAsync(
        string completionToken,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/complete",
            new { CompletionToken = completionToken },
            cancellationToken);
        return response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
            : null;
    }

    private static async Task<TokenLoginResult> ReadLoginResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
            return tokens is null
                ? new TokenLoginResult(ErrorMessage: "Authentication returned an invalid token response.")
                : new TokenLoginResult(Tokens: tokens);
        }

        if (response.StatusCode is not (HttpStatusCode.Accepted or HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity))
        {
            return new TokenLoginResult();
        }

        var payload = await response.Content.ReadFromJsonAsync<AuthenticationStepPayload>(cancellationToken: cancellationToken);
        if (payload is not null
            && Enum.TryParse<AuthStatus>(payload.Status, ignoreCase: true, out var status)
            && status is AuthStatus.RequiresPinChange or AuthStatus.RequiresMfaEnrollment or AuthStatus.RequiresMfaVerification
            && !string.IsNullOrWhiteSpace(payload.ChallengeToken))
        {
            return new TokenLoginResult(
                StepUp: new TokenAuthenticationStep(status, payload.ChallengeToken, payload.MinimumPinLength),
                ErrorMessage: payload.Message);
        }

        return new TokenLoginResult(ErrorMessage: payload?.Message);
    }

    public async Task<TokenResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/refresh", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/auth/logout", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed class AuthenticationStepPayload
    {
        public string? Status { get; set; }
        public string? ChallengeToken { get; set; }
        public int? MinimumPinLength { get; set; }
        public string? Message { get; set; }
    }
}
