using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using Xunit;

namespace PTDoc.Tests.Identity;

public class UserRegistrationServiceTests
{
    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task RegisterAsync_ValidPtRequest_CreatesInactiveUser()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "Sarah Johnson",
            "sarah@clinic.com",
            new DateTime(1990, 1, 1),
            "PT",
            clinic.Id,
            "1234",
            "PT",
            "PT12345",
            "CA"));

        Assert.Equal(RegistrationStatus.PendingApproval, result.Status);
        var user = await context.Users.FirstAsync();
        Assert.False(user.IsActive);
        Assert.Equal("PT", user.Role);
        Assert.Equal(clinic.Id, user.ClinicId);
    }

    [Fact]
    public async Task RegisterAsync_MissingLicenseForPt_ReturnsInvalidLicenseData()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "Sarah Johnson",
            "sarah2@clinic.com",
            new DateTime(1990, 1, 1),
            "PT",
            clinic.Id,
            "1234",
            null,
            null,
            null));

        Assert.Equal(RegistrationStatus.InvalidLicenseData, result.Status);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsEmailAlreadyExists()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        context.Users.Add(new User
        {
            Username = "existing",
            PinHash = "hash",
            FirstName = "Existing",
            LastName = "User",
            Email = "dup@clinic.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ClinicId = clinic.Id
        });
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "New User",
            "dup@clinic.com",
            new DateTime(1992, 3, 3),
            "FrontDesk",
            clinic.Id,
            "1234",
            null,
            null,
            null));

        Assert.Equal(RegistrationStatus.EmailAlreadyExists, result.Status);
    }

    [Fact]
    public async Task RegisterAsync_InvalidPin_ReturnsInvalidPin()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "New User",
            "new@clinic.com",
            new DateTime(1992, 3, 3),
            "FrontDesk",
            clinic.Id,
            "12",
            null,
            null,
            null));

        Assert.Equal(RegistrationStatus.InvalidPin, result.Status);
    }

    [Fact]
    public async Task RegisterAsync_UsernameCollision_AppendsSuffix()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        context.Users.Add(new User
        {
            Username = "jsmith",
            PinHash = "hash",
            FirstName = "Existing",
            LastName = "User",
            Email = "existing@clinic.com",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            ClinicId = clinic.Id
        });
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "John Smith",
            "jsmith@newclinic.com",
            new DateTime(1991, 4, 4),
            "FrontDesk",
            clinic.Id,
            "1234",
            null,
            null,
            null));

        Assert.Equal(RegistrationStatus.PendingApproval, result.Status);
        var created = await context.Users.SingleAsync(u => u.Email == "jsmith@newclinic.com");
        Assert.Equal("jsmith2", created.Username);
    }

    [Fact]
    public async Task ApproveRegistrationAsync_SetsUserActiveTrue()
    {
        await using var context = CreateInMemoryContext();
        var user = new User
        {
            Username = "pending_user",
            PinHash = "hash",
            FirstName = "Pending",
            LastName = "User",
            Role = "PT",
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);
        var result = await sut.ApproveRegistrationAsync(user.Id, Guid.NewGuid());

        Assert.Equal(RegistrationStatus.Succeeded, result.Status);
        Assert.True((await context.Users.FindAsync(user.Id))!.IsActive);
    }

    [Fact]
    public async Task RejectRegistrationAsync_DeletesPendingUser()
    {
        await using var context = CreateInMemoryContext();
        var user = new User
        {
            Username = "reject_me",
            PinHash = "hash",
            FirstName = "Reject",
            LastName = "Me",
            Role = "PT",
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);
        var result = await sut.RejectRegistrationAsync(user.Id, Guid.NewGuid());

        Assert.Equal(RegistrationStatus.Succeeded, result.Status);
        Assert.Null(await context.Users.FindAsync(user.Id));
    }
}
