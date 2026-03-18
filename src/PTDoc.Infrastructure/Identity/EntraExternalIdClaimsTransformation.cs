using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// Normalizes Microsoft Entra External ID claims into the claim types PTDoc already uses internally.
/// </summary>
public sealed class EntraExternalIdClaimsTransformation : IClaimsTransformation
{
    private readonly EntraExternalIdOptions _options;

    public EntraExternalIdClaimsTransformation(IOptions<EntraExternalIdOptions> options)
    {
        _options = options.Value;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!_options.Enabled || principal.Identity is not ClaimsIdentity identity)
        {
            return Task.FromResult(principal);
        }

        var transformed = new ClaimsIdentity(identity);
        var claims = transformed.Claims.ToList();

        AddSingleClaimIfMissing(
            transformed,
            ClaimTypes.NameIdentifier,
            claims,
            _options.Claims.SubjectClaimType,
            "sub");

        AddSingleClaimIfMissing(
            transformed,
            PTDocClaimTypes.ExternalSubject,
            claims,
            _options.Claims.SubjectClaimType,
            "sub");

        AddSingleClaimIfMissing(
            transformed,
            ClaimTypes.Name,
            claims,
            _options.Claims.NameClaimType,
            _options.Claims.UsernameClaimType,
            _options.Claims.EmailClaimType);

        AddSingleClaimIfMissing(
            transformed,
            ClaimTypes.Email,
            claims,
            _options.Claims.EmailClaimType);

        AddSingleClaimIfMissing(
            transformed,
            HttpTenantContextAccessor.ClinicIdClaimType,
            claims,
            _options.Claims.ClinicIdClaimType);

        AddSingleClaimIfMissing(
            transformed,
            HttpPatientContextAccessor.PatientIdClaimType,
            claims,
            _options.Claims.PatientIdClaimType);

        AddSingleClaimIfMissing(
            transformed,
            PTDocClaimTypes.ExternalProvider,
            claims,
            PTDocClaimTypes.ExternalProvider);

        if (!transformed.HasClaim(c => c.Type == PTDocClaimTypes.ExternalProvider))
        {
            transformed.AddClaim(new Claim(PTDocClaimTypes.ExternalProvider, _options.ProviderKey));
        }

        if (!transformed.HasClaim(c => c.Type == PTDocClaimTypes.AuthenticationType))
        {
            transformed.AddClaim(new Claim(PTDocClaimTypes.AuthenticationType, "entra_jwt"));
        }

        foreach (var role in GetClaimValues(claims, _options.Claims.RoleClaimType, _options.Claims.StandardRoleClaimType)
                     .SelectMany(ExpandRoleAliases)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!transformed.HasClaim(ClaimTypes.Role, role))
            {
                transformed.AddClaim(new Claim(ClaimTypes.Role, role));
            }
        }

        return Task.FromResult(new ClaimsPrincipal(transformed));
    }

    private static void AddSingleClaimIfMissing(
        ClaimsIdentity identity,
        string targetClaimType,
        IReadOnlyCollection<Claim> claims,
        params string[] sourceClaimTypes)
    {
        if (identity.HasClaim(c => c.Type == targetClaimType))
        {
            return;
        }

        foreach (var sourceClaimType in sourceClaimTypes.Where(static t => !string.IsNullOrWhiteSpace(t)))
        {
            var value = claims.FirstOrDefault(c => c.Type == sourceClaimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                identity.AddClaim(new Claim(targetClaimType, value));
                return;
            }
        }
    }

    private static IEnumerable<string> GetClaimValues(
        IReadOnlyCollection<Claim> claims,
        params string[] claimTypes)
    {
        foreach (var claimType in claimTypes.Where(static t => !string.IsNullOrWhiteSpace(t)))
        {
            foreach (var claim in claims.Where(c => c.Type == claimType))
            {
                foreach (var value in ParseClaimValue(claim.Value))
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> ParseClaimValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        if (value.StartsWith("[", StringComparison.Ordinal))
        {
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(value);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var values = new List<string>();
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var role = item.GetString();
                            if (!string.IsNullOrWhiteSpace(role))
                            {
                                values.Add(role);
                            }
                        }
                    }

                    return values;
                }
            }
            catch (JsonException)
            {
                // Fall through to the raw claim value.
            }
            finally
            {
                document?.Dispose();
            }
        }

        return [value];
    }

    private static IEnumerable<string> ExpandRoleAliases(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            yield break;
        }

        yield return role;

        if (string.Equals(role, "Front Desk", StringComparison.OrdinalIgnoreCase))
        {
            yield return Roles.FrontDesk;
        }
    }
}
