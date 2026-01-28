namespace PTDoc.Application.Auth;

public interface ITokenService
{
    Task<TokenResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<TokenResponse?> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
}