namespace PTDoc.Infrastructure.Services;

using System.Security.Claims;
using PTDoc.Application.Auth;

public sealed class CredentialValidator : ICredentialValidator
{
    private const string DemoPin = "1234";
    private const string DemoDisplayName = "Dr. Demo Clinician";

    public Task<ClaimsIdentity?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (username != DemoPin || password != DemoPin)
        {
            return Task.FromResult<ClaimsIdentity?>(null);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, DemoPin),
            new Claim(ClaimTypes.Name, DemoDisplayName),
            new Claim(ClaimTypes.Email, $"{DemoPin}@demo.pin"),
            new Claim(ClaimTypes.Role, "Clinician")
        };

        return Task.FromResult<ClaimsIdentity?>(new ClaimsIdentity(claims, "PTDocAuth"));
    }
}