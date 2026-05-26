using Microsoft.Data.Sqlite;
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
    private const string TestBetaSeedPin = "8642";

    [Fact]
    public async Task SeedBetaAccessDataAsync_CreatesPfptClinicAndSeededUsers()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

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

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        dani.IsActive = false;
        dani.Role = Roles.Billing;
        dani.PinHash = AuthService.HashPin("9999");
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        Assert.Equal(4, await context.Users.CountAsync(user => user.ClinicId == DatabaseSeeder.BetaClinicId));
        dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        Assert.True(dani.IsActive);
        Assert.Equal(Roles.PT, dani.Role);
        Assert.Equal(DatabaseSeeder.BetaClinicId, dani.ClinicId);

        var authService = new AuthService(context, NullLogger<AuthService>.Instance, CreateAuditServiceMock());
        var result = await authService.AuthenticateAsync("dani.beta", TestBetaSeedPin, "127.0.0.1", "BetaAccessSeederTests");

        Assert.NotNull(result);
        Assert.Equal(AuthStatus.Success, result!.Status);
        Assert.Equal(Roles.PT, result.Role);
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_CreatesIdempotentSearchablePatientFixtures()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);
        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var patients = await context.Patients
            .Where(patient => patient.ClinicId == DatabaseSeeder.BetaClinicId
                && patient.MedicalRecordNumber != null
                && patient.MedicalRecordNumber!.StartsWith("BETA-PT-"))
            .OrderBy(patient => patient.MedicalRecordNumber)
            .ToListAsync();

        Assert.Equal(4, patients.Count);
        Assert.Contains(patients, patient =>
            patient.FirstName == "Avery" &&
            patient.LastName == "Adams" &&
            patient.MedicalRecordNumber == "BETA-PT-001" &&
            patient.Email == "avery.adams.beta@physicallyfitpt.test");
        Assert.Contains(patients, patient =>
            patient.FirstName == "Jordan" &&
            patient.LastName == "Lee" &&
            patient.MedicalRecordNumber == "BETA-PT-002" &&
            patient.Email == "jordan.lee.beta@physicallyfitpt.test");
        Assert.All(patients, patient =>
        {
            Assert.False(patient.IsArchived);
            Assert.Equal(DatabaseSeeder.BetaClinicId, patient.ClinicId);
            Assert.True(patient.ConsentSigned);
        });
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_DoesNotMoveFixtureMrnFromAnotherClinic()
    {
        await using var context = CreateInMemoryContext();
        var otherClinicId = Guid.NewGuid();
        var existingPatientId = Guid.NewGuid();
        context.Clinics.Add(new Clinic
        {
            Id = otherClinicId,
            Name = "Other Clinic",
            Slug = "other-clinic",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        context.Patients.Add(new Patient
        {
            Id = existingPatientId,
            FirstName = "Existing",
            LastName = "Patient",
            DateOfBirth = new DateTime(1980, 1, 1),
            Email = "existing.patient@example.test",
            MedicalRecordNumber = "BETA-PT-001",
            ClinicId = otherClinicId
        });
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var existingPatient = await context.Patients.SingleAsync(patient => patient.Id == existingPatientId);
        Assert.Equal(otherClinicId, existingPatient.ClinicId);
        Assert.Equal("Existing", existingPatient.FirstName);
        Assert.Equal("existing.patient@example.test", existingPatient.Email);

        Assert.False(await context.Patients.AnyAsync(patient =>
            patient.ClinicId == DatabaseSeeder.BetaClinicId &&
            patient.MedicalRecordNumber == "BETA-PT-001"));
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_MatchesFixtureMrnsCaseInsensitively()
    {
        await using var context = CreateInMemoryContext();
        var existingPatientId = Guid.NewGuid();
        context.Clinics.Add(new Clinic
        {
            Id = DatabaseSeeder.BetaClinicId,
            Name = "Physically Fit Physical Therapy",
            Slug = "pfpt-beta",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        context.Patients.Add(new Patient
        {
            Id = existingPatientId,
            FirstName = "Existing",
            LastName = "Lowercase",
            DateOfBirth = new DateTime(1980, 1, 1),
            Email = "existing.lowercase@example.test",
            MedicalRecordNumber = "beta-pt-001",
            ClinicId = DatabaseSeeder.BetaClinicId
        });
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var patients = await context.Patients
            .Where(patient => patient.ClinicId == DatabaseSeeder.BetaClinicId
                && patient.MedicalRecordNumber != null
                && patient.MedicalRecordNumber!.ToUpper() == "BETA-PT-001")
            .ToListAsync();

        var patient = Assert.Single(patients);
        Assert.Equal(existingPatientId, patient.Id);
        Assert.Equal("BETA-PT-001", patient.MedicalRecordNumber);
        Assert.Equal("Avery", patient.FirstName);
        Assert.Equal("avery.adams.beta@physicallyfitpt.test", patient.Email);
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_DoesNotRewriteStablePinOrFutureLicenseExpiration()
    {
        await using var context = CreateInMemoryContext();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        var originalPinHash = dani.PinHash;
        var originalLicenseExpirationDate = dani.LicenseExpirationDate;

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        dani = await context.Users.SingleAsync(user => user.Username == "dani.beta");
        Assert.Equal(originalPinHash, dani.PinHash);
        Assert.Equal(originalLicenseExpirationDate, dani.LicenseExpirationDate);
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_UpdatesLegacyMixedCaseIdentifierRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var legacyUserId = Guid.NewGuid();
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Users (Id, Username, PinHash, FirstName, LastName, Email, Role, IsActive, CreatedAt)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})
            """,
            legacyUserId,
            "Dani.Beta",
            AuthService.HashPin("9999"),
            "Legacy",
            "Tester",
            "DANI.BETA@PHYSICALLYFITPT.TEST",
            Roles.Billing,
            false,
            DateTime.UtcNow);

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var users = await context.Users
            .Where(user => user.Username.ToLower() == "dani.beta")
            .ToListAsync();
        var user = Assert.Single(users);
        Assert.Equal(legacyUserId, user.Id);
        Assert.Equal("dani.beta", user.Username);
        Assert.Equal("dani.beta@physicallyfitpt.test", user.Email);
        Assert.Equal(Roles.PT, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(DatabaseSeeder.BetaClinicId, user.ClinicId);
    }

    [Fact]
    public async Task SeedBetaAccessDataAsync_ReusesExistingBetaSlugClinic()
    {
        await using var context = CreateInMemoryContext();
        var existingClinicId = Guid.NewGuid();
        context.Clinics.Add(new Clinic
        {
            Id = existingClinicId,
            Name = "Existing PFPT Beta",
            Slug = "pfpt-beta",
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();

        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);

        var clinic = await context.Clinics.SingleAsync(clinic => clinic.Slug == "pfpt-beta");
        Assert.Equal(existingClinicId, clinic.Id);
        Assert.Equal("Physically Fit Physical Therapy", clinic.Name);
        Assert.True(clinic.IsActive);

        var users = await context.Users
            .Where(user => user.ClinicId == existingClinicId)
            .ToListAsync();
        Assert.Equal(4, users.Count);
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
        await DatabaseSeeder.SeedBetaAccessDataAsync(context, NullLogger.Instance, TestBetaSeedPin);
        var authService = new AuthService(context, NullLogger<AuthService>.Instance, CreateAuditServiceMock());

        var usernameResult = await authService.AuthenticateAsync(username, TestBetaSeedPin, "127.0.0.1", "BetaAccessSeederTests");
        var emailResult = await authService.AuthenticateAsync(email, TestBetaSeedPin, "127.0.0.1", "BetaAccessSeederTests");

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
