namespace PTDoc.Application.Auth;

using PTDoc.Application.Identity;

public interface ITokenService
{
    Task<TokenLoginResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenLoginResult> CompletePinChangeAsync(string challengeToken, string newPin, CancellationToken cancellationToken = default);
    Task<AuthenticatorEnrollmentStart?> BeginMfaEnrollmentAsync(string challengeToken, CancellationToken cancellationToken = default);
    Task<AuthenticatorEnrollmentCompletion?> VerifyMfaEnrollmentAsync(string challengeToken, string code, CancellationToken cancellationToken = default);
    Task<MfaCodeVerificationResult> VerifyMfaAsync(string challengeToken, string code, bool useRecoveryCode, CancellationToken cancellationToken = default);
    Task<TokenResponse?> CompleteAuthenticationAsync(string completionToken, CancellationToken cancellationToken = default);
    Task<TokenResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
}

public sealed record TokenLoginResult(
    TokenResponse? Tokens = null,
    TokenAuthenticationStep? StepUp = null,
    string? ErrorMessage = null)
{
    public bool Succeeded => Tokens is not null;
}

public sealed record TokenAuthenticationStep(
    AuthStatus Status,
    string ChallengeToken,
    int? MinimumPinLength = null);

public sealed record AuthenticatorEnrollmentStart(
    string ManualKey,
    string OtpAuthUri,
    string QrSvg,
    string EnrollmentChallengeToken);

public sealed record AuthenticatorEnrollmentCompletion(
    IReadOnlyList<string> RecoveryCodes,
    string CompletionToken);

public sealed record MfaCodeVerificationResult(
    bool Succeeded,
    string? CompletionToken = null,
    string? ErrorCode = null);
