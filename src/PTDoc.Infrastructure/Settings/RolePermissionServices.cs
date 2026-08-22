using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

public sealed class RolePermissionAdministrationService(
    ApplicationDbContext context,
    IAuditService auditService) : IRolePermissionAdministrationService
{
    public async Task<RolePermissionsResponse> GetAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var persisted = await context.RoleCapabilityPermissions
            .Where(permission => permission.ClinicId == clinicId)
            .ToListAsync(cancellationToken);

        var mode = await context.ClinicSecurityPolicies
            .Where(policy => policy.ClinicId == clinicId)
            .Select(policy => (AuthorizationRolloutMode?)policy.AuthorizationMode)
            .SingleOrDefaultAsync(cancellationToken)
            ?? AuthorizationRolloutMode.Static;

        return new RolePermissionsResponse(
            RolePermissionCatalog.Roles.Select(role => MapRole(role, persisted)).ToArray(),
            mode);
    }

    public async Task<SettingsOperationResult<RolePermissionSet>> UpdateAsync(
        Guid clinicId,
        string roleKey,
        UpdateRolePermissionsRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var role = RolePermissionCatalog.FindRole(roleKey);
        if (role is null)
        {
            return SettingsOperationResult<RolePermissionSet>.NotFound();
        }

        if (role.IsReadOnly)
        {
            return SettingsOperationResult<RolePermissionSet>.Forbidden("role_read_only");
        }

        var validationErrors = ValidateUpdates(role.Key, request.Permissions);
        if (validationErrors.Count > 0)
        {
            return SettingsOperationResult<RolePermissionSet>.Validation(validationErrors);
        }

        var requestedKeys = request.Permissions.Select(item => item.CapabilityKey).ToArray();
        var existing = await context.RoleCapabilityPermissions
            .Where(permission => permission.ClinicId == clinicId &&
                                 permission.RoleKey == role.Key &&
                                 requestedKeys.Contains(permission.CapabilityKey))
            .ToDictionaryAsync(permission => permission.CapabilityKey, cancellationToken);

        var changes = new List<Dictionary<string, object>>();
        foreach (var update in request.Permissions)
        {
            existing.TryGetValue(update.CapabilityKey, out var permission);
            var currentVersion = permission?.Version ?? 0;
            if (currentVersion != update.ExpectedVersion)
            {
                return SettingsOperationResult<RolePermissionSet>.Conflict();
            }

            var oldLevel = permission?.Level
                ?? RolePermissionCatalog.GetCanonicalLevel(role.Key, update.CapabilityKey);

            if (permission is null)
            {
                permission = new RoleCapabilityPermission
                {
                    ClinicId = clinicId,
                    RoleKey = role.Key,
                    CapabilityKey = update.CapabilityKey,
                    Level = update.Level,
                    LockedMinimum = RolePermissionCatalog.GetLockedMinimum(role.Key, update.CapabilityKey),
                    Version = 1,
                    UpdatedByUserId = actorUserId
                };
                context.RoleCapabilityPermissions.Add(permission);
                existing[update.CapabilityKey] = permission;
            }
            else
            {
                permission.Level = update.Level;
                permission.Version++;
                permission.UpdatedByUserId = actorUserId;
                permission.UpdatedAtUtc = DateTime.UtcNow;
            }

            changes.Add(new Dictionary<string, object>
            {
                ["capabilityKey"] = update.CapabilityKey.ToString(),
                ["oldLevel"] = oldLevel.ToString(),
                ["newLevel"] = update.Level.ToString()
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<RolePermissionSet>.Conflict();
        }

        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = "RolePermissionsUpdated",
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = nameof(RoleCapabilityPermission),
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId,
                ["targetRole"] = role.Key,
                ["changes"] = changes
            }
        }, cancellationToken);

        var all = await context.RoleCapabilityPermissions
            .Where(permission => permission.ClinicId == clinicId && permission.RoleKey == role.Key)
            .ToListAsync(cancellationToken);
        return SettingsOperationResult<RolePermissionSet>.Success(MapRole(role, all));
    }

    public async Task<SettingsOperationResult<RolePermissionSet>> CloneAsync(
        Guid clinicId,
        string targetRoleKey,
        CloneRolePermissionsRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var targetRole = RolePermissionCatalog.FindRole(targetRoleKey);
        var sourceRole = RolePermissionCatalog.FindRole(request.SourceRoleKey);
        if (targetRole is null || sourceRole is null)
        {
            return SettingsOperationResult<RolePermissionSet>.NotFound();
        }

        if (targetRole.IsReadOnly)
        {
            return SettingsOperationResult<RolePermissionSet>.Forbidden("role_read_only");
        }

        if (string.Equals(targetRole.Key, sourceRole.Key, StringComparison.Ordinal))
        {
            return SettingsOperationResult<RolePermissionSet>.Validation(
                new Dictionary<string, string[]> { ["sourceRoleKey"] = ["Source and target roles must differ."] });
        }

        var roleKeys = new[] { targetRole.Key, sourceRole.Key };
        var persisted = await context.RoleCapabilityPermissions
            .Where(permission => permission.ClinicId == clinicId && roleKeys.Contains(permission.RoleKey))
            .ToListAsync(cancellationToken);

        var source = persisted.Where(item => item.RoleKey == sourceRole.Key)
            .ToDictionary(item => item.CapabilityKey);
        var target = persisted.Where(item => item.RoleKey == targetRole.Key)
            .ToDictionary(item => item.CapabilityKey);
        var changedKeys = new List<string>();

        foreach (var definition in RolePermissionCatalog.Capabilities)
        {
            if (!definition.IsSupported ||
                RolePermissionCatalog.GetLockedMinimum(targetRole.Key, definition.Key) > PermissionLevel.None)
            {
                continue;
            }

            var sourceLevel = source.TryGetValue(definition.Key, out var sourcePermission)
                ? sourcePermission.Level
                : RolePermissionCatalog.GetCanonicalLevel(sourceRole.Key, definition.Key);

            if (!target.TryGetValue(definition.Key, out var targetPermission))
            {
                targetPermission = new RoleCapabilityPermission
                {
                    ClinicId = clinicId,
                    RoleKey = targetRole.Key,
                    CapabilityKey = definition.Key,
                    Level = sourceLevel,
                    LockedMinimum = PermissionLevel.None,
                    UpdatedByUserId = actorUserId
                };
                context.RoleCapabilityPermissions.Add(targetPermission);
                target[definition.Key] = targetPermission;
            }
            else
            {
                targetPermission.Level = sourceLevel;
                targetPermission.Version++;
                targetPermission.UpdatedByUserId = actorUserId;
                targetPermission.UpdatedAtUtc = DateTime.UtcNow;
            }

            changedKeys.Add(definition.Key.ToString());
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<RolePermissionSet>.Conflict();
        }

        await auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = "RolePermissionsCloned",
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = nameof(RoleCapabilityPermission),
            Metadata = new Dictionary<string, object>
            {
                ["clinicId"] = clinicId,
                ["sourceRole"] = sourceRole.Key,
                ["targetRole"] = targetRole.Key,
                ["capabilityKeys"] = changedKeys
            }
        }, cancellationToken);

        return SettingsOperationResult<RolePermissionSet>.Success(MapRole(targetRole, target.Values.ToArray()));
    }

    private static Dictionary<string, string[]> ValidateUpdates(string roleKey, IReadOnlyList<PermissionUpdate> updates)
    {
        var errors = new Dictionary<string, string[]>();
        if (updates.Count == 0)
        {
            errors["permissions"] = ["At least one permission update is required."];
            return errors;
        }

        if (updates.GroupBy(item => item.CapabilityKey).Any(group => group.Count() > 1))
        {
            errors["permissions"] = ["A capability can be updated only once per request."];
        }

        foreach (var update in updates)
        {
            var definition = RolePermissionCatalog.Capabilities.FirstOrDefault(item => item.Key == update.CapabilityKey);
            if (definition is null)
            {
                errors[$"permissions.{update.CapabilityKey}"] = ["Unknown capability."];
                continue;
            }

            if (!definition.IsSupported && update.Level != PermissionLevel.None)
            {
                errors[$"permissions.{update.CapabilityKey}"] = ["This capability is not supported by a server endpoint."];
            }

            if (update.Level < RolePermissionCatalog.GetLockedMinimum(roleKey, update.CapabilityKey))
            {
                errors[$"permissions.{update.CapabilityKey}"] = ["The requested level is below the locked system minimum."];
            }
        }

        return errors;
    }

    private static RolePermissionSet MapRole(
        SettingsRoleDefinition role,
        IReadOnlyCollection<RoleCapabilityPermission> persisted)
    {
        var byCapability = persisted
            .Where(item => item.RoleKey == role.Key)
            .ToDictionary(item => item.CapabilityKey);

        var permissions = RolePermissionCatalog.Capabilities.Select(definition =>
        {
            byCapability.TryGetValue(definition.Key, out var item);
            var level = definition.IsSupported
                ? item?.Level ?? RolePermissionCatalog.GetCanonicalLevel(role.Key, definition.Key)
                : PermissionLevel.None;
            return new RolePermissionItem(
                definition.Key,
                definition.Name,
                definition.Description,
                level,
                item?.LockedMinimum ?? RolePermissionCatalog.GetLockedMinimum(role.Key, definition.Key),
                definition.IsSupported,
                item?.Version ?? 0);
        }).ToArray();

        return new RolePermissionSet(
            role.Key,
            role.DisplayName,
            role.IsReadOnly,
            permissions,
            permissions.Count(item => item.Level == PermissionLevel.None),
            permissions.Count(item => item.Level == PermissionLevel.View),
            permissions.Count(item => item.Level == PermissionLevel.Edit),
            permissions.Count(item => item.Level == PermissionLevel.Full));
    }
}

