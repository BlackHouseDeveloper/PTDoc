namespace PTDoc.Infrastructure.Services;

using System.Security.Claims;
using PTDoc.Application.Auth;

/// <summary>
/// Demo credential validator for development and testing purposes only.
/// TODO: Replace with proper credential validation before production use.
/// This validator uses hardcoded credentials and should never be used in production.
/// </summary>
public sealed class CredentialValidator : ICredentialValidator
{
    private const string DemoPin = "1234";
    private const string DemoDisplayName = "Dr. Demo Clinician";

    public CredentialValidator()
    {
        // Warn if running in production
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CredentialValidator is a demo implementation with hardcoded credentials and must not be used in production. " +
                "Replace with a proper credential validation service that integrates with your identity provider.");
        }
    }

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