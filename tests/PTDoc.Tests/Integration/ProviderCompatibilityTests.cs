using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Provider compatibility tests for Sprint B.
/// Validates that SQLite migrations apply correctly and basic CRUD operations work.
/// Decision reference: PTDocs+ Branch-Specific Database Blueprint - Sprint B.
/// </summary>
public class ProviderCompatibilityTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public ProviderCompatibilityTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection,
                x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();
    }

    [Fact]
    public void Sqlite_Provider_Creates_Schema_Successfully()
    {
        // Assert: Schema was created (Migrate() did not throw)
        var pendingMigrations = _context.Database.GetPendingMigrations();
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task Sqlite_Provider_Can_Insert_And_Retrieve_Patient()
    {
        // Arrange
        var patient = new Patient
        {
            FirstName = "Sprint",
            LastName = "B",
            DateOfBirth = new DateTime(1990, 6, 15)
        };

        // Act
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Patients.FindAsync(patient.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Sprint", retrieved.FirstName);
        Assert.Equal("B", retrieved.LastName);
    }

    [Fact]
    public async Task Sqlite_Provider_Enforces_Unique_Index_On_IntakeForm_AccessToken()
    {
        // Arrange: two intake forms with the same access token violates unique index
        var patient = new Patient
        {
            FirstName = "Unique",
            LastName = "Test",
            DateOfBirth = new DateTime(1985, 1, 1)
        };
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var token = Guid.NewGuid().ToString("N");

        var form1 = new IntakeForm
        {
            PatientId = patient.Id,
            AccessToken = token,
            TemplateVersion = "1.0",
            LastModifiedUtc = DateTime.UtcNow
        };
        var form2 = new IntakeForm
        {
            PatientId = patient.Id,
            AccessToken = token, // same token → unique constraint violation
            TemplateVersion = "1.0",
            LastModifiedUtc = DateTime.UtcNow
        };

        _context.IntakeForms.AddRange(form1, form2);

        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _context.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_Provider_Can_Insert_And_Retrieve_User()
    {
        // Arrange
        var user = new User
        {
            Username = $"provider_test_{Guid.NewGuid():N}",
            PinHash = "$2b$12$testhashvalue",
            Role = "Clinician",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        // Act
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var retrieved = await _context.Users.FindAsync(user.Id);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(user.Username, retrieved.Username);
        Assert.Equal("Clinician", retrieved.Role);
    }

    [Fact]
    public async Task Sqlite_Provider_Can_Delete_Entity()
    {
        // Arrange
        var patient = new Patient
        {
            FirstName = "Delete",
            LastName = "Me",
            DateOfBirth = new DateTime(2000, 1, 1)
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        // Act
        _context.Patients.Remove(patient);
        await _context.SaveChangesAsync();

        // Assert
        var deleted = await _context.Patients.FindAsync(patient.Id);
        Assert.Null(deleted);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