public sealed class PermissionEvaluator(
    ApplicationDbContext context,
    ILogger<PermissionEvaluator> logger) : IPermissionEvaluator
{
    public async Task<PermissionEvaluation> EvaluateAsync(
        Guid clinicId,
        string roleKey,
        CapabilityKey capabilityKey,
        PermissionLevel requiredLevel,
        bool staticAllowed,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = RolePermissionCatalog.NormalizeRole(roleKey);
        var mode = await context.ClinicSecurityPolicies
            .Where(policy => policy.ClinicId == clinicId)
            .Select(policy => (AuthorizationRolloutMode?)policy.AuthorizationMode)
            .SingleOrDefaultAsync(cancellationToken)
            ?? AuthorizationRolloutMode.Static;

        var definition = RolePermissionCatalog.Capabilities.FirstOrDefault(item => item.Key == capabilityKey);
        var configuredLevel = definition?.IsSupported == true
            ? await context.RoleCapabilityPermissions
                .Where(permission => permission.ClinicId == clinicId &&
                                     permission.RoleKey == normalizedRole &&
                                     permission.CapabilityKey == capabilityKey)
                .Select(permission => (PermissionLevel?)permission.Level)
                .SingleOrDefaultAsync(cancellationToken)
                ?? RolePermissionCatalog.GetCanonicalLevel(normalizedRole, capabilityKey)
            : PermissionLevel.None;

        var dynamicAllowed = configuredLevel >= requiredLevel;
        if (mode == AuthorizationRolloutMode.Shadow && dynamicAllowed != staticAllowed)
        {
            logger.LogWarning(
                "Dynamic authorization shadow difference ClinicId={ClinicId} Role={Role} Capability={Capability} RequiredLevel={RequiredLevel} StaticAllowed={StaticAllowed} DynamicAllowed={DynamicAllowed}",
                clinicId,
                normalizedRole,
                capabilityKey,
                requiredLevel,
                staticAllowed,
                dynamicAllowed);
        }

        var effectiveAllowed = mode == AuthorizationRolloutMode.Enforced ? dynamicAllowed : staticAllowed;
        return new PermissionEvaluation(
            staticAllowed,
            dynamicAllowed,
            effectiveAllowed,
            mode,
            effectiveAllowed ? "allowed" : "insufficient_capability");
    }
}
