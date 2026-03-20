using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Identity;

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

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}