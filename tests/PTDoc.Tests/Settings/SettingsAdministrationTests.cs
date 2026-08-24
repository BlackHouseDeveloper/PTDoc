using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Api.Auth;
using PTDoc.Api.Appointments;
using PTDoc.Api.Security;
using PTDoc.Api.Settings;
using PTDoc.Application.Communication;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Communication;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Settings;
using Xunit;

namespace PTDoc.Tests.Settings;

[Trait("Category", "CoreCi")]
public sealed class SettingsAdministrationTests
{
    [Fact]
    public void CanonicalPermissionCatalog_HasStableCompleteMatrixAndRecoveryLocks()
    {
        Assert.Equal(30, RolePermissionCatalog.Capabilities.Count);
        Assert.Equal(9, RolePermissionCatalog.Roles.Count);
        Assert.Equal(
            Enumerable.Range(1, 30),
            RolePermissionCatalog.Capabilities.Select(capability => (int)capability.Key));

        Assert.True(RolePermissionCatalog.FindRole("Owner")!.IsReadOnly);
        Assert.Equal(PermissionLevel.Full,
            RolePermissionCatalog.GetLockedMinimum("Admin", CapabilityKey.UsersManage));
        Assert.Equal(PermissionLevel.Full,
            RolePermissionCatalog.GetLockedMinimum("Admin", CapabilityKey.RolesPermissionsManage));
        Assert.Equal(PermissionLevel.Full,
            RolePermissionCatalog.GetLockedMinimum("Admin", CapabilityKey.ClinicSettingsManage));
        Assert.Equal(PermissionLevel.None,
            RolePermissionCatalog.GetCanonicalLevel("Patient", CapabilityKey.ClinicalNotesView));
    }

