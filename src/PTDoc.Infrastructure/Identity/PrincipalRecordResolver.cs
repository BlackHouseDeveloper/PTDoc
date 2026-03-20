using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// Resolves internal PTDoc records for externally authenticated principals.
/// Business context is derived from database records, not custom token claims.
/// </summary>
public sealed class PrincipalRecordResolver
{
    private const string ProvisioningCacheKey = "PTDoc:PrincipalProvisioning";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _services;

    public PrincipalRecordResolver(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider services)
    {
        _httpContextAccessor = httpContextAccessor;
        _services = services;
    }

    public PrincipalProvisioningResult GetProvisioningResult()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var principal = httpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return PrincipalProvisioningResult.Unauthenticated;
        }

        if (httpContext!.Items.TryGetValue(ProvisioningCacheKey, out var cached)
            && cached is PrincipalProvisioningResult cachedResult)
        {
            return cachedResult;
        }

        var resolved = ResolveProvisioningResult(principal);
        httpContext.Items[ProvisioningCacheKey] = resolved;
        return resolved;
    }

    public Guid? TryResolveInternalUserId()
    {
        return GetProvisioningResult().InternalUserId;
    }

    public Guid? TryResolveClinicId()
    {
        return GetProvisioningResult().ClinicId;
    }

    public Guid? TryResolvePatientId()
    {
        return GetProvisioningResult().PatientId;
    }

    public void EnsureProvisioned()
    {
        var result = GetProvisioningResult();
        if (result.RequiresProvisioningFailure)
        {
            throw new ProvisioningException(result);
        }
    }

    private ApplicationDbContext CreateContext()
    {
        var dbOptions = _services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        return new ApplicationDbContext(dbOptions);
    }

    private PrincipalProvisioningResult ResolveProvisioningResult(ClaimsPrincipal principal)
    {
        if (TryGetGuidClaim(principal, out var internalUserId, PTDocClaimTypes.InternalUserIdAliases()))
        {
            return new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = true,
                PrincipalType = PrincipalTypes.User,
                InternalUserId = internalUserId,
                ClinicId = TryGetGuidClaimValue(principal, HttpTenantContextAccessor.ClinicIdClaimType),
                Provider = principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value
            };
        }

        var patientId = TryGetGuidClaimValue(principal, HttpPatientContextAccessor.PatientIdClaimType);
        var clinicId = TryGetGuidClaimValue(principal, HttpTenantContextAccessor.ClinicIdClaimType);
        if (patientId.HasValue)
        {
            return new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = true,
                PrincipalType = PrincipalTypes.Patient,
                PatientId = patientId,
                ClinicId = clinicId,
                Provider = principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value
            };
        }

        var provider = principal.FindFirst(PTDocClaimTypes.ExternalProvider)?.Value;
        var externalSubject = principal.Claims
            .Where(c => PTDocClaimTypes.ExternalSubjectAliases().Contains(c.Type, StringComparer.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        var principalType = HasRole(principal, Roles.Patient) ? PrincipalTypes.Patient : PrincipalTypes.User;
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(externalSubject))
        {
            return BuildFailure(principalType, provider, externalSubject, "missing_external_identity", "Authenticated principal is missing an external identity subject or provider.");
        }

        using var db = CreateContext();
        var mapping = db.ExternalIdentityMappings
            .AsNoTracking()
            .Where(m => m.IsActive
                && m.Provider == provider
                && m.ExternalSubject == externalSubject
                && m.PrincipalType == principalType)
            .FirstOrDefault();

        if (mapping is null)
        {
            return BuildFailure(principalType, provider, externalSubject, "identity_not_mapped", "Authenticated principal is not provisioned in PTDoc.");
        }

        return principalType == PrincipalTypes.Patient
            ? ResolvePatientProvisioning(db, mapping, provider, externalSubject)
            : ResolveUserProvisioning(db, mapping, provider, externalSubject);
    }

    private static PrincipalProvisioningResult ResolvePatientProvisioning(
        ApplicationDbContext db,
        ExternalIdentityMapping mapping,
        string provider,
        string externalSubject)
    {
        var patient = db.Patients
            .AsNoTracking()
            .Where(p => !p.IsArchived && p.Id == mapping.InternalEntityId)
            .Select(p => new { p.Id, p.ClinicId })
            .FirstOrDefault();

        if (patient is null)
        {
            return BuildFailure(PrincipalTypes.Patient, provider, externalSubject, "patient_not_found", "External patient mapping points to a missing or archived patient.");
        }

        return new PrincipalProvisioningResult
        {
            IsAuthenticated = true,
            IsProvisioned = true,
            PrincipalType = PrincipalTypes.Patient,
            Provider = provider,
            ExternalSubject = externalSubject,
            PatientId = patient.Id,
            ClinicId = mapping.TenantId ?? patient.ClinicId
        };
    }

    private static PrincipalProvisioningResult ResolveUserProvisioning(
        ApplicationDbContext db,
        ExternalIdentityMapping mapping,
        string provider,
        string externalSubject)
    {
        var user = db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Id == mapping.InternalEntityId)
            .Select(u => new { u.Id, u.ClinicId })
            .FirstOrDefault();

        if (user is null)
        {
            return BuildFailure(PrincipalTypes.User, provider, externalSubject, "user_not_found", "External user mapping points to a missing or inactive user.");
        }

        return new PrincipalProvisioningResult
        {
            IsAuthenticated = true,
            IsProvisioned = true,
            PrincipalType = PrincipalTypes.User,
            Provider = provider,
            ExternalSubject = externalSubject,
            InternalUserId = user.Id,
            ClinicId = mapping.TenantId ?? user.ClinicId
        };
    }

    private static PrincipalProvisioningResult BuildFailure(
        string principalType,
        string? provider,
        string? externalSubject,
        string failureCode,
        string failureReason)
    {
        return new PrincipalProvisioningResult
        {
            IsAuthenticated = true,
            IsProvisioned = false,
            PrincipalType = principalType,
            Provider = provider,
            ExternalSubject = externalSubject,
            FailureCode = failureCode,
            FailureReason = failureReason
        };
    }

    private static bool HasRole(ClaimsPrincipal principal, string role)
    {
        return principal.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Any(c => string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetGuidClaim(
        ClaimsPrincipal principal,
        out Guid value,
        IEnumerable<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var claimValue = principal.FindFirst(claimType)?.Value;
            if (Guid.TryParse(claimValue, out value))
            {
                return true;
            }
        }

        value = Guid.Empty;
        return false;
    }

    private static Guid? TryGetGuidClaimValue(ClaimsPrincipal principal, string claimType)
    {
        var claim = principal.FindFirst(claimType)?.Value;
        return Guid.TryParse(claim, out var value) ? value : null;
    }
}
