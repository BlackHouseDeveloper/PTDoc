using Microsoft.AspNetCore.Http;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Identity;

/// <summary>
/// Reads the internal patient identifier from claims or resolves it from PTDoc's patient records.
/// </summary>
public sealed class HttpPatientContextAccessor : IPatientContextAccessor
{
    public const string PatientIdClaimType = "patient_id";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PrincipalRecordResolver? _principalRecordResolver;

    public HttpPatientContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        PrincipalRecordResolver? principalRecordResolver = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _principalRecordResolver = principalRecordResolver;
    }

    public Guid? GetCurrentPatientId()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var claim = principal
            ?.FindFirst(PatientIdClaimType)?.Value;

        if (Guid.TryParse(claim, out var patientId))
        {
            return patientId;
        }

        var resolvedPatientId = _principalRecordResolver?.TryResolvePatientId();
        if (resolvedPatientId.HasValue)
        {
            return resolvedPatientId.Value;
        }

        if (principal?.Identity?.IsAuthenticated == true && principal.IsInRole(Roles.Patient))
        {
            throw new ProvisioningException(_principalRecordResolver?.GetProvisioningResult() ?? new PrincipalProvisioningResult
            {
                IsAuthenticated = true,
                IsProvisioned = false,
                PrincipalType = PrincipalTypes.Patient,
                FailureCode = "patient_not_provisioned",
                FailureReason = "Authenticated patient principal is not mapped to an internal PTDoc patient."
            });
        }

        return null;
    }
}
