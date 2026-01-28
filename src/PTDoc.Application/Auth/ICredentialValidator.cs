namespace PTDoc.Application.Auth;

using System.Security.Claims;

public interface ICredentialValidator
{
    Task<ClaimsIdentity?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default);
}