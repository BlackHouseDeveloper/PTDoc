using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Claims;
using PTDoc.Api.Auth;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Settings;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Tenancy;

[Trait("Category", "Tenancy")]
public sealed class OrganizationMembershipTests
{
    [Theory]
    [InlineData("missing", true, false)]
    [InlineData("legacy", true, true)]
    [InlineData("legacy", false, false)]
    [InlineData("inactive-legacy", true, false)]
    public async Task ClinicRole_LegacyFallbackRequiresAnExistingActiveHomeClinic(string state, bool homeMatches, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var clinicId = state == "missing" ? Guid.NewGuid() : fixture.FirstClinic.Id;
        fixture.FirstClinic.OrganizationId = null;
        fixture.FirstClinic.IsActive = state != "inactive-legacy";
        fixture.Staff.ClinicId = homeMatches ? clinicId : fixture.ForeignClinic.Id;
        await fixture.Db.SaveChangesAsync();

        Assert.Equal(allowed ? Roles.PT : null,
            await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, clinicId));
        if (state == "missing")
        {
            var auth = new AuthService(fixture.Db, NullLogger<AuthService>.Instance, Mock.Of<IAuditService>(),
                clinicSignIn: new ClinicSignInContext { ClinicId = clinicId });
            Assert.Null(await auth.AuthenticateAsync(fixture.Staff.Username, "12345678"));
            Assert.Empty(await fixture.Db.Sessions.ToListAsync());
        }
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, "Patient")]
    [InlineData(OrganizationRole.Owner, "patient")]
    [InlineData(OrganizationRole.Administrator, "Patient")]
    [InlineData(OrganizationRole.Administrator, "patient")]
    public async Task RegistrationAdministration_RejectsPatientActorWithRetainedOrganizationAuthority(OrganizationRole role, string patientRole)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Owner.Role = patientRole;
        (await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Owner.Id)).Role = role;
        fixture.Staff.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal("OrganizationMember", await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Owner, Guid.Empty));
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Owner.Id);
        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(item => item.GetCurrentClinicId()).Returns(Guid.Empty);
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object, tenant.Object);

        Assert.Empty((await service.GetPendingRegistrationsAsync(new(null, "All", null, null, null, null, null))).Items);
        Assert.Null(await service.GetPendingRegistrationAsync(fixture.Staff.Id));
        Assert.Equal(RegistrationStatus.NotFound, (await service.UpdatePendingRegistrationAsync(fixture.Staff.Id,
            new("Edited Staff", "edited@example.test", new DateTime(1990, 1, 1), Roles.FrontDesk, null, null), fixture.Owner.Id)).Status);
        Assert.Equal(RegistrationStatus.NotFound, (await service.ApproveRegistrationAsync(fixture.Staff.Id, fixture.Owner.Id)).Status);
        Assert.Equal(RegistrationStatus.NotFound, (await service.RejectRegistrationAsync(fixture.Staff.Id, fixture.Owner.Id)).Status);
        Assert.Equal(RegistrationStatus.NotFound, (await service.HoldRegistrationAsync(fixture.Staff.Id, fixture.Owner.Id)).Status);
        Assert.Equal(RegistrationStatus.NotFound, (await service.CancelRegistrationAsync(fixture.Staff.Id, fixture.Owner.Id)).Status);
        Assert.Equal("staff@example.test", fixture.Staff.Email);
        Assert.Equal(Roles.PT, fixture.Staff.Role);
        Assert.False(fixture.Staff.IsActive);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invitation_RequiresAnExistingActiveStaffAccount(bool exists)
    {
        await using var fixture = await Fixture.CreateAsync();
        const string email = "inactive-invite@example.test";
        if (exists)
        {
            fixture.Db.Users.Add(new User { Username = "inactive-invite", Email = email, Role = Roles.PT, IsActive = false });
            await fixture.Db.SaveChangesAsync();
        }
        var version = (await fixture.Db.Organizations.SingleAsync(item => item.Id == fixture.OrganizationId)).Version;

        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.InviteAsync(fixture.OrganizationId, email));

        Assert.Empty(await fixture.Db.OrganizationInvitations.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        Assert.Equal(version, (await fixture.Db.Organizations.SingleAsync(item => item.Id == fixture.OrganizationId)).Version);
    }

    [Theory]
    [InlineData(OrganizationRole.Owner, null, "target", true, false)]
    [InlineData(OrganizationRole.Administrator, null, "target", true, false)]
    [InlineData(OrganizationRole.Owner, Roles.PT, "target", true, false)]
    [InlineData(OrganizationRole.Administrator, Roles.Admin, "other", true, false)]
    [InlineData(OrganizationRole.Owner, Roles.Owner, "administration-only", true, false)]
    [InlineData(OrganizationRole.Owner, Roles.Owner, "none", true, false)]
    [InlineData(OrganizationRole.Administrator, Roles.Admin, "target", false, false)]
    [InlineData(OrganizationRole.Administrator, Roles.Admin, "target", true, true)]
    [InlineData(OrganizationRole.Owner, Roles.Owner, "target", true, true)]
    public async Task PatientRegistration_RequiresSelectedClinicAdministrativeGrant(
        OrganizationRole organizationRole, string? clinicRole, string selection, bool activeGrant, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id)).Role = organizationRole;
        var patient = new User
        {
            Username = "patient-applicant",
            FirstName = "Patient",
            LastName = "Applicant",
            Email = "patient-applicant@example.test",
            DateOfBirth = new DateTime(1990, 1, 1),
            Role = "patient",
            ClinicId = fixture.SecondClinic.Id,
            IsActive = false
        };
        fixture.Db.Users.Add(patient);
        if (clinicRole is not null)
            fixture.Db.ClinicMemberships.Add(new ClinicMembership
            {
                ClinicId = fixture.SecondClinic.Id,
                UserId = fixture.Staff.Id,
                Role = clinicRole,
                IsActive = activeGrant
            });
        await fixture.Db.SaveChangesAsync();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Staff.Id);
        var tenant = new Mock<ITenantContextAccessor>();
        tenant.Setup(item => item.GetCurrentClinicId()).Returns(selection switch
        {
            "target" => fixture.SecondClinic.Id,
            "other" => fixture.FirstClinic.Id,
            "administration-only" => Guid.Empty,
            _ => (Guid?)null
        });
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object, tenant.Object);

        var page = await service.GetPendingRegistrationsAsync(new(null, "All", null, null, null, null, null));
        Assert.Equal(allowed, page.Items.Any(item => item.Id == patient.Id));
        Assert.Equal(allowed, await service.GetPendingRegistrationAsync(patient.Id) is not null);
        var edit = await service.UpdatePendingRegistrationAsync(patient.Id,
            new("Patient Applicant", "updated-patient@example.test", patient.DateOfBirth,
                allowed ? Roles.Patient : Roles.FrontDesk, null, null), fixture.Staff.Id);
        Assert.Equal(allowed ? RegistrationStatus.Succeeded : RegistrationStatus.NotFound, edit.Status);
        var approval = await service.ApproveRegistrationAsync(patient.Id, fixture.Staff.Id);
        Assert.Equal(allowed ? RegistrationStatus.Succeeded : RegistrationStatus.NotFound, approval.Status);
        Assert.Equal(allowed, patient.IsActive);
        if (!allowed)
        {
            Assert.Equal(RegistrationStatus.NotFound, (await service.RejectRegistrationAsync(patient.Id, fixture.Staff.Id)).Status);
            Assert.Equal(RegistrationStatus.NotFound, (await service.HoldRegistrationAsync(patient.Id, fixture.Staff.Id)).Status);
            Assert.Equal(RegistrationStatus.NotFound, (await service.CancelRegistrationAsync(patient.Id, fixture.Staff.Id)).Status);
            Assert.Equal("patient", patient.Role);
            Assert.Equal("patient-applicant@example.test", patient.Email);
            Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        }
    }

    [Fact]
    public async Task OrganizationOnlyRegistrationAdmin_CannotConvertStaffRegistrationToPatient()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Staff.IsActive = false;
        fixture.Staff.FirstName = "Staff";
        fixture.Staff.LastName = "Applicant";
        await fixture.Db.SaveChangesAsync();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Owner.Id);
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object);

        var result = await service.UpdatePendingRegistrationAsync(fixture.Staff.Id,
            new("Patient Applicant", "converted@example.test", new DateTime(1990, 1, 1), Roles.Patient, null, null), fixture.Owner.Id);

        Assert.Equal(RegistrationStatus.NotFound, result.Status);
        Assert.Equal(Roles.PT, fixture.Staff.Role);
        Assert.Equal("staff@example.test", fixture.Staff.Email);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("Patient")]
    [InlineData("patient")]
    public async Task PatientAccounts_AreExcludedFromStaffAdministrationAndInvitations(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var patient = new User { Username = "portal", Email = "portal@example.test", Role = role, ClinicId = fixture.SecondClinic.Id };
        fixture.Db.Users.Add(patient);
        await fixture.Db.SaveChangesAsync();
        Assert.DoesNotContain(await fixture.Service.GetStaffAsync(fixture.OrganizationId), item => item.UserId == patient.Id);
        var membership = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == patient.Id);
        var grantCount = await fixture.Db.ClinicMemberships.CountAsync();
        var auditCount = await fixture.Db.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.GetStaffClinicsAsync(fixture.OrganizationId, patient.Id));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.FirstClinic.Id, patient.Id, new(Roles.FrontDesk, true, 0)));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.SetOrganizationMembershipAsync(
            fixture.OrganizationId, patient.Id, new(OrganizationRole.Owner, true, membership.Version)));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.InviteAsync(fixture.OrganizationId, patient.Email));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.For(patient.Id).GetOrganizationsAsync());

        Assert.Equal(grantCount, await fixture.Db.ClinicMemberships.CountAsync());
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());
        Assert.Empty(await fixture.Db.OrganizationInvitations.ToListAsync());
        Assert.Equal(OrganizationRole.Member, membership.Role);
        // Keep the portal's existing home grant; removing it would break patient authentication.
        Assert.Equal(role, await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, patient, fixture.SecondClinic.Id));
    }

    [Fact]
    public async Task Invitation_CannotBeAcceptedAfterAccountBecomesPatient()
    {
        await using var fixture = await Fixture.CreateAsync();
        var outsider = new User { Username = "invite-target", Email = "invite-target@example.test", Role = Roles.PT, ClinicId = fixture.ForeignClinic.Id };
        fixture.Db.Users.Add(outsider);
        await fixture.Db.SaveChangesAsync();
        var invitation = await fixture.Service.InviteAsync(fixture.OrganizationId, outsider.Email);
        outsider.Role = Roles.Patient;
        await fixture.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.For(outsider.Id).AcceptInvitationAsync(invitation.Token));

        Assert.Null((await fixture.Db.OrganizationInvitations.SingleAsync()).AcceptedAtUtc);
        Assert.False(await fixture.Db.OrganizationMemberships.AnyAsync(item => item.OrganizationId == fixture.OrganizationId && item.UserId == outsider.Id));
    }

    [Fact]
    public async Task RegistrationAdministrator_CannotEditOrReactivateAnOrganizationOwnerWithOrdinaryHomeRole()
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id)).Role = OrganizationRole.Administrator;
        fixture.Owner.Role = Roles.PT;
        fixture.Owner.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Staff.Id);
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object);

        Assert.Null(await service.GetPendingRegistrationAsync(fixture.Owner.Id));
        var pending = await service.GetPendingRegistrationsAsync(new(null, "Pending", null, null, null, null, null));
        Assert.DoesNotContain(pending.Items, item => item.Id == fixture.Owner.Id);
        Assert.Equal(RegistrationStatus.NotFound, (await service.UpdatePendingRegistrationAsync(fixture.Owner.Id,
            new("Changed Owner", "changed@example.test", new DateTime(1990, 1, 1), Roles.PT, "PT-123", "CA"), fixture.Staff.Id)).Status);
        Assert.Equal(RegistrationStatus.NotFound, (await service.ApproveRegistrationAsync(fixture.Owner.Id, fixture.Staff.Id)).Status);
        Assert.False(fixture.Owner.IsActive);
        Assert.Equal("owner@example.test", fixture.Owner.Email);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        (await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id)).Role = OrganizationRole.Owner;
        await fixture.Db.SaveChangesAsync();
        Assert.NotNull(await service.GetPendingRegistrationAsync(fixture.Owner.Id));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("ADMIN")]
    public async Task RegistrationAdministrator_CannotReviewPrivilegedRoleWithDifferentCasing(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var staff = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        staff.Role = OrganizationRole.Administrator;
        var pending = new User { Username = "privileged-pending", ClinicId = fixture.FirstClinic.Id, Role = role, IsActive = false };
        fixture.Db.Users.Add(pending);
        await fixture.Db.SaveChangesAsync();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Staff.Id);
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object);
        Assert.Null(await service.GetPendingRegistrationAsync(pending.Id));
        Assert.Equal(RegistrationStatus.NotFound, (await service.ApproveRegistrationAsync(pending.Id, fixture.Staff.Id)).Status);
    }

    [Theory]
    [InlineData("/api/v1/patients", false)]
    [InlineData("/api/v1/sync/client/pull", false)]
    [InlineData("/api/v1/organizations", true)]
    [InlineData("/api/v1/admin/registrations/pending", true)]
    public async Task AdministrationOnlySession_CannotEnterClinicalEndpoints(string path, bool allowed)
    {
        await using var fixture = await Fixture.CreateAsync();
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(PTDocClaimTypes.InternalUserId, fixture.Owner.Id.ToString()),
            new Claim(HttpTenantContextAccessor.ClinicIdClaimType, Guid.Empty.ToString()),
            new Claim(ClaimTypes.Role, Roles.Owner)
        }, "test"));
        var resolver = new PrincipalRecordResolver(new HttpContextAccessor { HttpContext = http }, Mock.Of<IServiceProvider>());
        var called = false;
        var guard = new ClinicMembershipGuardMiddleware(_ => { called = true; return Task.CompletedTask; });
        await guard.InvokeAsync(http, resolver, fixture.Db);
        Assert.Equal(allowed, called);
        if (allowed) Assert.Equal("OrganizationMember", http.User.FindFirst(ClaimTypes.Role)!.Value);
        else Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshToken_RechecksSelectedClinicGrantAndRole(bool revoke)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.PTA, true, 0));
        var store = new DbRefreshTokenStore(fixture.Db);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, fixture.Staff.Id.ToString()),
            new Claim(ClaimTypes.Role, Roles.PTA),
            new Claim(HttpTenantContextAccessor.ClinicIdClaimType, fixture.SecondClinic.Id.ToString()),
            new Claim(PTDocClaimTypes.PinCredentialBinding, PinCredentialBinding.Create(fixture.Staff.PinHash))
        };
        await store.StoreAsync("synthetic-refresh", new RefreshTokenRecord(fixture.Staff.Id.ToString(), claims, DateTimeOffset.UtcNow.AddHours(1)), default);
        Assert.NotNull(await store.GetAsync("synthetic-refresh", default));
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id,
            new(revoke ? Roles.PTA : Roles.PT, !revoke, 1));
        Assert.Null(await store.GetAsync("synthetic-refresh", default));
    }

    [Fact]
    public async Task RegistrationAdministration_RequiresLiveOrganizationAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = new User { Username = "foreign-pending", ClinicId = fixture.ForeignClinic.Id, Role = Roles.PT, IsActive = false };
        fixture.Db.Users.Add(pending);
        await fixture.Db.SaveChangesAsync();
        var identity = new Mock<IIdentityContextAccessor>();
        identity.Setup(item => item.TryGetCurrentUserId()).Returns(fixture.Owner.Id);
        var service = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance, identity.Object);
        Assert.Null(await service.GetPendingRegistrationAsync(pending.Id));
        Assert.Equal(RegistrationStatus.NotFound, (await service.ApproveRegistrationAsync(pending.Id, fixture.Owner.Id)).Status);
        var anonymous = new UserRegistrationService(fixture.Db, NullLogger<UserRegistrationService>.Instance);
        Assert.Null(await anonymous.GetPendingRegistrationAsync(fixture.Staff.Id));
        fixture.Owner.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Null(await service.GetPendingRegistrationAsync(fixture.Staff.Id));
    }

    [Theory]
    [InlineData(false, Roles.PT)]
    [InlineData(true, Roles.Patient)]
    public async Task UnusableOwnerAccount_DoesNotAllowRemovingLastUsableOwner(bool active, string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        member.Role = OrganizationRole.Owner;
        fixture.Staff.IsActive = active;
        fixture.Staff.Role = role;
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<OrganizationConflictException>(() => fixture.Service.SetOrganizationMembershipAsync(
            fixture.OrganizationId, fixture.Owner.Id, new(OrganizationRole.Member, true, 1)));
    }

    [Fact]
    public async Task OrganizationOwner_DoesNotInheritSecondClinicAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Owner, fixture.SecondClinic.Id));
        Assert.Equal("OrganizationMember", await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Owner, Guid.Empty));
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Owner.Id,
            new ClinicMembershipChange(Roles.PT, true, 0));
        Assert.Equal(Roles.PT, await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Owner, fixture.SecondClinic.Id));
        Assert.Equal(fixture.FirstClinic.Id, fixture.Owner.ClinicId);
        Assert.Equal(Roles.Owner, fixture.Owner.Role);
    }

    [Fact]
    public async Task ForeignOrganizationAndClinic_AreDeniedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Db.ClinicMemberships.CountAsync();
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.GetStaffAsync(fixture.ForeignClinic.Id));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.Service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.ForeignClinic.Id, fixture.Staff.Id, new(Roles.PT, true, 0)));
        Assert.Equal(before, await fixture.Db.ClinicMemberships.CountAsync());
    }

    [Fact]
    public async Task LastOwnerAndStaleVersion_AreRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<OrganizationConflictException>(() => fixture.Service.SetOrganizationMembershipAsync(
            fixture.OrganizationId, fixture.Owner.Id, new(OrganizationRole.Member, true, 1)));
        await Assert.ThrowsAsync<OrganizationConflictException>(() => fixture.Service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.FirstClinic.Id, fixture.Staff.Id, new(Roles.PTA, true, 0)));
        Assert.Equal(Roles.PT, await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.FirstClinic.Id));
    }

    [Theory]
    [InlineData(Roles.PT)]
    [InlineData(Roles.PTA)]
    [InlineData(Roles.FrontDesk)]
    [InlineData(Roles.Billing)]
    [InlineData(Roles.Aide)]
    [InlineData(Roles.PracticeManager)]
    public async Task OrganizationAdministrator_CanExplicitlyGrantOwnOrdinaryClinicAccess(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        member.Role = OrganizationRole.Administrator;
        await fixture.Db.SaveChangesAsync();
        var service = fixture.For(fixture.Staff.Id);
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.SecondClinic.Id));

        await service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(role, true, 0));

        Assert.Equal(role, await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.SecondClinic.Id));
        Assert.Equal(fixture.FirstClinic.Id, fixture.Staff.ClinicId);
        Assert.Equal(Roles.PT, fixture.Staff.Role);
        Assert.Equal(OrganizationRole.Administrator, member.Role);
        var audit = Assert.Single(await fixture.Db.AuditLogs.ToListAsync());
        Assert.Equal("ClinicMembershipChanged", audit.EventType);
        Assert.Equal(fixture.Staff.Id, audit.UserId);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.ForeignClinic.Id, fixture.Staff.Id, new(role, true, 0)));
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.ForeignClinic.Id));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Owner)]
    public async Task OrganizationAdministrator_CannotGrantPrivilegedClinicRoles(string role)
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        member.Role = OrganizationRole.Administrator;
        await fixture.Db.SaveChangesAsync();
        var service = fixture.For(fixture.Staff.Id);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(role, true, 0)));
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.SetOrganizationMembershipAsync(
            fixture.OrganizationId, fixture.Staff.Id, new(OrganizationRole.Owner, true, member.Version)));
    }

    [Fact]
    public async Task SelectedClinicSession_UsesMembershipRole_AndRevocationInvalidatesIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.PTA, true, 0));
        var signIn = new ClinicSignInContext { ClinicId = fixture.SecondClinic.Id };
        var auth = new AuthService(fixture.Db, NullLogger<AuthService>.Instance, Mock.Of<IAuditService>(), clinicSignIn: signIn);
        var result = Assert.IsType<AuthResult>(await auth.AuthenticateAsync(fixture.Staff.Username, "12345678"));
        Assert.Equal(fixture.SecondClinic.Id, result.ClinicId);
        Assert.Equal(Roles.PTA, result.Role);
        var token = Assert.IsType<string>(result.Token);
        Assert.Equal(fixture.SecondClinic.Id, (await auth.GetCurrentUserAsync(token))!.ClinicId);
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.PTA, false, 1));
        Assert.Null(await auth.ValidateSessionAsync(token));
        Assert.Equal(fixture.FirstClinic.Id, fixture.Staff.ClinicId);
    }

    [Fact]
    public async Task DeactivatedOrganizationMembership_RevokesAllItsClinicGrants()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.PT, true, 0));
        await fixture.Service.SetOrganizationMembershipAsync(fixture.OrganizationId, fixture.Staff.Id, new(OrganizationRole.Member, false, 1));
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.FirstClinic.Id));
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, fixture.Staff, fixture.SecondClinic.Id));
        Assert.Empty(await fixture.For(fixture.Staff.Id).GetMyClinicsAsync());
    }

    [Fact]
    public async Task Invitation_IsHashedEmailBoundSingleUse_AndGrantsNoClinicAccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        var outsider = new User { Username = "invited", Email = "invited@example.test", Role = Roles.PT, IsActive = true, ClinicId = fixture.ForeignClinic.Id };
        fixture.Db.Users.Add(outsider);
        await fixture.Db.SaveChangesAsync();
        var invitation = await fixture.Service.InviteAsync(fixture.OrganizationId, outsider.Email.ToUpperInvariant());
        var stored = await fixture.Db.OrganizationInvitations.SingleAsync();
        Assert.Equal(outsider.Email, stored.Email);
        Assert.NotEqual(invitation.Token, stored.TokenHash);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.For(fixture.Staff.Id).AcceptInvitationAsync(invitation.Token));
        await fixture.For(outsider.Id).AcceptInvitationAsync(invitation.Token);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => fixture.For(outsider.Id).AcceptInvitationAsync(invitation.Token));
        Assert.Null(await ClinicMembershipAccess.ResolveRoleAsync(fixture.Db, outsider, fixture.FirstClinic.Id));
        Assert.DoesNotContain(fixture.Db.AuditLogs, item => item.MetadataJson.Contains(invitation.Token));
    }

    [Fact]
    public async Task TargetClinicPolicy_AndPinChallengeRetainSelectedClinic()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SetClinicMembershipAsync(fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.PT, true, 0));
        var policy = await fixture.Db.ClinicSecurityPolicies.SingleAsync(item => item.ClinicId == fixture.SecondClinic.Id);
        policy.MinimumPinLength = 10;
        await fixture.Db.SaveChangesAsync();
        var signIn = new ClinicSignInContext { ClinicId = fixture.SecondClinic.Id };
        var protector = new TestProtector();
        var mfa = new MfaAuthenticationService(fixture.Db, protector, Mock.Of<IAuditService>(), TimeProvider.System, signIn);
        var auth = new AuthService(fixture.Db, NullLogger<AuthService>.Instance, Mock.Of<IAuditService>(), mfa, clinicSignIn: signIn);
        var result = Assert.IsType<AuthResult>(await auth.AuthenticateAsync(fixture.Staff.Username, "12345678"));
        Assert.Equal(AuthStatus.RequiresPinChange, result.Status);
        Assert.Equal(10, result.MinimumPinLength);
        var nextScope = new ClinicSignInContext();
        var nextMfa = new MfaAuthenticationService(fixture.Db, protector, Mock.Of<IAuditService>(), TimeProvider.System, nextScope);
        Assert.True(nextMfa.TryValidateChallenge(result.ChallengeToken!, MfaChallengePurpose.PinChange, TimeSpan.FromMinutes(5), out var principal));
        Assert.Equal(fixture.SecondClinic.Id, principal.ClinicId);
        var nextAuth = new AuthService(fixture.Db, NullLogger<AuthService>.Instance, Mock.Of<IAuditService>(), nextMfa, clinicSignIn: nextScope);
        var completed = Assert.IsType<AuthResult>(await nextAuth.CompletePinChangeAsync(result.ChallengeToken!, "1234567890"));
        Assert.Equal(fixture.SecondClinic.Id, completed.ClinicId);
        Assert.Equal(fixture.FirstClinic.Id, fixture.Staff.ClinicId);
    }

    private sealed class TestProtector : ISettingsSecretProtector
    {
        private readonly Dictionary<string, string> values = [];
        public string Protect(string purpose, string plaintext)
        {
            var token = Guid.NewGuid().ToString(); values[purpose + token] = plaintext; return token;
        }
        public bool TryUnprotect(string purpose, string protectedValue, TimeSpan maximumAge, out string plaintext)
            => values.TryGetValue(purpose + protectedValue, out plaintext!);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; } = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Clinic FirstClinic { get; } = new() { Name = "Clinic A", Slug = "clinic-a" };
        public Clinic SecondClinic { get; } = new() { Name = "Clinic B", Slug = "clinic-b" };
        public Clinic ForeignClinic { get; } = new() { Name = "Clinic C", Slug = "clinic-c" };
        public User Owner { get; } = new() { Username = "owner", Email = "owner@example.test", Role = Roles.Owner, PinHash = AuthService.HashPin("12345678"), PinChangedAtUtc = DateTime.UtcNow };
        public User Staff { get; } = new() { Username = "staff", Email = "staff@example.test", Role = Roles.PT, PinHash = AuthService.HashPin("12345678"), PinChangedAtUtc = DateTime.UtcNow };
        public Guid OrganizationId => FirstClinic.Id;
        public OrganizationAdministrationService Service => For(Owner.Id);
        public OrganizationAdministrationService For(Guid userId)
        {
            var identity = new Mock<IIdentityContextAccessor>();
            identity.Setup(item => item.TryGetCurrentUserId()).Returns(userId);
            identity.Setup(item => item.GetCurrentUserId()).Returns(userId);
            return new OrganizationAdministrationService(Db, identity.Object, TimeProvider.System);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.SecondClinic.OrganizationId = fixture.OrganizationId;
            fixture.Owner.ClinicId = fixture.FirstClinic.Id; fixture.Staff.ClinicId = fixture.FirstClinic.Id;
            fixture.Db.Clinics.AddRange(fixture.FirstClinic, fixture.SecondClinic, fixture.ForeignClinic);
            fixture.Db.Users.AddRange(fixture.Owner, fixture.Staff);
            await fixture.Db.SaveChangesAsync();
            (await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Owner.Id)).Role = OrganizationRole.Owner;
            foreach (var policy in await fixture.Db.ClinicSecurityPolicies.ToListAsync()) policy.MfaEnforcementMode = MfaEnforcementMode.Off;
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
