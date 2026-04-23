using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public class LegacyApiCredentialValidatorTests
{
    [Fact]
    public async Task ValidateAsync_UsesRealUserIdAndClinicClaims()
    {
        var clinicId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = userId,
            Username = "testuser",
            Email = "test@example.com",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Test",
            LastName = "User",
            Role = "PT",
            ClinicId = clinicId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var validator = new LegacyApiCredentialValidator(context);

        var identity = await validator.ValidateAsync("testuser", "1234");

        Assert.NotNull(identity);
        Assert.Equal(userId.ToString(), identity!.FindFirst(PTDocClaimTypes.InternalUserId)?.Value);
        Assert.Equal(userId.ToString(), identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal(clinicId.ToString(), identity.FindFirst(HttpTenantContextAccessor.ClinicIdClaimType)?.Value);
        Assert.Equal("legacy_jwt", identity.FindFirst(PTDocClaimTypes.AuthenticationType)?.Value);
    }

    [Fact]
    public async Task ValidateAsync_MatchesUsername_CaseInsensitively()
    {
        var userId = Guid.NewGuid();

        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = userId,
            Username = "antoniolhardy27",
            Email = "antonio@example.com",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Antonio",
            LastName = "Hardy",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var validator = new LegacyApiCredentialValidator(context);

        var identity = await validator.ValidateAsync("Antoniolhardy27", "1234");

        Assert.NotNull(identity);
        Assert.Equal(userId.ToString(), identity!.FindFirst(PTDocClaimTypes.InternalUserId)?.Value);
        Assert.Equal("antoniolhardy27", identity.FindFirst(ClaimTypes.Name)?.Value);
    }

    [Fact]
    public async Task ValidateAsync_NormalizesStoredEmail_BeforeCaseInsensitiveLookup()
    {
        var userId = Guid.NewGuid();

        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = userId,
            Username = "MixedCaseUser",
            Email = "Mixed.Email@Clinic.com",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Mixed",
            LastName = "Case",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var storedUser = await context.Users.SingleAsync(u => u.Id == userId);
        Assert.Equal("mixedcaseuser", storedUser.Username);
        Assert.Equal("mixed.email@clinic.com", storedUser.Email);

        var validator = new LegacyApiCredentialValidator(context);

        var identity = await validator.ValidateAsync("MIXED.EMAIL@CLINIC.COM", "1234");

        Assert.NotNull(identity);
        Assert.Equal(userId.ToString(), identity!.FindFirst(PTDocClaimTypes.InternalUserId)?.Value);
        Assert.Equal("mixedcaseuser", identity.FindFirst(ClaimTypes.Name)?.Value);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
