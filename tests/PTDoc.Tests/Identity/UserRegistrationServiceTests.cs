using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using Xunit;

namespace PTDoc.Tests.Identity;

[Trait("Category", "CoreCi")]
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
            "PT12345",
            "CA"));

        Assert.Equal(RegistrationStatus.PendingApproval, result.Status);
        var user = await context.Users.FirstAsync();
        Assert.False(user.IsActive);
        Assert.Equal("PT", user.Role);
        Assert.Equal(clinic.Id, user.ClinicId);
        Assert.Equal(new DateTime(1990, 1, 1), user.DateOfBirth);
        Assert.Equal("PT12345", user.LicenseNumber);
        Assert.Equal("CA", user.LicenseState);
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
            null));

        Assert.Equal(RegistrationStatus.InvalidPin, result.Status);
    }

    [Theory]
    [InlineData("Billing", "billing@clinic.com")]
    [InlineData("Patient", "patient@clinic.com")]
    public async Task RegisterAsync_QaReadOnlyRoles_CreateInactiveUser(string roleKey, string email)
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var result = await sut.RegisterAsync(new UserRegistrationRequest(
            "QA User",
            email,
            new DateTime(1992, 3, 3),
            roleKey,
            clinic.Id,
            "1234",
            null,
            null));

        Assert.Equal(RegistrationStatus.PendingApproval, result.Status);
        var user = await context.Users.SingleAsync(u => u.Email == email);
        Assert.False(user.IsActive);
        Assert.Equal(roleKey, user.Role);
    }

    [Fact]
    public async Task GetRegisterableRolesAsync_IncludesQaAuditRoles()
    {
        await using var context = CreateInMemoryContext();
        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var roles = await sut.GetRegisterableRolesAsync();

        Assert.Contains(roles, role => role.Key == Roles.PT);
        Assert.Contains(roles, role => role.Key == Roles.PTA);
        Assert.Contains(roles, role => role.Key == Roles.FrontDesk);
        Assert.Contains(roles, role => role.Key == Roles.Owner);
        Assert.Contains(roles, role => role.Key == Roles.Billing);
        Assert.Contains(roles, role => role.Key == Roles.Patient);
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
            null));

        Assert.Equal(RegistrationStatus.PendingApproval, result.Status);
        var created = await context.Users.SingleAsync(u => u.Email == "jsmith@newclinic.com");
        Assert.Equal("jsmith2", created.Username);
    }

    [Fact]
    public async Task ApproveRegistrationAsync_SetsUserActiveTrue()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        var user = new User
        {
            Username = "pending_user",
            PinHash = "hash",
            FirstName = "Pending",
            LastName = "User",
            Role = "PT",
            Email = "pending@clinic.com",
            DateOfBirth = new DateTime(1990, 1, 1),
            ClinicId = clinic.Id,
            LicenseNumber = "PT0001",
            LicenseState = "CA",
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
    public async Task GetPendingRegistrationsAsync_Returns_RealCompletenessState()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);
        context.Users.AddRange(
            new User
            {
                Username = "complete",
                PinHash = "hash",
                FirstName = "Complete",
                LastName = "Applicant",
                Email = "complete@clinic.com",
                DateOfBirth = new DateTime(1990, 1, 1),
                Role = "PT",
                ClinicId = clinic.Id,
                LicenseNumber = "PT-1000",
                LicenseState = "CA",
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            },
            new User
            {
                Username = "missing",
                PinHash = "hash",
                FirstName = "Missing",
                LastName = "License",
                Email = "missing@clinic.com",
                DateOfBirth = new DateTime(1991, 2, 2),
                Role = "PT",
                ClinicId = clinic.Id,
                LicenseNumber = null,
                LicenseState = null,
                IsActive = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            });
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);

        var page = await sut.GetPendingRegistrationsAsync(new PendingRegistrationsQuery(null, "Pending", null, null, null, null, null));

        var complete = Assert.Single(page.Items.Where(item => item.Email == "complete@clinic.com"));
        Assert.True(complete.CredentialsComplete);
        Assert.Empty(complete.MissingFields);
        Assert.Equal("PT-1000", complete.LicenseNumber);
        Assert.Equal("CA", complete.LicenseState);

        var missing = Assert.Single(page.Items.Where(item => item.Email == "missing@clinic.com"));
        Assert.False(missing.CredentialsComplete);
        Assert.Contains(missing.MissingFields, message => message.Contains("License number", StringComparison.Ordinal));
        Assert.Contains(missing.MissingFields, message => message.Contains("License state", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdatePendingRegistrationAsync_Persists_Admin_Edits_And_Audits_Field_Names()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);

        var user = new User
        {
            Username = "pending-edit",
            PinHash = "hash",
            FirstName = "Pending",
            LastName = "Edit",
            Email = "pending-edit@clinic.com",
            DateOfBirth = null,
            Role = "PT",
            ClinicId = clinic.Id,
            LicenseNumber = null,
            LicenseState = null,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);
        var adminId = Guid.NewGuid();

        var result = await sut.UpdatePendingRegistrationAsync(
            user.Id,
            new AdminRegistrationUpdateRequest(
                "Updated Pending Edit",
                "updated@clinic.com",
                new DateTime(1988, 4, 12),
                "PTA",
                "PTA-7788",
                "ny"),
            adminId);

        Assert.Equal(RegistrationStatus.Succeeded, result.Status);

        var persisted = await context.Users.SingleAsync(saved => saved.Id == user.Id);
        Assert.Equal("Updated", persisted.FirstName);
        Assert.Equal("Pending Edit", persisted.LastName);
        Assert.Equal("updated@clinic.com", persisted.Email);
        Assert.Equal(new DateTime(1988, 4, 12), persisted.DateOfBirth);
        Assert.Equal("PTA", persisted.Role);
        Assert.Equal("PTA-7788", persisted.LicenseNumber);
        Assert.Equal("NY", persisted.LicenseState);

        var audit = await context.AuditLogs.SingleAsync(log => log.EntityId == user.Id && log.EventType == "RegistrationUpdated");
        Assert.Equal(adminId, audit.UserId);
        Assert.Contains("changedFields", audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("updated@clinic.com", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PTA-7788", audit.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPendingRegistrationAsync_Returns_Stored_Username()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);

        var user = new User
        {
            Username = "review-user",
            PinHash = "hash",
            FirstName = "Review",
            LastName = "User",
            Email = "review.user@clinic.com",
            DateOfBirth = new DateTime(1992, 7, 8),
            Role = "FrontDesk",
            ClinicId = clinic.Id,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);
        var detail = await sut.GetPendingRegistrationAsync(user.Id);

        Assert.NotNull(detail);
        Assert.Equal("review-user", detail!.Username);
    }

    [Fact]
    public async Task ApproveRegistrationAsync_Returns_ValidationFailed_When_Required_Data_Is_Missing()
    {
        await using var context = CreateInMemoryContext();
        var clinic = new Clinic { Name = "North Clinic", Slug = "north", IsActive = true };
        context.Clinics.Add(clinic);

        var user = new User
        {
            Username = "pending-missing",
            PinHash = "hash",
            FirstName = "Pending",
            LastName = "Missing",
            Email = "pending-missing@clinic.com",
            DateOfBirth = null,
            Role = "PT",
            ClinicId = clinic.Id,
            LicenseNumber = null,
            LicenseState = null,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var sut = new UserRegistrationService(context, NullLogger<UserRegistrationService>.Instance);
        var result = await sut.ApproveRegistrationAsync(user.Id, Guid.NewGuid());

        Assert.Equal(RegistrationStatus.ValidationFailed, result.Status);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("DateOfBirth", result.ValidationErrors!.Keys);
        Assert.Contains("LicenseNumber", result.ValidationErrors.Keys);
        Assert.Contains("LicenseState", result.ValidationErrors.Keys);
        Assert.False((await context.Users.SingleAsync(saved => saved.Id == user.Id)).IsActive);
    }

    [Fact]
    public async Task RejectRegistrationAsync_KeepsUserInactiveAndAuditsDecision()
    {
        await using var context = CreateInMemoryContext();
        var adminId = Guid.NewGuid();
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
        var result = await sut.RejectRegistrationAsync(user.Id, adminId);

        Assert.Equal(RegistrationStatus.Succeeded, result.Status);

        var persistedUser = await context.Users.FindAsync(user.Id);
        Assert.NotNull(persistedUser);
        Assert.False(persistedUser!.IsActive);

        var rejectionAudit = await context.AuditLogs
            .Where(a => a.EntityType == nameof(User)
                && a.EntityId == user.Id
                && a.EventType == "RegistrationRejected")
            .SingleAsync();

        Assert.Equal(adminId, rejectionAudit.UserId);
        Assert.True(rejectionAudit.Success);
    }
}
