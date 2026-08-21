using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

public sealed class SecurityPolicyAdministrationService(
    ApplicationDbContext context,
    IAuditService auditService) : ISecurityPolicyAdministrationService
{
    public async Task<SecurityPolicyDto> GetAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var policy = await context.ClinicSecurityPolicies
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        return policy is null ? CanonicalDefaults() : Map(policy);
    }

    public async Task<SettingsOperationResult<SecurityPolicyDto>> UpdateAsync(
        Guid clinicId,
        UpdateSecurityPolicyRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<SecurityPolicyDto>.Validation(errors);
        }

        var policy = await context.ClinicSecurityPolicies
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        if ((policy?.Version ?? 0) != request.ExpectedVersion)
        {
            return SettingsOperationResult<SecurityPolicyDto>.Conflict();
        }

        var oldPolicy = policy is null ? CanonicalDefaults() : Map(policy);
        if (policy is null)
        {
            policy = new ClinicSecurityPolicy
            {
                ClinicId = clinicId,
                Version = 1,
                UpdatedByUserId = actorUserId
            };
            context.ClinicSecurityPolicies.Add(policy);
        }
        else
        {
            policy.Version++;
            policy.UpdatedAtUtc = DateTime.UtcNow;
            policy.UpdatedByUserId = actorUserId;
        }

        policy.MfaEnforcementMode = request.MfaEnforcementMode;
        policy.MfaEffectiveAtUtc = request.MfaEnforcementMode == MfaEnforcementMode.Off
            ? null
            : request.MfaEffectiveAtUtc;
        policy.RequirePinChangeOnFirstLogin = request.RequirePinChangeOnFirstLogin;
        policy.MinimumPinLength = request.MinimumPinLength;
        policy.SessionInactivityMinutes = request.SessionInactivityMinutes;
        policy.AllowRoleCustomization = request.AllowRoleCustomization;
        policy.RestrictCliniciansToOwnSchedules = request.RestrictCliniciansToOwnSchedules;
        policy.AuthorizationMode = request.AuthorizationMode;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<SecurityPolicyDto>.Conflict();
        }

        var updated = Map(policy);
        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = "SecurityPolicyUpdated",
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = nameof(ClinicSecurityPolicy),
            EntityId = policy.Id,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId,
                ["oldVersion"] = oldPolicy.Version,
                ["newVersion"] = updated.Version,
                ["oldMfaMode"] = oldPolicy.MfaEnforcementMode.ToString(),
                ["newMfaMode"] = updated.MfaEnforcementMode.ToString(),
                ["oldAuthorizationMode"] = oldPolicy.AuthorizationMode.ToString(),
                ["newAuthorizationMode"] = updated.AuthorizationMode.ToString()
            }
        }, cancellationToken);

        return SettingsOperationResult<SecurityPolicyDto>.Success(updated);
    }

    public async Task<MfaReadinessDto> GetMfaReadinessAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var users = await context.Users
            .Where(user => user.ClinicId == clinicId && user.IsActive)
            .Select(user => new
            {
                user.Role,
                IsEnrolled = user.MfaCredential != null && user.MfaCredential.IsActive
            })
            .ToListAsync(cancellationToken);

        return new MfaReadinessDto(
            users.Count,
            users.Count(user => user.IsEnrolled),
            users.Count(user => user.Role == "Admin"),
            users.Count(user => user.Role == "Admin" && user.IsEnrolled));
    }

    public async Task<SettingsOperationResult<bool>> ForcePinChangeAsync(
        Guid clinicId,
        Guid userId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var user = await context.Users.SingleOrDefaultAsync(
            item => item.Id == userId && item.ClinicId == clinicId,
            cancellationToken);
        if (user is null)
        {
            return SettingsOperationResult<bool>.NotFound();
        }

        user.MustChangePin = true;
        await context.SaveChangesAsync(cancellationToken);
        await AuditUserActionAsync("PinChangeForced", clinicId, userId, actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<bool>.Success(true);
    }

    public async Task<SettingsOperationResult<bool>> ResetMfaAsync(
        Guid clinicId,
        Guid userId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var userExists = await context.Users.AnyAsync(
            item => item.Id == userId && item.ClinicId == clinicId,
            cancellationToken);
        if (!userExists)
        {
            return SettingsOperationResult<bool>.NotFound();
        }

        var credential = await context.UserMfaCredentials
            .SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (credential is not null)
        {
            credential.IsActive = false;
            credential.EncryptedSecret = string.Empty;
            credential.ResetAtUtc = DateTime.UtcNow;
            credential.ResetByUserId = actorUserId;
            var recoveryCodes = context.UserMfaRecoveryCodes.Where(item => item.UserMfaCredentialId == credential.Id);
            context.UserMfaRecoveryCodes.RemoveRange(recoveryCodes);
            await context.SaveChangesAsync(cancellationToken);
        }

        await AuditUserActionAsync("MfaReset", clinicId, userId, actorUserId, correlationId, cancellationToken);
        return SettingsOperationResult<bool>.Success(true);
    }

    private async Task AuditUserActionAsync(
        string eventType,
        Guid clinicId,
        Guid targetUserId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = eventType,
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = nameof(User),
            EntityId = targetUserId,
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId,
                ["targetUserId"] = targetUserId
            }
        }, cancellationToken);
    }

    private static Dictionary<string, string[]> Validate(UpdateSecurityPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.MinimumPinLength is < 8 or > 12)
        {
            errors["minimumPinLength"] = ["Minimum PIN length must be between 8 and 12 digits."];
        }

        if (request.SessionInactivityMinutes is < 5 or > 60)
        {
            errors["sessionInactivityMinutes"] = ["Session inactivity timeout must be between 5 and 60 minutes."];
        }

        if (request.MfaEnforcementMode != MfaEnforcementMode.Off && request.MfaEffectiveAtUtc is null)
        {
            errors["mfaEffectiveAtUtc"] = ["An effective date is required when MFA rollout or enforcement is enabled."];
        }

        return errors;
    }

    private static SecurityPolicyDto CanonicalDefaults() => new(
        MfaEnforcementMode.Off,
        null,
        true,
        8,
        15,
        true,
        false,
        AuthorizationRolloutMode.Static,
        true,
        0);

    private static SecurityPolicyDto Map(ClinicSecurityPolicy policy) => new(
        policy.MfaEnforcementMode,
        policy.MfaEffectiveAtUtc,
        policy.RequirePinChangeOnFirstLogin,
        policy.MinimumPinLength,
        policy.SessionInactivityMinutes,
        policy.AllowRoleCustomization,
        policy.RestrictCliniciansToOwnSchedules,
        policy.AuthorizationMode,
        true,
        policy.Version);
}
