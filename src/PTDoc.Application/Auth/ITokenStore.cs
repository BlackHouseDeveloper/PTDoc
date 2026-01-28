namespace PTDoc.Application.Auth;

public interface ITokenStore
{
    Task<TokenResponse?> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TokenResponse tokens, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}