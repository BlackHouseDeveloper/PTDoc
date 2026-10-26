using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public class PrincipalProvisioningResolverTests
{
    [Theory]
    [InlineData("SessionToken")]
    [InlineData("Cookies")]
    [InlineData("AuthenticationTypes.Federation")]
    public async Task EntraTransformation_MixedModePreservesLocalPrincipalAndTenant(string identityType)
    {
        using var services = CreateServices();
        using var scope = services.CreateScope();
        var clinicId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PTDocClaimTypes.InternalUserId, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Admin),
            new Claim(HttpTenantContextAccessor.ClinicIdClaimType, clinicId.ToString()),
            new Claim(PTDocClaimTypes.AuthenticationType, "session_token")
        ], identityType));
        var transformation = new EntraExternalIdClaimsTransformation(
            Options.Create(new EntraExternalIdOptions { Enabled = true }));

        var transformed = await transformation.TransformAsync(principal);
        Assert.Same(principal, transformed);
        Assert.False(transformed.HasClaim(claim => claim.Type == PTDocClaimTypes.ExternalProvider));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = transformed
        };
        var resolver = scope.ServiceProvider.GetRequiredService<PrincipalRecordResolver>();
        Assert.Equal(userId, resolver.TryResolveInternalUserId());
        Assert.Equal(clinicId, scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().GetCurrentClinicId());
    }

    [Theory]
    [InlineData(PTDocClaimTypes.EntraJwtIdentityType)]
    [InlineData(PTDocClaimTypes.EntraOidcIdentityType)]
    public async Task EntraTransformation_EnrichesOnlyServerSelectedExternalIdentities(string identityType)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "external-test-subject")], identityType));
        var transformation = new EntraExternalIdClaimsTransformation(
            Options.Create(new EntraExternalIdOptions { Enabled = true }));

        var transformed = await transformation.TransformAsync(principal);

        Assert.Equal(EntraExternalIdOptions.DefaultProviderKey, transformed.FindFirst(PTDocClaimTypes.ExternalProvider)?.Value);
        Assert.Equal("external-test-subject", transformed.FindFirst(PTDocClaimTypes.ExternalSubject)?.Value);
        Assert.Equal("entra_jwt", transformed.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value);
    }

    [Theory]
    [InlineData(PTDocClaimTypes.EntraJwtIdentityType)]
    [InlineData(PTDocClaimTypes.EntraOidcIdentityType)]
    public async Task EntraTransformation_ReplacesReservedClaimsAndRemainsIdempotent(string identityType)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "verified-subject"),
            new Claim(PTDocClaimTypes.ExternalSubject, "spoofed-subject"),
            new Claim(PTDocClaimTypes.ExternalSubject, "another-subject"),
            new Claim(PTDocClaimTypes.ExternalProvider, "spoofed-provider"),
            new Claim(PTDocClaimTypes.ExternalProvider, "another-provider"),
            new Claim(PTDocClaimTypes.AuthenticationType, "legacy_jwt"),
            new Claim(PTDocClaimTypes.AuthenticationType, "session_token")
        ], identityType));
        var transformation = new EntraExternalIdClaimsTransformation(
            Options.Create(new EntraExternalIdOptions { Enabled = true }));

        var transformed = await transformation.TransformAsync(principal);
        var repeated = await transformation.TransformAsync(transformed);

        foreach (var result in new[] { transformed, repeated })
        {
            Assert.Equal("verified-subject", Assert.Single(result.FindAll(PTDocClaimTypes.ExternalSubject)).Value);
            Assert.Equal(EntraExternalIdOptions.DefaultProviderKey,
                Assert.Single(result.FindAll(PTDocClaimTypes.ExternalProvider)).Value);
            Assert.Equal("entra_jwt", Assert.Single(result.FindAll(PTDocClaimTypes.AuthenticationType)).Value);
            Assert.Equal(identityType, result.Identity?.AuthenticationType);
        }
        Assert.Equal("legacy_jwt", principal.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value);
    }

    [Fact]
    public void Resolver_UsesExternalMapping_NotEmail_ForPatientResolution()
    {
        var services = CreateServices();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clinicId = Guid.NewGuid();
        var mappedPatientId = Guid.NewGuid();

        context.Patients.AddRange(
            new Patient
            {
                Id = mappedPatientId,
                FirstName = "Mapped",
                LastName = "Patient",
                Email = "shared@example.com",
                DateOfBirth = new DateTime(1990, 1, 1),
                ClinicId = clinicId
            },
            new Patient
            {
                Id = Guid.NewGuid(),
                FirstName = "Duplicate",
                LastName = "Patient",
                Email = "shared@example.com",
                DateOfBirth = new DateTime(1991, 1, 1),
                ClinicId = clinicId
            });

        context.ExternalIdentityMappings.Add(new ExternalIdentityMapping
        {
            Provider = EntraExternalIdOptions.DefaultProviderKey,
            ExternalSubject = "entra-subject-1",
            PrincipalType = PrincipalTypes.Patient,
            InternalEntityId = mappedPatientId,
            TenantId = clinicId
        });
        context.SaveChanges();

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PTDocClaimTypes.ExternalProvider, EntraExternalIdOptions.DefaultProviderKey),
                new Claim(PTDocClaimTypes.ExternalSubject, "entra-subject-1"),
                new Claim(ClaimTypes.Role, Roles.Patient),
                new Claim(ClaimTypes.Email, "shared@example.com")
            ], "Test"))
        };

        var resolver = scope.ServiceProvider.GetRequiredService<PrincipalRecordResolver>();

        Assert.Equal(mappedPatientId, resolver.TryResolvePatientId());
        Assert.Equal(clinicId, resolver.TryResolveClinicId());
    }

    [Fact]
    public void HttpIdentityContextAccessor_Throws_WhenAuthenticatedUserIsUnmapped()
    {
        var services = CreateServices();
        using var scope = services.CreateScope();

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PTDocClaimTypes.ExternalProvider, EntraExternalIdOptions.DefaultProviderKey),
                new Claim(PTDocClaimTypes.ExternalSubject, "missing-user-subject"),
                new Claim(ClaimTypes.Role, Roles.PT)
            ], "Test"))
        };

        var accessor = scope.ServiceProvider.GetRequiredService<IIdentityContextAccessor>();

        Assert.Null(accessor.TryGetCurrentUserId());
        Assert.Throws<ProvisioningException>(() => accessor.GetCurrentUserId());
    }

    [Fact]
    public void HttpTenantContextAccessor_RejectsExternalTenantClaimThatConflictsWithMapping()
    {
        var services = CreateServices();
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mappedClinicId = Guid.NewGuid();
        var claimedClinicId = Guid.NewGuid();
        var externalSubject = Guid.NewGuid().ToString();
        var user = new User
        {
            Username = $"tenant-mismatch-{Guid.NewGuid():N}",
            PinHash = "unused",
            FirstName = "Tenant",
            LastName = "Mismatch",
            Role = Roles.Admin,
            ClinicId = mappedClinicId,
            IsActive = true
        };
        context.Users.Add(user);
        context.ExternalIdentityMappings.Add(new ExternalIdentityMapping
        {
            Provider = EntraExternalIdOptions.DefaultProviderKey,
            ExternalSubject = externalSubject,
            PrincipalType = PrincipalTypes.User,
            InternalEntityId = user.Id,
            TenantId = mappedClinicId
        });
        context.SaveChanges();

        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(PTDocClaimTypes.ExternalProvider, EntraExternalIdOptions.DefaultProviderKey),
                new Claim(PTDocClaimTypes.ExternalSubject, externalSubject),
                new Claim(ClaimTypes.NameIdentifier, externalSubject),
                new Claim(ClaimTypes.Role, Roles.Admin),
                new Claim(HttpTenantContextAccessor.ClinicIdClaimType, claimedClinicId.ToString())
            ], "Test"))
        };

        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var exception = Assert.Throws<ProvisioningException>(() => accessor.GetCurrentClinicId());

        Assert.Equal("tenant_claim_mismatch", exception.FailureCode);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddScoped<PrincipalRecordResolver>();
        services.AddScoped<IIdentityContextAccessor, HttpIdentityContextAccessor>();
        services.AddScoped<ITenantContextAccessor, HttpTenantContextAccessor>();
        services.AddScoped<IPatientContextAccessor, HttpPatientContextAccessor>();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        return services.BuildServiceProvider(validateScopes: true);
    }
}
