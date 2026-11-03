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

    [Fact]
    public async Task InactiveOwnerAccount_DoesNotAllowRemovingLastUsableOwner()
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        member.Role = OrganizationRole.Owner;
        fixture.Staff.IsActive = false;
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

    [Fact]
    public async Task OrganizationAdministrator_CannotGrantPrivilegedClinicRoles()
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = await fixture.Db.OrganizationMemberships.SingleAsync(item => item.UserId == fixture.Staff.Id);
        member.Role = OrganizationRole.Administrator;
        await fixture.Db.SaveChangesAsync();
        var service = fixture.For(fixture.Staff.Id);
        await Assert.ThrowsAsync<OrganizationAccessException>(() => service.SetClinicMembershipAsync(
            fixture.OrganizationId, fixture.SecondClinic.Id, fixture.Staff.Id, new(Roles.Admin, true, 0)));
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
        var invitation = await fixture.Service.InviteAsync(fixture.OrganizationId, outsider.Email);
        var stored = await fixture.Db.OrganizationInvitations.SingleAsync();
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
