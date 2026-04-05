using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public class PrincipalProvisioningResolverTests
{
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