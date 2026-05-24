using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Seeders;
using PTDoc.Infrastructure.Identity;
using Xunit;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
public sealed class BetaAccessSeederTests
{
    [Fact]
    public async Task SeedBetaAccessDataAsync_CreatesPfptClinicAndSeededUsers()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance);

        var clinic = await context.Clinics.SingleAsync(clinic => clinic.Id == DatabaseSeeder.BetaClinicId);
        Assert.Equal("Physically Fit Physical Therapy", clinic.Name);
        Assert.Equal("pfpt-beta", clinic.Slug);
        Assert.True(clinic.IsActive);

        var users = await context.Users
            .Where(user => user.ClinicId == DatabaseSeeder.BetaClinicId)
            .OrderBy(user => user.Username)
            .ToListAsync();

        Assert.Equal(4, users.Count);
        AssertSeededUser(users, "dani.beta", "dani.beta@physicallyfitpt.test", Roles.PT, "PT-BETA-001", "CA");
        AssertSeededUser(users, "january.beta", "january.beta@physicallyfitpt.test", Roles.Admin, null, null);
        AssertSeededUser(users, "patient.beta", "patient.beta@physicallyfitpt.test", Roles.Patient, null, null);
        AssertSeededUser(users, "pta.beta", "pta.beta@physicallyfitpt.test", Roles.PTA, "PTA-BETA-001", "CA");
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_IsAuthoritativeAndIdempotent()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance);

        var dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        dani.IsActive = false;
        dani.Role = Roles.Billing;
        dani.PinHash = AuthService.HashPin("9999");
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance);

        Assert.Equal(4, await context.Users.CountAsync(user => user.ClinicId == DatabaseSeeder.BetaClinicId));
        dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        Assert.True(dani.IsActive);
        Assert.Equal(Roles.PT, dani.Role);
        Assert.Equal(DatabaseSeeder.BetaClinicId, dani.ClinicId);

        var authService = new AuthService(context, NullLogger<AuthService>.Instance, CreateAuditServiceMock());
        var result = await authService.AuthenticateAsync("dani.beta", "1234", "127.0.0.1", "BetaAccessSeederTests");

        Assert.NotNull(result);
        Assert.Equal(AuthStatus.Success, result!.Status);
        Assert.Equal(Roles.PT, result.Role);
    }

    [Theory]
    [InlineData("january.beta", "january.beta@physicallyfitpt.test", Roles.Admin)]
    [InlineData("dani.beta", "dani.beta@physicallyfitpt.test", Roles.PT)]
    [InlineData("pta.beta", "pta.beta@physicallyfitpt.test", Roles.PTA)]
    [InlineData("patient.beta", "patient.beta@physicallyfitpt.test", Roles.Patient)]
    public async Task SeedBetaAccessDataAsync_SeededUsersCanLoginWithUsernameOrEmail(
        string username,
        string email,
        string role)
    {
        await using var context = CreateInMemoryContext();
        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance);
        var authService = new AuthService(context, NullLogger<AuthService>.Instance, CreateAuditServiceMock());

        var usernameResult = await authService.AuthenticateAsync(username, "1234", "127.0.0.1", "BetaAccessSeederTests");
        var emailResult = await authService.AuthenticateAsync(email, "1234", "127.0.0.1", "BetaAccessSeederTests");

        Assert.NotNull(usernameResult);
        Assert.NotNull(emailResult);
        Assert.Equal(AuthStatus.Success, usernameResult!.Status);
        Assert.Equal(AuthStatus.Success, emailResult!.Status);
        Assert.Equal(role, usernameResult.Role);
        Assert.Equal(role, emailResult.Role);
        Assert.Equal(DatabaseSeeder.BetaClinicId, usernameResult.ClinicId);
        Assert.Equal(DatabaseSeeder.BetaClinicId, emailResult.ClinicId);
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static IAuditService CreateAuditServiceMock()
    {
        var mock = new Mock<IAuditService>();
        return mock.Object;
    }

    private static void AssertSeededUser(
        IReadOnlyCollection<User> users,
        string username,
        string email,
        string role,
        string? licenseNumber,
        string? licenseState)
    {
        var user = Assert.Single(users.Where(user => user.Username == username));
        Assert.Equal(email, user.Email);
        Assert.Equal(role, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(DatabaseSeeder.BetaClinicId, user.ClinicId);
        Assert.Equal(licenseNumber, user.LicenseNumber);
        Assert.Equal(licenseState, user.LicenseState);
    }
}