    [Theory]
    [InlineData("mfa", true)]
    [InlineData("[\"pwd\",\"mfa\"]", true)]
    [InlineData("pwd", false)]
    [InlineData("c1", false)]
    public void ExternalMfaAssurance_AcceptsOnlyExplicitVerifiedMfaMethod(string amr, bool expected)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("amr", amr)], "test"));

        Assert.Equal(expected, ExternalMfaAssuranceMiddleware.HasVerifiedMfaMethod(principal));
    }

    [Fact]
    public void SettingsSecretProtector_NullEnvelopeFailsClosed()
    {
        var provider = new EphemeralDataProtectionProvider();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var protector = new DataProtectionSettingsSecretProtector(provider, time);
        var protectedNull = provider
            .CreateProtector("PTDoc.Settings.Security.v1", "challenge")
            .Protect("null");

        var succeeded = protector.TryUnprotect(
            "challenge",
            protectedNull,
            TimeSpan.FromMinutes(5),
            out var plaintext);

        Assert.False(succeeded);
        Assert.Empty(plaintext);
    }

    [Fact]
    public async Task DynamicCapabilityAuthorization_PropagatesRequestCancellation()
    {
        var clinicId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var httpContext = new DefaultHttpContext
        {
            RequestAborted = cancellation.Token
        };
        var tenantContext = new Mock<ITenantContextAccessor>();
        tenantContext.Setup(accessor => accessor.GetCurrentClinicId()).Returns(clinicId);
        var evaluator = new Mock<IPermissionEvaluator>();
        evaluator.Setup(service => service.EvaluateAsync(
                clinicId,
                Roles.Admin,
                CapabilityKey.ClinicSettingsManage,
                PermissionLevel.View,
                true,
                cancellation.Token))
            .ReturnsAsync(new PermissionEvaluation(true, true, true, AuthorizationRolloutMode.Enforced, "allowed"));
        var requirement = new DynamicCapabilityRequirement(
            [CapabilityKey.ClinicSettingsManage],
            PermissionLevel.View,
            [Roles.Admin]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, Roles.Admin)],
            "test"));
        var authorizationContext = new AuthorizationHandlerContext(
            [requirement],
            principal,
            httpContext);

        await new DynamicCapabilityAuthorizationHandler(tenantContext.Object, evaluator.Object)
            .HandleAsync(authorizationContext);

        Assert.True(authorizationContext.HasSucceeded);
        evaluator.VerifyAll();
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.Owner, true)]
    [InlineData(Roles.Billing, false)]
    public async Task ClientCapabilityAuthorization_PreservesStaticRoleDecision(
        string role,
        bool expectedAllowed)
    {
        var requirement = new DynamicCapabilityRequirement(
            [CapabilityKey.ClinicSettingsManage],
            PermissionLevel.View,
            [Roles.Admin, Roles.Owner]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role)],
            "test"));
        var authorizationContext = new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource: null);

        await new ClientStaticCapabilityAuthorizationHandler().HandleAsync(authorizationContext);

        Assert.Equal(expectedAllowed, authorizationContext.HasSucceeded);
    }

    [Fact]
    public async Task NewClinic_IsSeededWithVersionedTenantSettings()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Seeded Clinic", Slug = $"seed-{Guid.NewGuid():N}" };

        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        Assert.Equal("America/Los_Angeles", clinic.TimeZoneId);
        Assert.Equal(12, await context.VisitTypes.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Equal(7, await context.ClinicBusinessHours.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Equal(270, await context.RoleCapabilityPermissions.CountAsync(item => item.ClinicId == clinic.Id));
        Assert.Single(await context.ClinicSecurityPolicies.Where(item => item.ClinicId == clinic.Id).ToListAsync());
        Assert.Single(await context.SchedulingPreferences.Where(item => item.ClinicId == clinic.Id).ToListAsync());
        Assert.Single(await context.AutoCheckInPolicies.Where(item => item.ClinicId == clinic.Id).ToListAsync());

        var unsupported = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id && item.RoleKey == "PT" &&
            item.CapabilityKey == CapabilityKey.StaffMessagesSend);
        Assert.Equal(PermissionLevel.None, unsupported.Level);
    }

    [Fact]
    public async Task RoleAdministration_RejectsOwnerMutationAndLockedAdminReduction()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Permissions Clinic", Slug = $"permissions-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new RolePermissionAdministrationService(context, CreateAuditService().Object);

        var ownerResult = await service.UpdateAsync(
            clinic.Id,
            "Owner",
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.ClinicSettingsManage, PermissionLevel.Full, 1)]),
            Guid.NewGuid(),
            "owner-read-only");
        var adminResult = await service.UpdateAsync(
            clinic.Id,
            "Admin",
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.UsersManage, PermissionLevel.View, 1)]),
            Guid.NewGuid(),
            "admin-recovery-lock");
        var settingsRecoveryResult = await service.UpdateAsync(
            clinic.Id,
            "Admin",
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.ClinicSettingsManage, PermissionLevel.Edit, 1)]),
            Guid.NewGuid(),
            "settings-recovery-lock");

        Assert.Equal(SettingsOperationStatus.Forbidden, ownerResult.Status);
        Assert.Equal("role_read_only", ownerResult.ErrorCode);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, adminResult.Status);
        Assert.Contains("permissions.UsersManage", adminResult.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, settingsRecoveryResult.Status);
        Assert.Contains("permissions.ClinicSettingsManage", settingsRecoveryResult.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task RoleAdministration_RejectsNullPermissionPayloadMembers()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Null Permissions Clinic", Slug = $"null-permissions-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new RolePermissionAdministrationService(context, CreateAuditService().Object);

        var nullUpdates = await service.UpdateAsync(
            clinic.Id,
            Roles.PT,
            new UpdateRolePermissionsRequest(null!),
            Guid.NewGuid(),
            "null-permission-updates");
        var nullUpdate = await service.UpdateAsync(
            clinic.Id,
            Roles.PT,
            new UpdateRolePermissionsRequest([null!]),
            Guid.NewGuid(),
            "null-permission-update");
        var nullSource = await service.CloneAsync(
            clinic.Id,
            Roles.PT,
            new CloneRolePermissionsRequest(null!, []),
            Guid.NewGuid(),
            "null-clone-source");
        var nullExpectation = await service.CloneAsync(
            clinic.Id,
            Roles.PT,
            new CloneRolePermissionsRequest(Roles.PTA, [null!]),
            Guid.NewGuid(),
            "null-clone-expectation");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullUpdates.Status);
        Assert.Contains("permissions", nullUpdates.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullUpdate.Status);
        Assert.Contains("permissions", nullUpdate.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullSource.Status);
        Assert.Contains("sourceRoleKey", nullSource.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullExpectation.Status);
        Assert.Contains("targetPermissions", nullExpectation.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task RolePermissions_ClampPersistedAdminRecoveryLevelToLockedMinimum()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Recovery Clamp Clinic", Slug = $"recovery-clamp-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var persisted = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id
            && item.RoleKey == Roles.Admin
            && item.CapabilityKey == CapabilityKey.ClinicSettingsManage);
        persisted.Level = PermissionLevel.Edit;
        persisted.LockedMinimum = PermissionLevel.None;
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.AuthorizationMode = AuthorizationRolloutMode.Enforced;
        await context.SaveChangesAsync();

        var administration = new RolePermissionAdministrationService(context, CreateAuditService().Object);
        var response = await administration.GetAsync(clinic.Id);
        var evaluator = new PermissionEvaluator(context, NullLogger<PermissionEvaluator>.Instance);
        var evaluation = await evaluator.EvaluateAsync(
            clinic.Id,
            Roles.Admin,
            CapabilityKey.ClinicSettingsManage,
            PermissionLevel.Full,
            staticAllowed: false);

        var admin = response.Roles.Single(item => item.RoleKey == Roles.Admin);
        var recoveryPermission = admin.Permissions.Single(item =>
            item.CapabilityKey == CapabilityKey.ClinicSettingsManage);
        Assert.Equal(PermissionLevel.Full, recoveryPermission.Level);
        Assert.Equal(PermissionLevel.Full, recoveryPermission.LockedMinimum);
        Assert.True(evaluation.DynamicAllowed);
        Assert.True(evaluation.EffectiveAllowed);
    }

    [Fact]
    public async Task SchedulingReadScope_RestrictsOwnOnlyCapabilityEvenWhenClinicSwitchIsOff()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Own Capability Clinic", Slug = $"own-capability-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.AuthorizationMode = AuthorizationRolloutMode.Enforced;
        policy.RestrictCliniciansToOwnSchedules = false;
        var allSchedules = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id
            && item.RoleKey == Roles.PT
            && item.CapabilityKey == CapabilityKey.ScheduleViewAll);
        allSchedules.Level = PermissionLevel.None;
        await context.SaveChangesAsync();
        var clinicianId = Guid.NewGuid();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(accessor => accessor.GetCurrentUserRole()).Returns(Roles.PT);
        identity.Setup(accessor => accessor.GetCurrentUserId()).Returns(clinicianId);
        var evaluator = new PermissionEvaluator(context, NullLogger<PermissionEvaluator>.Instance);

        var ownOnly = await AppointmentEndpoints.GetRestrictedReadClinicianIdAsync(
            context, clinic.Id, identity.Object, evaluator, CancellationToken.None);
        allSchedules.Level = PermissionLevel.View;
        await context.SaveChangesAsync();
        var all = await AppointmentEndpoints.GetRestrictedReadClinicianIdAsync(
            context, clinic.Id, identity.Object, evaluator, CancellationToken.None);

        Assert.Equal(clinicianId, ownOnly);
        Assert.Null(all);
    }

    [Fact]
    public async Task PermissionEvaluator_InvalidPersistedLevelFallsBackToCanonicalBaseline()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Invalid Level Clinic", Slug = $"invalid-level-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.AuthorizationMode = AuthorizationRolloutMode.Enforced;
        var persisted = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id
            && item.RoleKey == Roles.PT
            && item.CapabilityKey == CapabilityKey.RolesPermissionsManage);
        persisted.Level = (PermissionLevel)999;
        await context.SaveChangesAsync();
        var evaluator = new PermissionEvaluator(context, NullLogger<PermissionEvaluator>.Instance);
        var administration = new RolePermissionAdministrationService(context, CreateAuditService().Object);

        var evaluation = await evaluator.EvaluateAsync(
            clinic.Id,
            Roles.PT,
            CapabilityKey.RolesPermissionsManage,
            PermissionLevel.View,
            staticAllowed: false);
        var response = await administration.GetAsync(clinic.Id);
        var displayed = response.Roles.Single(item => item.RoleKey == Roles.PT).Permissions.Single(item =>
            item.CapabilityKey == CapabilityKey.RolesPermissionsManage);

        Assert.False(evaluation.DynamicAllowed);
        Assert.False(evaluation.EffectiveAllowed);
        Assert.Equal(PermissionLevel.None, displayed.Level);
    }

    [Fact]
    public async Task RoleClone_RejectsStaleTargetPermissionVersions()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Clone Conflict Clinic", Slug = $"clone-conflict-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new RolePermissionAdministrationService(context, CreateAuditService().Object);
        var matrix = await service.GetAsync(clinic.Id);
        var target = matrix.Roles.Single(item => item.RoleKey == Roles.PT);
        var expectations = target.Permissions
            .Where(item => item.IsSupported && item.LockedMinimum == PermissionLevel.None)
            .Select(item => new PermissionVersionExpectation(item.CapabilityKey, item.Version))
            .ToArray();
        var concurrent = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id
            && item.RoleKey == Roles.PT
            && item.CapabilityKey == CapabilityKey.AppointmentsCreate);
        concurrent.Level = PermissionLevel.Full;
        concurrent.Version++;
        await context.SaveChangesAsync();

        var result = await service.CloneAsync(
            clinic.Id,
            Roles.PT,
            new CloneRolePermissionsRequest(Roles.PTA, expectations),
            Guid.NewGuid(),
            "stale-clone");

        Assert.Equal(SettingsOperationStatus.Conflict, result.Status);
        Assert.Equal(PermissionLevel.Full, concurrent.Level);
    }

    [Fact]
    public async Task SettingsAdministration_RejectsUndefinedPolicyAndPermissionEnums()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Enum Validation Clinic", Slug = $"enum-validation-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var security = new SecurityPolicyAdministrationService(context, CreateAuditService().Object);
        var securityResult = await security.UpdateAsync(
            clinic.Id,
            new UpdateSecurityPolicyRequest(
                (MfaEnforcementMode)999,
                null,
                true,
                8,
                15,
                true,
                false,
                (AuthorizationRolloutMode)999,
                1),
            Guid.NewGuid(),
            "undefined-security-enums");

        var roles = new RolePermissionAdministrationService(context, CreateAuditService().Object);
        var permissionResult = await roles.UpdateAsync(
            clinic.Id,
            Roles.PT,
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.AppointmentsCreate, (PermissionLevel)999, 1)]),
            Guid.NewGuid(),
            "undefined-permission-level");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, securityResult.Status);
        Assert.Contains("mfaEnforcementMode", securityResult.ValidationErrors!.Keys);
        Assert.Contains("authorizationMode", securityResult.ValidationErrors.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, permissionResult.Status);
        Assert.Contains("permissions.AppointmentsCreate", permissionResult.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task RoleAdministration_RejectsUpdatesAndClonesWhenCustomizationIsDisabled()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Fixed Roles Clinic", Slug = $"fixed-roles-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.AllowRoleCustomization = false;
        await context.SaveChangesAsync();
        var audit = CreateAuditService();
        var service = new RolePermissionAdministrationService(context, audit.Object);

        var update = await service.UpdateAsync(
            clinic.Id,
            Roles.PT,
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.AppointmentsCreate, PermissionLevel.Full, 1)]),
            Guid.NewGuid(),
            "customization-disabled-update");
        var clone = await service.CloneAsync(
            clinic.Id,
            Roles.PT,
            new CloneRolePermissionsRequest(Roles.PTA, []),
            Guid.NewGuid(),
            "customization-disabled-clone");

        Assert.Equal(SettingsOperationStatus.Forbidden, update.Status);
        Assert.Equal("role_customization_disabled", update.ErrorCode);
        Assert.Equal(SettingsOperationStatus.Forbidden, clone.Status);
        Assert.Equal("role_customization_disabled", clone.ErrorCode);
        audit.Verify(service => service.LogSettingsEventAsync(
            It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RoleAdministration_AuditFailureDoesNotPersistMutation()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Atomic Audit Clinic", Slug = $"atomic-audit-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogSettingsEventAsync(
                It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var service = new RolePermissionAdministrationService(context, audit.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            clinic.Id,
            Roles.PT,
            new UpdateRolePermissionsRequest(
                [new PermissionUpdate(CapabilityKey.AppointmentsCreate, PermissionLevel.Full, 1)]),
            Guid.NewGuid(),
            "atomic-audit"));

        context.ChangeTracker.Clear();
        var persisted = await context.RoleCapabilityPermissions.SingleAsync(item =>
            item.ClinicId == clinic.Id && item.RoleKey == Roles.PT &&
            item.CapabilityKey == CapabilityKey.AppointmentsCreate);
        Assert.Equal(PermissionLevel.Edit, persisted.Level);
        Assert.Equal(1, persisted.Version);
    }

    [Fact]
    public async Task ClinicHours_RejectUndefinedWeekdayValues()
    {
        await using var context = CreateContext();
        var service = new SchedulingAdministrationService(context, CreateAuditService().Object);
        var invalidHours = Enumerable.Range(7, 7)
            .Select(value => new SaveClinicBusinessHourRequest(
                (DayOfWeek)value,
                false,
                null,
                null,
                null,
                null,
                1))
            .ToArray();

        var result = await service.UpdateClinicHoursAsync(
            Guid.NewGuid(),
            new UpdateClinicHoursRequest("America/Los_Angeles", 1, invalidHours),
            Guid.NewGuid(),
            "invalid-weekdays");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("hours", Assert.IsAssignableFrom<IReadOnlyDictionary<string, string[]>>(result.ValidationErrors));
    }

    [Fact]
    public async Task ClinicHours_RejectNullCollectionAndRows()
    {
        await using var context = CreateContext();
        var service = new SchedulingAdministrationService(context, CreateAuditService().Object);

        var nullCollection = await service.UpdateClinicHoursAsync(
            Guid.NewGuid(),
            new UpdateClinicHoursRequest("America/Los_Angeles", 1, null!),
            Guid.NewGuid(),
            "null-hours");
        var nullRow = await service.UpdateClinicHoursAsync(
            Guid.NewGuid(),
            new UpdateClinicHoursRequest(
                "America/Los_Angeles",
                1,
                Enumerable.Range(0, 7)
                    .Select(day => day == 3
                        ? null!
                        : new SaveClinicBusinessHourRequest((DayOfWeek)day, false, null, null, null, null, 1))
                    .ToArray()),
            Guid.NewGuid(),
            "null-hour-row");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullCollection.Status);
        Assert.Contains("hours", nullCollection.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullRow.Status);
        Assert.Contains("hours", nullRow.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task SchedulingPolicy_UsesClinicIanaTimeZoneAcrossDstTransition()
    {
        await using var context = CreateContext();
        var clinic = new Clinic
        {
            Name = "Eastern Clinic",
            Slug = $"eastern-{Guid.NewGuid():N}",
            TimeZoneId = "America/New_York"
        };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sunday = await context.ClinicBusinessHours.SingleAsync(item =>
            item.ClinicId == clinic.Id && item.DayOfWeek == DayOfWeek.Sunday);
        sunday.IsOpen = true;
        sunday.StartLocalTime = new TimeOnly(1, 0);
        sunday.EndLocalTime = new TimeOnly(4, 0);
        sunday.LunchStartLocalTime = null;
        sunday.LunchEndLocalTime = null;
        (await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id))
            .AppointmentBufferMinutes = 0;
        var clinicianId = Guid.NewGuid();
        context.Appointments.Add(new Appointment
        {
            PatientId = Guid.NewGuid(),
            ClinicId = clinic.Id,
            ClinicalId = clinicianId,
            StartTimeUtc = new DateTime(2026, 3, 8, 6, 45, 0, DateTimeKind.Utc),
            EndTimeUtc = new DateTime(2026, 3, 8, 7, 15, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.NoShow,
            LastModifiedUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = Guid.NewGuid()
        });
        await context.SaveChangesAsync();

        var service = new SchedulingPolicyEvaluator(context);
        var result = await service.EvaluateAsync(new AvailabilityRequest(
            clinic.Id,
            clinicianId,
            new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc)));

        Assert.True(result.IsAvailable);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public async Task AppointmentListDateRange_UsesClinicLocalDayBoundaries()
    {
        await using var context = CreateContext();
        var clinic = new Clinic
        {
            Name = "Local Date Clinic",
            Slug = $"local-date-{Guid.NewGuid():N}",
            TimeZoneId = "America/Los_Angeles"
        };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var range = await AppointmentEndpoints.BuildUtcDateRangeAsync(
            context,
            clinic.Id,
            new DateTime(2026, 7, 5),
            new DateTime(2026, 7, 5),
            CancellationToken.None);

        Assert.True(range.HasValue);
        var actualRange = range.GetValueOrDefault();
        Assert.Equal(new DateTime(2026, 7, 5, 7, 0, 0, DateTimeKind.Utc), actualRange.StartUtc);
        Assert.Equal(new DateTime(2026, 7, 6, 7, 0, 0, DateTimeKind.Utc), actualRange.EndExclusiveUtc);
    }

    [Fact]
    public async Task OwnSchedulePolicy_AllowsOnlyAuthenticatedClinician()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Own Schedule Clinic", Slug = $"own-schedule-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.RestrictCliniciansToOwnSchedules = true;
        await context.SaveChangesAsync();
        var clinicianId = Guid.NewGuid();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(accessor => accessor.GetCurrentUserRole()).Returns(Roles.PT);
        identity.Setup(accessor => accessor.GetCurrentUserId()).Returns(clinicianId);

        var own = await AppointmentEndpoints.CanAccessClinicianScheduleAsync(
            context, clinic.Id, clinicianId, identity.Object, CancellationToken.None);
        var other = await AppointmentEndpoints.CanAccessClinicianScheduleAsync(
            context, clinic.Id, Guid.NewGuid(), identity.Object, CancellationToken.None);

        Assert.True(own);
        Assert.False(other);
    }

    [Fact]
    public async Task TotpEnrollment_IssuesRecoveryCodesAndRejectsReplay()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Clinic", Slug = $"mfa-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Admin",
            Role = "Admin",
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.Clinics.Add(clinic);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var service = new MfaAuthenticationService(context, new TestSecretProtector(), CreateAuditService().Object, time);
        var enrollmentLoginChallenge = service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment);
        var start = await service.BeginEnrollmentAsync(enrollmentLoginChallenge);
        Assert.True(start.Succeeded);

        var enrollmentCode = ComputeTotp(DecodeBase32(start.Value!.ManualKey), time.GetUtcNow());
        var completion = await service.VerifyEnrollmentAsync(start.Value.EnrollmentChallengeToken, enrollmentCode);
        Assert.True(completion.Succeeded);
        Assert.Equal(10, completion.Value!.RecoveryCodes.Count);
        Assert.Equal(10, await context.UserMfaRecoveryCodes.CountAsync());

        time.Advance(TimeSpan.FromSeconds(30));
        var verificationChallenge = service.CreateChallenge(user.Id, MfaChallengePurpose.Verification);
        var verificationCode = ComputeTotp(DecodeBase32(start.Value.ManualKey), time.GetUtcNow());
        var first = await service.VerifyAsync(verificationChallenge, verificationCode);
        var replay = await service.VerifyAsync(verificationChallenge, verificationCode);

        Assert.True(first.Succeeded);
        Assert.False(replay.Succeeded);
        Assert.Equal("invalid_code", replay.ErrorCode);
    }

    [Fact]
    public async Task TotpRecoveryCodeRegeneration_InvalidatesPriorSet()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Recovery Clinic", Slug = $"mfa-recovery-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-recovery-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Recovery",
            Role = "Admin",
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();

        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var service = new MfaAuthenticationService(context, new TestSecretProtector(), CreateAuditService().Object, time);
        var start = await service.BeginEnrollmentAsync(service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment));
        var secret = DecodeBase32(start.Value!.ManualKey);
        var completion = await service.VerifyEnrollmentAsync(
            start.Value.EnrollmentChallengeToken,
            ComputeTotp(secret, time.GetUtcNow()));
        var priorRecoveryCode = completion.Value!.RecoveryCodes[0];

        time.Advance(TimeSpan.FromSeconds(30));
        var regenerated = await service.RegenerateRecoveryCodesAsync(
            user.Id,
            ComputeTotp(secret, time.GetUtcNow()));

        Assert.True(regenerated.Succeeded);
        Assert.Equal(10, regenerated.Value!.RecoveryCodes.Count);
        Assert.Equal(10, await context.UserMfaRecoveryCodes.CountAsync());

        var priorResult = await service.RecoverAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Verification),
            priorRecoveryCode);
        var newResult = await service.RecoverAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Verification),
            regenerated.Value.RecoveryCodes[0]);

        Assert.False(priorResult.Succeeded);
        Assert.True(newResult.Succeeded);
    }

    [Fact]
    public async Task TotpRecoveryCode_RelationalClaimAllowsExactlyOneUseAcrossContexts()
    {
        var connectionString = $"Data Source=mfa-claim-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        const string recoveryCode = "ABCD-EF01-2345";
        Guid userId;

        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var clinic = new Clinic { Name = "MFA Claim Clinic", Slug = $"mfa-claim-{Guid.NewGuid():N}" };
            var user = new User
            {
                Username = "mfa-claim-user",
                PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
                FirstName = "Mfa",
                LastName = "Claim",
                Role = Roles.Admin,
                ClinicId = clinic.Id,
                IsActive = true
            };
            var credential = new UserMfaCredential
            {
                User = user,
                UserId = user.Id,
                EncryptedSecret = "unused-for-recovery",
                IsActive = true,
                ActivatedAtUtc = time.GetUtcNow().UtcDateTime
            };
            seed.AddRange(
                clinic,
                user,
                credential,
                new UserMfaRecoveryCode
                {
                    Credential = credential,
                    UserMfaCredentialId = credential.Id,
                    CodeHash = BCrypt.Net.BCrypt.HashPassword("ABCDEF012345", 4),
                    CreatedAtUtc = time.GetUtcNow().UtcDateTime
                });
            userId = user.Id;
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var protector = new TestSecretProtector();
        var firstService = new MfaAuthenticationService(
            firstContext, protector, CreateAuditService().Object, time);
        var secondService = new MfaAuthenticationService(
            secondContext, protector, CreateAuditService().Object, time);

        var first = await firstService.RecoverAsync(
            firstService.CreateChallenge(userId, MfaChallengePurpose.Verification), recoveryCode);
        var second = await secondService.RecoverAsync(
            secondService.CreateChallenge(userId, MfaChallengePurpose.Verification), recoveryCode);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        await using var assertionContext = new ApplicationDbContext(options);
        Assert.NotNull(await assertionContext.UserMfaRecoveryCodes
            .Select(item => item.UsedAtUtc)
            .SingleAsync());
    }

    [Fact]
    public async Task TotpRecovery_NullCodeReturnsGenericInvalidResult()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Null Recovery Clinic", Slug = $"null-recovery-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "null-recovery-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Null",
            LastName = "Recovery",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true
        };
        var protector = new TestSecretProtector();
        var credential = new UserMfaCredential
        {
            User = user,
            UserId = user.Id,
            EncryptedSecret = protector.Protect("totp-secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(20))),
            IsActive = true
        };
        context.AddRange(clinic, user, credential);
        await context.SaveChangesAsync();
        var service = new MfaAuthenticationService(
            context,
            protector,
            CreateAuthAuditService().Object,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero)));

        var result = await service.RecoverAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Verification),
            null!);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_code", result.ErrorCode);
        Assert.Equal(1, credential.FailedAttemptCount);
    }

    [Fact]
    public async Task TotpEnrollment_RejectsValidCodeDuringFailureLockout()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Lock Clinic", Slug = $"mfa-lock-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-lock-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Lock",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var service = new MfaAuthenticationService(
            context, new TestSecretProtector(), CreateAuditService().Object, time);
        var start = await service.BeginEnrollmentAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment));
        var validCode = ComputeTotp(DecodeBase32(start.Value!.ManualKey), time.GetUtcNow());
        var invalidCode = validCode == "000000" ? "000001" : "000000";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failure = await service.VerifyEnrollmentAsync(
                start.Value.EnrollmentChallengeToken, invalidCode);
            Assert.False(failure.Succeeded);
        }

        var locked = await service.VerifyEnrollmentAsync(
            start.Value.EnrollmentChallengeToken, validCode);
        var credentialBeforeRestart = await context.UserMfaCredentials.SingleAsync();
        var encryptedSecretBeforeRestart = credentialBeforeRestart.EncryptedSecret;
        var lockedUntilBeforeRestart = credentialBeforeRestart.LockedUntilUtc;
        var restarted = await service.BeginEnrollmentAsync(
            service.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment));

        Assert.Equal(SettingsOperationStatus.Forbidden, locked.Status);
        Assert.Equal("mfa_temporarily_locked", locked.ErrorCode);
        Assert.Equal(SettingsOperationStatus.Forbidden, restarted.Status);
        Assert.Equal("mfa_temporarily_locked", restarted.ErrorCode);
        var credentialAfterRestart = await context.UserMfaCredentials.SingleAsync();
        Assert.False(credentialAfterRestart.IsActive);
        Assert.Equal(encryptedSecretBeforeRestart, credentialAfterRestart.EncryptedSecret);
        Assert.Equal(lockedUntilBeforeRestart, credentialAfterRestart.LockedUntilUtc);
    }

    [Fact]
    public async Task TotpFailures_UseLatestPersistedCountAcrossStaleContexts()
    {
        var connectionString = $"Data Source=mfa-failure-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var protector = new TestSecretProtector();
        var secret = RandomNumberGenerator.GetBytes(20);
        Guid userId;

        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            var clinic = new Clinic { Name = "MFA Failure Clinic", Slug = $"mfa-failure-{Guid.NewGuid():N}" };
            var user = new User
            {
                Username = "mfa-failure-admin",
                PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
                FirstName = "Mfa",
                LastName = "Failure",
                Role = Roles.Admin,
                ClinicId = clinic.Id,
                IsActive = true
            };
            seed.AddRange(
                clinic,
                user,
                new UserMfaCredential
                {
                    User = user,
                    UserId = user.Id,
                    EncryptedSecret = protector.Protect("totp-secret", Convert.ToBase64String(secret)),
                    IsActive = true,
                    LastAcceptedTimeStep = -1,
                    FailedAttemptCount = 3
                });
            userId = user.Id;
            await seed.SaveChangesAsync();
        }

        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        await firstContext.UserMfaCredentials.SingleAsync();
        await secondContext.UserMfaCredentials.SingleAsync();
        var firstService = new MfaAuthenticationService(firstContext, protector, CreateAuditService().Object, time);
        var secondService = new MfaAuthenticationService(secondContext, protector, CreateAuditService().Object, time);
        var validCode = ComputeTotp(secret, time.GetUtcNow());
        var invalidCode = validCode == "000000" ? "000001" : "000000";

        await firstService.VerifyAsync(
            firstService.CreateChallenge(userId, MfaChallengePurpose.Verification),
            invalidCode);
        await secondService.VerifyAsync(
            secondService.CreateChallenge(userId, MfaChallengePurpose.Verification),
            invalidCode);

        await using var assertionContext = new ApplicationDbContext(options);
        var credential = await assertionContext.UserMfaCredentials.SingleAsync();
        Assert.Equal(0, credential.FailedAttemptCount);
        Assert.True(credential.LockedUntilUtc > time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task AuthenticationCompletionToken_IsSingleUseAndBoundToActiveCredential()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "MFA Completion Clinic", Slug = $"mfa-completion-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "mfa-completion-admin",
            PinHash = BCrypt.Net.BCrypt.HashPassword("12345678"),
            FirstName = "Mfa",
            LastName = "Completion",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var audit = CreateAuthAuditService();
        var mfa = new MfaAuthenticationService(context, new TestSecretProtector(), audit.Object, time);
        var start = await mfa.BeginEnrollmentAsync(mfa.CreateChallenge(user.Id, MfaChallengePurpose.Enrollment));
        var completion = await mfa.VerifyEnrollmentAsync(
            start.Value!.EnrollmentChallengeToken,
            ComputeTotp(DecodeBase32(start.Value.ManualKey), time.GetUtcNow()));
        var auth = new AuthService(context, NullLogger<AuthService>.Instance, audit.Object, mfa, time);

        var first = await auth.CompleteMfaAsync(completion.Value!.CompletionToken);
        var replay = await auth.CompleteMfaAsync(completion.Value.CompletionToken);

        Assert.Equal(AuthStatus.Success, first!.Status);
        Assert.Null(replay);
        Assert.Single(await context.Sessions.Where(item => !item.IsRevoked).ToListAsync());
        Assert.Empty(await context.Sessions.Where(item =>
            item.IsRevoked && item.RevokedAt == null && item.LastActivityAt == null).ToListAsync());
    }

    [Fact]
    public async Task PinChangeChallenge_CannotReplayAfterSuccessfulChange()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "PIN Replay Clinic", Slug = $"pin-replay-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "pin-replay-admin",
            PinHash = AuthService.HashPin("12345678"),
            FirstName = "Pin",
            LastName = "Replay",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true,
            MustChangePin = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.MinimumPinLength = 10;
        await context.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var audit = CreateAuthAuditService();
        var mfa = new MfaAuthenticationService(context, new TestSecretProtector(), audit.Object, time);
        var auth = new AuthService(context, NullLogger<AuthService>.Instance, audit.Object, mfa, time);
        var login = await auth.AuthenticateAsync(user.Username, "12345678");
        Assert.Equal(AuthStatus.RequiresPinChange, login!.Status);
        Assert.Equal(10, login.MinimumPinLength);
        var challenge = login.ChallengeToken!;

        var rejected = await auth.CompletePinChangeAsync(challenge, "123456789");
        var accepted = await auth.CompletePinChangeAsync(challenge, "1234567890");
        var replay = await auth.CompletePinChangeAsync(challenge, "0987654321");

        Assert.Equal(AuthStatus.RequiresPinChange, rejected!.Status);
        Assert.Equal(10, rejected.MinimumPinLength);
        Assert.Equal(AuthStatus.Success, accepted!.Status);
        Assert.Null(replay);
        Assert.True(BCrypt.Net.BCrypt.Verify("1234567890", user.PinHash));
    }

    [Fact]
    public async Task ExpiredLegacyPinGrace_PersistsRequiredChangeForNextRequest()
    {
        var connectionString = $"Data Source=pin-grace-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var audit = CreateAuthAuditService();
        string challenge;
        Guid userId;

        await using (var loginContext = new ApplicationDbContext(options))
        {
            await loginContext.Database.EnsureCreatedAsync();
            var clinic = new Clinic { Name = "Expired PIN Clinic", Slug = $"expired-pin-{Guid.NewGuid():N}" };
            var user = new User
            {
                Username = "expired-pin-admin",
                PinHash = AuthService.HashPin("1234"),
                FirstName = "Expired",
                LastName = "Pin",
                Role = Roles.Admin,
                ClinicId = clinic.Id,
                IsActive = true,
                LegacyPinGraceEndsAtUtc = time.GetUtcNow().UtcDateTime.AddMinutes(-1)
            };
            loginContext.AddRange(clinic, user);
            await loginContext.SaveChangesAsync();
            userId = user.Id;
            var mfa = new MfaAuthenticationService(loginContext, new TestSecretProtector(), audit.Object, time);
            var auth = new AuthService(loginContext, NullLogger<AuthService>.Instance, audit.Object, mfa, time);

            var result = await auth.AuthenticateAsync(user.Username, "1234");

            Assert.Equal(AuthStatus.RequiresPinChange, result!.Status);
            challenge = result.ChallengeToken!;
        }

        await using (var changeContext = new ApplicationDbContext(options))
        {
            Assert.True(await changeContext.Users.IgnoreQueryFilters()
                .Where(item => item.Id == userId)
                .Select(item => item.MustChangePin)
                .SingleAsync());
            var mfa = new MfaAuthenticationService(changeContext, new TestSecretProtector(), audit.Object, time);
            var auth = new AuthService(changeContext, NullLogger<AuthService>.Instance, audit.Object, mfa, time);

            var result = await auth.CompletePinChangeAsync(challenge, "12345678");

            Assert.Equal(AuthStatus.Success, result!.Status);
        }
    }

    [Fact]
    public async Task DormantLegacyPin_UsesClinicRolloutCutoffInsteadOfNextLogin()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 9, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Dormant PIN Clinic", Slug = $"dormant-pin-{Guid.NewGuid():N}" };
        var user = new User
        {
            Username = "dormant-pin-admin",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Dormant",
            LastName = "Pin",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();
        var policy = await context.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.CreatedAtUtc = now.UtcDateTime.AddDays(-30);
        await context.SaveChangesAsync();
        var audit = CreateAuthAuditService();
        var time = new MutableTimeProvider(now);
        var mfa = new MfaAuthenticationService(context, new TestSecretProtector(), audit.Object, time);
        var auth = new AuthService(context, NullLogger<AuthService>.Instance, audit.Object, mfa, time);

        var result = await auth.AuthenticateAsync(user.Username, "1234");

        Assert.Equal(AuthStatus.RequiresPinChange, result!.Status);
        Assert.True(user.MustChangePin);
        Assert.Equal(policy.CreatedAtUtc.AddDays(14), user.LegacyPinGraceEndsAtUtc);
    }

    [Fact]
    public async Task ScheduleBlockValidation_RejectsUndefinedWeekdayBits()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Weekday Validation Clinic", Slug = $"weekday-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new SchedulingAdministrationService(context, CreateAuditService().Object);
        var request = new SaveScheduleBlockRequest(
            null,
            "Invalid weekdays",
            "administrative",
            (WeekdayFlags)128,
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            new DateOnly(2026, 9, 20),
            null,
            true,
            true);

        var result = await service.CreateScheduleBlockAsync(
            clinic.Id, request, Guid.NewGuid(), "invalid-weekday-bits");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, result.Status);
        Assert.Contains("weekdays", result.ValidationErrors!.Keys);
        Assert.Empty(await context.ScheduleBlockRules.ToListAsync());
    }

    [Fact]
    public async Task PinChangeAuditFailure_RollsBackCredentialMutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var clinic = new Clinic { Name = "PIN Audit Clinic", Slug = $"pin-audit-{Guid.NewGuid():N}" };
        var originalHash = AuthService.HashPin("12345678");
        var user = new User
        {
            Username = "pin-audit-admin",
            PinHash = originalHash,
            FirstName = "Pin",
            LastName = "Audit",
            Role = Roles.Admin,
            ClinicId = clinic.Id,
            IsActive = true,
            MustChangePin = true
        };
        context.AddRange(clinic, user);
        await context.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero));
        var audit = CreateAuthAuditService();
        audit.Setup(service => service.LogAuthEventAsync(
                It.Is<AuditEvent>(item => item.EventType == "PinChanged"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit unavailable"));
        var mfa = new MfaAuthenticationService(context, new TestSecretProtector(), audit.Object, time);
        var auth = new AuthService(context, NullLogger<AuthService>.Instance, audit.Object, mfa, time);

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.CompletePinChangeAsync(
            mfa.CreateChallenge(user.Id, MfaChallengePurpose.PinChange),
            "1234567890"));

        context.ChangeTracker.Clear();
        var persisted = await context.Users.IgnoreQueryFilters().SingleAsync(item => item.Id == user.Id);
        Assert.Equal(originalHash, persisted.PinHash);
        Assert.True(persisted.MustChangePin);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_QueuesConsentedChannelsOnlyOnce()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Reminder Clinic", Slug = $"reminder-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Reminder",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = "reminder@example.invalid",
            Phone = "+15555550100",
            ConsentSigned = true,
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid(),
            LastModifiedUtc = now.UtcDateTime
        };
        var appointment = new Appointment
        {
            PatientId = patient.Id,
            Patient = patient,
            ClinicalId = Guid.NewGuid(),
            ClinicId = clinic.Id,
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = new IntakeForm
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email,
                CommunicationTextConsent = false,
                CommunicationPhoneNumber = patient.Phone
            }),
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(clinic, patient, appointment, intake);
        await context.SaveChangesAsync();

        AppointmentReminderDeliveryRequest? deliveredRequest = null;
        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AppointmentReminderDeliveryRequest, CancellationToken>((request, _) => deliveredRequest = request)
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        communication.Setup(service => service.SendAppointmentReminderSmsAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();
        await processor.ProcessDueAsync();

        var dispatches = await context.AppointmentReminderDispatches.ToListAsync();
        Assert.Single(dispatches);
        Assert.All(dispatches, dispatch => Assert.Equal(ReminderDispatchStatus.Sent, dispatch.Status));
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        Assert.Contains(zone.DaylightName, deliveredRequest!.AppointmentLocalTime, StringComparison.Ordinal);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        communication.Verify(service => service.SendAppointmentReminderSmsAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AutoCheckInProcessor_QueuesMaximumLeadWithoutCompletedIntake()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Auto Check-In Clinic", Slug = $"auto-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var visitType = await context.VisitTypes.FirstAsync(item =>
            item.ClinicId == clinic.Id && item.RequiresIntake && item.IsActive);
        var scheduling = await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id);
        scheduling.SendAppointmentReminders = false;
        var policy = await context.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.IsEnabled = true;
        policy.LeadHours = 168;
        policy.EnableEmail = true;
        policy.EnableSms = false;
        policy.EligibleVisitTypeIdsJson = JsonSerializer.Serialize(new[] { visitType.Id });

        var eligiblePatient = CreateConsentedPatient(clinic.Id, "eligible@example.invalid", now.UtcDateTime);
        var completedPatient = CreateConsentedPatient(clinic.Id, "completed@example.invalid", now.UtcDateTime);
        var eligibleAppointment = CreateIntakeAppointment(clinic.Id, eligiblePatient, visitType, now.UtcDateTime.AddHours(167));
        var completedAppointment = CreateIntakeAppointment(clinic.Id, completedPatient, visitType, now.UtcDateTime.AddHours(167));
        var historicalCompletedIntake = CreateConsentedIntake(
            clinic.Id,
            eligiblePatient,
            now.UtcDateTime.AddDays(-1),
            submittedAt: now.UtcDateTime.AddDays(-1));
        var eligibleIntake = CreateConsentedIntake(clinic.Id, eligiblePatient, now.UtcDateTime, submittedAt: null);
        var completedIntake = CreateConsentedIntake(clinic.Id, completedPatient, now.UtcDateTime, submittedAt: now.UtcDateTime);
        context.AddRange(
            eligiblePatient,
            completedPatient,
            eligibleAppointment,
            completedAppointment,
            historicalCompletedIntake,
            eligibleIntake,
            completedIntake);
        await context.SaveChangesAsync();

        IntakeSendInviteRequest? deliveredRequest = null;
        var intakeWorkflow = new Mock<IIntakeCommunicationWorkflow>();
        intakeWorkflow.Setup(service => service.SendInviteAsync(
                It.IsAny<IntakeSendInviteRequest>(),
                It.IsAny<IntakeCommunicationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSendInviteRequest request, IntakeCommunicationContext? _, CancellationToken _) =>
            {
                deliveredRequest = request;
                return new IntakeDeliverySendResult
                {
                    Success = true,
                    IntakeId = request.IntakeId,
                    PatientId = eligiblePatient.Id,
                    Channel = request.Channel
                };
            });
        var processor = new AppointmentCommunicationProcessor(
            context,
            Mock.Of<ICommunicationService>(),
            intakeWorkflow.Object,
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(eligibleAppointment.Id, dispatch.AppointmentId);
        Assert.Equal(ReminderDispatchPurpose.AutoCheckIn, dispatch.Purpose);
        Assert.Equal(ReminderDispatchStatus.Sent, dispatch.Status);
        Assert.DoesNotContain(
            await context.AppointmentReminderDispatches.ToListAsync(),
            item => item.AppointmentId == completedAppointment.Id);
        Assert.Equal(AutoCheckInTemplateCatalog.Default, deliveredRequest!.TemplateKey);
        intakeWorkflow.Verify(service => service.SendInviteAsync(
            It.IsAny<IntakeSendInviteRequest>(),
            It.IsAny<IntakeCommunicationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AutoCheckInProcessor_ExceptionHonorsClinicMaximumAttempts()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Auto Check-In Retry Clinic", Slug = $"auto-retry-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var visitType = await context.VisitTypes.FirstAsync(item =>
            item.ClinicId == clinic.Id && item.RequiresIntake && item.IsActive);
        (await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id))
            .SendAppointmentReminders = false;
        var policy = await context.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.IsEnabled = true;
        policy.LeadHours = 24;
        policy.EnableEmail = true;
        policy.EnableSms = false;
        policy.MaxAttempts = 1;
        policy.EligibleVisitTypeIdsJson = JsonSerializer.Serialize(new[] { visitType.Id });
        var patient = CreateConsentedPatient(clinic.Id, "auto-retry@example.invalid", now.UtcDateTime);
        var appointment = CreateIntakeAppointment(clinic.Id, patient, visitType, now.UtcDateTime.AddHours(24));
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        context.AddRange(patient, appointment, intake);
        await context.SaveChangesAsync();
        var intakeWorkflow = new Mock<IIntakeCommunicationWorkflow>();
        intakeWorkflow.Setup(service => service.SendInviteAsync(
                It.IsAny<IntakeSendInviteRequest>(),
                It.IsAny<IntakeCommunicationContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider unavailable"));
        var processor = new AppointmentCommunicationProcessor(
            context,
            Mock.Of<ICommunicationService>(),
            intakeWorkflow.Object,
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(ReminderDispatchStatus.DeadLetter, dispatch.Status);
        Assert.Equal(1, dispatch.AttemptCount);
        Assert.Equal("delivery_exception", dispatch.LastStatusCode);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_SuppressesRetryWhenReminderPolicyIsDisabled()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        var clinic = new Clinic { Name = "Disabled Reminder Clinic", Slug = $"disabled-reminder-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var preference = await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id);
        preference.SendAppointmentReminders = true;
        preference.ReminderLeadHours = 24;
        var patient = CreateConsentedPatient(clinic.Id, "disabled-reminder@example.invalid", now.UtcDateTime);
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(
            patient,
            appointment,
            CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null));
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = false, Status = DeliveryStatus.Failed });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            time,
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();
        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(ReminderDispatchStatus.RetryScheduled, dispatch.Status);
        preference.SendAppointmentReminders = false;
        await context.SaveChangesAsync();
        time.Advance(TimeSpan.FromMinutes(6));

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal("reminder_policy_changed", dispatch.LastStatusCode);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, "auto_check_in_policy_changed")]
    [InlineData(false, "intake_completed")]
    public async Task AutoCheckInProcessor_SuppressesRetryWhenPolicyOrLatestIntakeBecomesIneligible(
        bool disablePolicy,
        string expectedReason)
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        var clinic = new Clinic { Name = "Completed Retry Intake Clinic", Slug = $"completed-retry-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var visitType = await context.VisitTypes.FirstAsync(item =>
            item.ClinicId == clinic.Id && item.RequiresIntake && item.IsActive);
        (await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id))
            .SendAppointmentReminders = false;
        var policy = await context.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id);
        policy.IsEnabled = true;
        policy.LeadHours = 24;
        policy.EnableEmail = true;
        policy.EnableSms = false;
        policy.MaxAttempts = 3;
        policy.EligibleVisitTypeIdsJson = JsonSerializer.Serialize(new[] { visitType.Id });
        var patient = CreateConsentedPatient(clinic.Id, "completed-retry@example.invalid", now.UtcDateTime);
        var appointment = CreateIntakeAppointment(clinic.Id, patient, visitType, now.UtcDateTime.AddHours(24));
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        context.AddRange(patient, appointment, intake);
        await context.SaveChangesAsync();
        var intakeWorkflow = new Mock<IIntakeCommunicationWorkflow>();
        intakeWorkflow.Setup(service => service.SendInviteAsync(
                It.IsAny<IntakeSendInviteRequest>(),
                It.IsAny<IntakeCommunicationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IntakeSendInviteRequest request, IntakeCommunicationContext? _, CancellationToken _) =>
                new IntakeDeliverySendResult
                {
                    Success = false,
                    IntakeId = request.IntakeId,
                    PatientId = patient.Id,
                    Channel = request.Channel
                });
        var processor = new AppointmentCommunicationProcessor(
            context,
            Mock.Of<ICommunicationService>(),
            intakeWorkflow.Object,
            time,
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();
        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(ReminderDispatchStatus.RetryScheduled, dispatch.Status);
        if (disablePolicy)
        {
            policy.IsEnabled = false;
        }
        else
        {
            intake.SubmittedAt = now.UtcDateTime.AddMinutes(1);
            intake.LastModifiedUtc = now.UtcDateTime.AddMinutes(1);
        }
        await context.SaveChangesAsync();
        time.Advance(TimeSpan.FromMinutes(6));

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal(expectedReason, dispatch.LastStatusCode);
        intakeWorkflow.Verify(service => service.SendInviteAsync(
            It.IsAny<IntakeSendInviteRequest>(),
            It.IsAny<IntakeCommunicationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_DoesNotRetryProviderAcceptedAuditFailure()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Accepted Reminder Clinic", Slug = $"accepted-reminder-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var scheduling = await context.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id);
        scheduling.SendAppointmentReminders = true;
        scheduling.ReminderLeadHours = 24;
        var patient = CreateConsentedPatient(clinic.Id, "accepted@example.invalid", now.UtcDateTime);
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        context.AddRange(patient, appointment, intake);
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DeliveryAcceptedAuditException(
                new DeliveryResult
                {
                    Succeeded = true,
                    Status = DeliveryStatus.Sent,
                    Provider = "TestProvider",
                    ProviderMessageId = "accepted-1"
                },
                new InvalidOperationException("audit unavailable")));
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();
        await processor.ProcessDueAsync();

        var dispatch = await context.AppointmentReminderDispatches.SingleAsync();
        Assert.Equal(ReminderDispatchStatus.Sent, dispatch.Status);
        Assert.Equal(1, dispatch.AttemptCount);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_DeadLettersInterruptedUnknownDelivery()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var dispatch = new AppointmentReminderDispatch
        {
            ClinicId = Guid.NewGuid(),
            AppointmentId = Guid.NewGuid(),
            AppointmentVersionUtc = now.UtcDateTime,
            Purpose = ReminderDispatchPurpose.AppointmentReminder,
            Channel = ReminderChannel.Email,
            IdempotencyKey = $"interrupted:{Guid.NewGuid():N}",
            Status = ReminderDispatchStatus.Processing,
            AttemptCount = 1,
            EligibleAtUtc = now.UtcDateTime.AddMinutes(-20),
            NextAttemptAtUtc = now.UtcDateTime.AddMinutes(-20),
            CreatedAtUtc = now.UtcDateTime.AddMinutes(-20),
            UpdatedAtUtc = now.UtcDateTime.AddMinutes(-11)
        };
        context.AppointmentReminderDispatches.Add(dispatch);
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.DeadLetter, dispatch.Status);
        Assert.Equal("delivery_outcome_unknown", dispatch.LastStatusCode);
        Assert.Equal(1, dispatch.AttemptCount);
        communication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AppointmentReminderProcessor_QueuesCandidatesBeyondFirstPage()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Paged Reminder Clinic", Slug = $"paged-{Guid.NewGuid():N}" };
        var patient = CreateConsentedPatient(clinic.Id, "paged@example.invalid", now.UtcDateTime);
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        context.AddRange(clinic, patient, intake);
        for (var index = 0; index < 501; index++)
        {
            context.Appointments.Add(new Appointment
            {
                Patient = patient,
                PatientId = patient.Id,
                ClinicId = clinic.Id,
                ClinicalId = Guid.NewGuid(),
                StartTimeUtc = now.UtcDateTime.AddHours(23).AddSeconds(index),
                EndTimeUtc = now.UtcDateTime.AddHours(24).AddSeconds(index),
                Status = AppointmentStatus.Scheduled,
                LastModifiedUtc = now.UtcDateTime,
                ModifiedByUserId = Guid.NewGuid()
            });
        }
        await context.SaveChangesAsync();

        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(501, await context.AppointmentReminderDispatches.CountAsync());
    }

    [Fact]
    public async Task AppointmentReminderProcessor_AtomicallyClaimsDispatchAcrossWorkers()
    {
        var databaseName = $"settings-claims-{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        Guid dispatchId;

        await using (var seedContext = new ApplicationDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var clinic = new Clinic { Name = "Claim Clinic", Slug = $"claim-{Guid.NewGuid():N}" };
            seedContext.Clinics.Add(clinic);
            await seedContext.SaveChangesAsync();
            var preference = await seedContext.SchedulingPreferences.SingleAsync(item => item.ClinicId == clinic.Id);
            preference.SendAppointmentReminders = true;
            preference.ReminderLeadHours = 24;
            (await seedContext.AutoCheckInPolicies.SingleAsync(item => item.ClinicId == clinic.Id))
                .IsEnabled = false;
            var patient = CreateConsentedPatient(clinic.Id, "claim@example.invalid", now.UtcDateTime);
            var appointment = new Appointment
            {
                Patient = patient,
                PatientId = patient.Id,
                Clinic = clinic,
                ClinicId = clinic.Id,
                ClinicalId = Guid.NewGuid(),
                StartTimeUtc = now.UtcDateTime.AddHours(24),
                EndTimeUtc = now.UtcDateTime.AddHours(25),
                Status = AppointmentStatus.Scheduled,
                LastModifiedUtc = now.UtcDateTime,
                ModifiedByUserId = Guid.NewGuid()
            };
            var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
            var dispatch = new AppointmentReminderDispatch
            {
                ClinicId = clinic.Id,
                Appointment = appointment,
                AppointmentId = appointment.Id,
                AppointmentVersionUtc = appointment.LastModifiedUtc,
                Purpose = ReminderDispatchPurpose.AppointmentReminder,
                Channel = ReminderChannel.Email,
                ReminderLeadHours = 24,
                IdempotencyKey = $"{ReminderDispatchPurpose.AppointmentReminder}:{appointment.Id:N}:{appointment.LastModifiedUtc.Ticks}:24:{ReminderChannel.Email}",
                Status = ReminderDispatchStatus.Pending,
                EligibleAtUtc = now.UtcDateTime,
                NextAttemptAtUtc = now.UtcDateTime,
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime
            };
            dispatchId = dispatch.Id;
            seedContext.AddRange(patient, appointment, intake, dispatch);
            await seedContext.SaveChangesAsync();
        }

        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        await using var firstContext = new ApplicationDbContext(options);
        await using var secondContext = new ApplicationDbContext(options);
        var first = new AppointmentCommunicationProcessor(
            firstContext,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);
        var second = new AppointmentCommunicationProcessor(
            secondContext,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await Task.WhenAll(first.ProcessDueAsync(), second.ProcessDueAsync());

        await using var assertionContext = new ApplicationDbContext(options);
        var saved = await assertionContext.AppointmentReminderDispatches.SingleAsync(item => item.Id == dispatchId);
        Assert.Equal(ReminderDispatchStatus.Sent, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_SuppressesDispatchAfterChannelConsentIsRevoked()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Revocation Clinic", Slug = $"revoke-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Revoked",
            LastName = "Consent",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = "revoked@example.invalid",
            ConsentSigned = true,
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = new IntakeForm
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email,
                RevokedConsentKeys = ["communicationEmailConsent"]
            }),
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var dispatch = new AppointmentReminderDispatch
        {
            ClinicId = clinic.Id,
            Appointment = appointment,
            AppointmentId = appointment.Id,
            AppointmentVersionUtc = appointment.LastModifiedUtc,
            Purpose = ReminderDispatchPurpose.AppointmentReminder,
            Channel = ReminderChannel.Email,
            IdempotencyKey = $"revoked:{appointment.Id:N}",
            Status = ReminderDispatchStatus.Pending,
            EligibleAtUtc = now.UtcDateTime,
            NextAttemptAtUtc = now.UtcDateTime,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        context.AddRange(clinic, patient, appointment, intake, dispatch);
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.Suppressed, dispatch.Status);
        Assert.Equal("communication_consent_unavailable", dispatch.LastStatusCode);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AppointmentReminderProcessor_ReplacesChangedAppointmentDispatchBeforeDelivery()
    {
        await using var context = CreateContext();
        var now = new DateTimeOffset(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);
        var clinic = new Clinic { Name = "Changed Appointment Clinic", Slug = $"changed-{Guid.NewGuid():N}" };
        var patient = CreateConsentedPatient(clinic.Id, "changed@example.invalid", now.UtcDateTime);
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = now.UtcDateTime.AddHours(24),
            EndTimeUtc = now.UtcDateTime.AddHours(25),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = now.UtcDateTime,
            ModifiedByUserId = Guid.NewGuid()
        };
        var intake = CreateConsentedIntake(clinic.Id, patient, now.UtcDateTime, submittedAt: null);
        var dispatch = new AppointmentReminderDispatch
        {
            ClinicId = clinic.Id,
            Appointment = appointment,
            AppointmentId = appointment.Id,
            AppointmentVersionUtc = now.UtcDateTime.AddMinutes(-1),
            Purpose = ReminderDispatchPurpose.AppointmentReminder,
            Channel = ReminderChannel.Email,
            IdempotencyKey = $"changed:{appointment.Id:N}",
            Status = ReminderDispatchStatus.Pending,
            EligibleAtUtc = now.UtcDateTime,
            NextAttemptAtUtc = now.UtcDateTime,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        context.AddRange(clinic, patient, appointment, intake, dispatch);
        await context.SaveChangesAsync();
        var communication = new Mock<ICommunicationService>();
        communication.Setup(service => service.SendAppointmentReminderEmailAsync(
                It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeliveryResult { Succeeded = true, Status = DeliveryStatus.Sent });
        var processor = new AppointmentCommunicationProcessor(
            context,
            communication.Object,
            Mock.Of<IIntakeCommunicationWorkflow>(),
            new MutableTimeProvider(now),
            NullLogger<AppointmentCommunicationProcessor>.Instance);

        await processor.ProcessDueAsync();

        Assert.Equal(ReminderDispatchStatus.Cancelled, dispatch.Status);
        Assert.Equal("appointment_changed", dispatch.LastStatusCode);
        var replacement = await context.AppointmentReminderDispatches.SingleAsync(item => item.Id != dispatch.Id);
        Assert.Equal(appointment.LastModifiedUtc, replacement.AppointmentVersionUtc);
        Assert.Equal(ReminderDispatchStatus.Sent, replacement.Status);
        communication.Verify(service => service.SendAppointmentReminderEmailAsync(
            It.IsAny<AppointmentReminderDeliveryRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KioskService_MalformedAnonymousCredentialsFailClosed()
    {
        await using var context = CreateContext();
        var service = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));

        var nullEnrollment = await service.EnrollAsync(null);
        var blankEnrollment = await service.EnrollAsync(" ");
        var nullDevice = await service.CheckInAsync(null, "12345678");
        var nullAppointment = await service.CheckInAsync($"{Guid.NewGuid():N}.device", null);

        Assert.Equal(SettingsOperationStatus.NotFound, nullEnrollment.Status);
        Assert.Equal(SettingsOperationStatus.NotFound, blankEnrollment.Status);
        Assert.Equal(SettingsOperationStatus.NotFound, nullDevice.Status);
        Assert.Equal(SettingsOperationStatus.NotFound, nullAppointment.Status);
    }

    [Fact]
    public async Task KioskCheckIn_AcceptsAdvertisedNumericCodeAndQrPayload()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Kiosk Clinic", Slug = $"kiosk-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Kiosk",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(clinic, patient, appointment);
        await context.SaveChangesAsync();

        AuditEvent? tokenCreatedAudit = null;
        var audit = CreateAuditService();
        audit.Setup(service => service.LogSettingsEventAsync(
                It.Is<AuditEvent>(item => item.EventType == "KioskCheckInTokenCreated"),
                It.IsAny<CancellationToken>()))
            .Callback<AuditEvent, CancellationToken>((item, _) => tokenCreatedAudit = item)
            .Returns(Task.CompletedTask);
        var service = new KioskCheckInService(
            context,
            audit.Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Front Desk iPad"),
            Guid.NewGuid(),
            "create-station");
        var enrollment = await service.EnrollAsync(station.Value!.Code);

        var numericToken = await service.CreateCheckInTokenAsync(
            clinic.Id, appointment.Id, Guid.NewGuid(), "numeric-token");
        var numericResult = await service.CheckInAsync(
            enrollment.Value!.DeviceCredential,
            numericToken.Value!.NumericCode);
        var numericReplay = await service.CheckInAsync(
            enrollment.Value.DeviceCredential,
            numericToken.Value.NumericCode);

        Assert.True(numericResult.Succeeded);
        Assert.Equal(SettingsOperationStatus.NotFound, numericReplay.Status);
        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.NotNull(tokenCreatedAudit);
        Assert.Equal(nameof(KioskCheckInToken), tokenCreatedAudit.EntityType);
        Assert.NotEqual(appointment.Id, tokenCreatedAudit.EntityId);
        Assert.Equal(appointment.Id, tokenCreatedAudit.Metadata["appointmentId"]);

        var qrToken = await service.CreateCheckInTokenAsync(
            clinic.Id, appointment.Id, Guid.NewGuid(), "qr-token");
        var qrResult = await service.CheckInAsync(
            enrollment.Value.DeviceCredential,
            qrToken.Value!.QrPayload);

        Assert.True(qrResult.Succeeded);
        Assert.Equal(2, await context.KioskCheckInTokens.CountAsync(item => item.ConsumedAtUtc != null));
    }

    [Fact]
    public async Task QuickAdminAdministration_RejectsNullCollectionAndStationNames()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Null Quick Admin Clinic", Slug = $"null-quick-admin-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var autoCheckIn = new AutoCheckInAdministrationService(context, CreateAuditService().Object);
        var autoCheckInResult = await autoCheckIn.UpdateAsync(
            clinic.Id,
            new UpdateAutoCheckInPolicyRequest(
                false,
                24,
                true,
                true,
                AutoCheckInTemplateCatalog.Default,
                3,
                null!,
                1),
            Guid.NewGuid(),
            "null-eligible-visit-types");

        var kiosk = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var nullCreateName = await kiosk.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest(null!),
            Guid.NewGuid(),
            "null-create-name");
        var station = await kiosk.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Valid Station"),
            Guid.NewGuid(),
            "valid-station");
        var nullUpdateName = await kiosk.UpdateStationAsync(
            clinic.Id,
            station.Value!.StationId,
            new UpdateKioskStationRequest(null!, true, 1),
            Guid.NewGuid(),
            "null-update-name");

        Assert.Equal(SettingsOperationStatus.ValidationFailed, autoCheckInResult.Status);
        Assert.Contains("eligibleVisitTypeIds", autoCheckInResult.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullCreateName.Status);
        Assert.Contains("name", nullCreateName.ValidationErrors!.Keys);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, nullUpdateName.Status);
        Assert.Contains("name", nullUpdateName.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task KioskCheckIn_RejectsTokenWhenAppointmentCompletedAfterIssuance()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Completed Kiosk Clinic", Slug = $"completed-kiosk-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Completed",
            LastName = "Kiosk",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = Guid.NewGuid()
        };
        context.AddRange(clinic, patient, appointment);
        await context.SaveChangesAsync();
        var service = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Completed Visit iPad"),
            Guid.NewGuid(),
            "create-completed-station");
        var enrollment = await service.EnrollAsync(station.Value!.Code);
        var token = await service.CreateCheckInTokenAsync(
            clinic.Id, appointment.Id, Guid.NewGuid(), "completed-token");
        appointment.Status = AppointmentStatus.Completed;
        await context.SaveChangesAsync();

        var result = await service.CheckInAsync(
            enrollment.Value!.DeviceCredential,
            token.Value!.NumericCode);

        Assert.Equal(SettingsOperationStatus.ValidationFailed, result.Status);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
        Assert.Null(await context.KioskCheckInTokens
            .Where(item => item.AppointmentId == appointment.Id)
            .Select(item => item.ConsumedAtUtc)
            .SingleAsync());
    }

    [Fact]
    public async Task KioskCheckIn_RequiresNormalizedActivePolicyCopay()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Normalized Copay Clinic", Slug = $"normalized-copay-{Guid.NewGuid():N}" };
        var patient = new Patient
        {
            FirstName = "Normalized",
            LastName = "Copay",
            DateOfBirth = new DateTime(1990, 1, 1),
            PayerInfoJson = "{}",
            ClinicId = clinic.Id,
            ModifiedByUserId = Guid.NewGuid()
        };
        var appointment = new Appointment
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinic.Id,
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = DateTime.UtcNow.AddHours(1),
            EndTimeUtc = DateTime.UtcNow.AddHours(2),
            Status = AppointmentStatus.Scheduled,
            ModifiedByUserId = Guid.NewGuid()
        };
        var insurance = new PatientInsurancePolicy
        {
            Patient = patient,
            PatientId = patient.Id,
            Clinic = clinic,
            ClinicId = clinic.Id,
            CoveragePriority = InsuranceCoveragePriority.Primary,
            Status = InsurancePolicyStatus.Active,
            CopayAmount = 35m,
            ModifiedByUserId = Guid.NewGuid(),
            LastModifiedUtc = DateTime.UtcNow
        };
        context.AddRange(clinic, patient, appointment, insurance);
        await context.SaveChangesAsync();
        var workflow = new AppointmentCheckInWorkflow(context, TimeProvider.System);

        var result = await workflow.CheckInAsync(appointment.Id, clinic.Id);

        Assert.Equal(AppointmentCheckInStatus.PaymentRequired, result.Status);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public async Task KioskRevocation_InvalidatesOutstandingEnrollmentCode()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Revoked Kiosk Clinic", Slug = $"revoked-kiosk-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Revoked iPad"),
            Guid.NewGuid(),
            "create-revoked-station");

        var revoked = await service.RevokeStationAsync(
            clinic.Id,
            station.Value!.StationId,
            1,
            Guid.NewGuid(),
            "revoke-station");
        var enrollment = await service.EnrollAsync(station.Value.Code);

        Assert.True(revoked.Succeeded);
        Assert.Equal(SettingsOperationStatus.NotFound, enrollment.Status);
        Assert.NotNull(await context.KioskEnrollmentCodes
            .Where(item => item.KioskStationId == station.Value.StationId)
            .Select(item => item.ConsumedAtUtc)
            .SingleAsync());
        var storedStation = await context.KioskStations.SingleAsync(
            item => item.Id == station.Value.StationId);
        Assert.False(storedStation.IsActive);
        Assert.NotNull(storedStation.RevokedAtUtc);
    }

    [Fact]
    public async Task KioskRevocation_IsTerminalForActivationAndCredentialRotation()
    {
        await using var context = CreateContext();
        var clinic = new Clinic { Name = "Terminal Kiosk Clinic", Slug = $"terminal-kiosk-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Terminal iPad"),
            Guid.NewGuid(),
            "create-terminal-station");
        var revoked = await service.RevokeStationAsync(
            clinic.Id,
            station.Value!.StationId,
            1,
            Guid.NewGuid(),
            "revoke-terminal-station");

        var activation = await service.UpdateStationAsync(
            clinic.Id,
            station.Value.StationId,
            new UpdateKioskStationRequest("Terminal iPad", true, 2),
            Guid.NewGuid(),
            "reactivate-terminal-station");
        var rotation = await service.RotateEnrollmentAsync(
            clinic.Id,
            station.Value.StationId,
            2,
            Guid.NewGuid(),
            "rotate-terminal-station");

        Assert.True(revoked.Succeeded);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, activation.Status);
        Assert.Equal(SettingsOperationStatus.ValidationFailed, rotation.Status);
        var stored = await context.KioskStations.SingleAsync(item => item.Id == station.Value.StationId);
        Assert.False(stored.IsActive);
        Assert.NotNull(stored.RevokedAtUtc);
        Assert.Equal("revoked", stored.DeviceCredentialHash);
    }

    [Fact]
    public void AutoCheckInDraftIdentity_IsStablePerAppointment()
    {
        var appointmentId = Guid.NewGuid();

        var firstChannel = AppointmentCommunicationProcessor.CreateAutoCheckInIntakeId(appointmentId);
        var secondChannel = AppointmentCommunicationProcessor.CreateAutoCheckInIntakeId(appointmentId);

        Assert.Equal(firstChannel, secondChannel);
        Assert.NotEqual(firstChannel, AppointmentCommunicationProcessor.CreateAutoCheckInIntakeId(Guid.NewGuid()));
    }

    [Fact]
    public async Task KioskEnrollment_RelationalClaimAllowsExactlyOneUse()
    {
        var connectionString = $"Data Source=kiosk-enroll-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connectionString)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var clinic = new Clinic { Name = "Kiosk Claim Clinic", Slug = $"kiosk-claim-{Guid.NewGuid():N}" };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        var service = new KioskCheckInService(
            context,
            CreateAuditService().Object,
            new AppointmentCheckInWorkflow(context, TimeProvider.System));
        var station = await service.CreateStationAsync(
            clinic.Id,
            new CreateKioskStationRequest("Claimed iPad"),
            Guid.NewGuid(),
            "create-claim-station");

        var first = await service.EnrollAsync(station.Value!.Code);
        var second = await service.EnrollAsync(station.Value.Code);

        Assert.True(first.Succeeded);
        Assert.Equal(SettingsOperationStatus.NotFound, second.Status);
        Assert.NotNull(await context.KioskEnrollmentCodes
            .Select(item => item.ConsumedAtUtc)
            .SingleAsync());
    }

    private static Patient CreateConsentedPatient(Guid clinicId, string email, DateTime now) =>
        new()
        {
            FirstName = "Communication",
            LastName = "Fixture",
            DateOfBirth = new DateTime(1990, 1, 1),
            Email = email,
            ConsentSigned = true,
            ClinicId = clinicId,
            ModifiedByUserId = Guid.NewGuid(),
            LastModifiedUtc = now
        };

    private static Appointment CreateIntakeAppointment(
        Guid clinicId,
        Patient patient,
        VisitType visitType,
        DateTime startTimeUtc) =>
        new()
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinicId,
            ClinicalId = Guid.NewGuid(),
            VisitType = visitType,
            VisitTypeId = visitType.Id,
            StartTimeUtc = startTimeUtc,
            EndTimeUtc = startTimeUtc.AddMinutes(visitType.DurationMinutes),
            Status = AppointmentStatus.Scheduled,
            LastModifiedUtc = startTimeUtc.AddDays(-1),
            ModifiedByUserId = Guid.NewGuid()
        };

    private static IntakeForm CreateConsentedIntake(
        Guid clinicId,
        Patient patient,
        DateTime now,
        DateTime? submittedAt) =>
        new()
        {
            Patient = patient,
            PatientId = patient.Id,
            ClinicId = clinicId,
            TemplateVersion = "1.0",
            AccessToken = "test-token-hash",
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = IntakeConsentJson.Serialize(new IntakeConsentPacket
            {
                CommunicationEmailConsent = true,
                CommunicationEmail = patient.Email
            }),
            SubmittedAt = submittedAt,
            LastModifiedUtc = now,
            ModifiedByUserId = Guid.NewGuid()
        };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<IAuditService> CreateAuditService()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.LogSettingsEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private static Mock<IAuditService> CreateAuthAuditService()
    {
        var audit = CreateAuditService();
        audit.Setup(service => service.LogAuthEventAsync(It.IsAny<AuditEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private static string ComputeTotp(byte[] secret, DateTimeOffset instant)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, instant.ToUnixTimeSeconds() / 30);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | ((hash[offset + 1] & 0xff) << 16)
                     | ((hash[offset + 2] & 0xff) << 8)
                     | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value)
        {
            var index = alphabet.IndexOf(character);
            Assert.True(index >= 0);
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return output.ToArray();
    }

    private sealed class TestSecretProtector : ISettingsSecretProtector
    {
        public string Protect(string purpose, string plaintext) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{purpose}\n{plaintext}"));

        public bool TryUnprotect(string purpose, string protectedValue, TimeSpan maximumAge, out string plaintext)
        {
            try
            {
                var value = Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
                var prefix = $"{purpose}\n";
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    plaintext = value[prefix.Length..];
                    return true;
                }
            }
            catch (FormatException)
            {
                // Invalid protected values fail closed.
            }

            plaintext = string.Empty;
            return false;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;
        public override DateTimeOffset GetUtcNow() => _value;
        public void Advance(TimeSpan duration) => _value += duration;
    }
}
