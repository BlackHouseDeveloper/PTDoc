using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Integration;

/// <summary>
/// Integration tests for SQLCipher encryption functionality.
/// Validates encrypted and plain database modes work correctly.
/// </summary>
public class EncryptionIntegrationTests : IDisposable
{
    private SqliteConnection? _connection;

    [Fact]
    public async Task Migrations_Succeed_When_Encryption_Enabled()
    {
        // Arrange: Create encrypted connection
        var key = GenerateTestKey();
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        // Set encryption key via PRAGMA
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA key = '{key}';";
            await command.ExecuteNonQueryAsync();
        }

        // Act: Apply migrations
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Assert: DB is functional
        var patient = new Patient
        {
            FirstName = "Test",
            LastName = "Patient",
            DateOfBirth = DateTime.UtcNow.AddYears(-30)
        };

        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        var retrieved = await context.Patients.FirstAsync();
        Assert.Equal("Test", retrieved.FirstName);
        Assert.Equal("Patient", retrieved.LastName);
    }

    [Fact]
    public async Task Plain_Mode_Still_Works()
    {
        // Arrange: Normal SQLite (no encryption)
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        // Act: Apply migrations without PRAGMA key
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Assert: DB works normally
        var patient = new Patient
        {
            FirstName = "Plain",
            LastName = "Mode",
            DateOfBirth = DateTime.UtcNow.AddYears(-25)
        };

        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Patients.CountAsync());

        var retrieved = await context.Patients.FirstAsync();
        Assert.Equal("Plain", retrieved.FirstName);
    }

    [Fact]
    public async Task Encrypted_DB_Persists_Data_Correctly()
    {
        // Arrange: Encrypted database
        var key = GenerateTestKey();
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        using (var command = _connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA key = '{key}';";
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ApplicationDbContext(options);
        await context.Database.MigrateAsync();

        // Act: Add multiple entities
        var patient1 = new Patient { FirstName = "Alice", LastName = "Smith", DateOfBirth = DateTime.UtcNow.AddYears(-35) };
        var patient2 = new Patient { FirstName = "Bob", LastName = "Jones", DateOfBirth = DateTime.UtcNow.AddYears(-40) };

        context.Patients.AddRange(patient1, patient2);
        await context.SaveChangesAsync();

        // Assert: Data persists
        Assert.Equal(2, await context.Patients.CountAsync());

        var alice = await context.Patients.FirstAsync(p => p.FirstName == "Alice");
        Assert.Equal("Smith", alice.LastName);
    }

    private static string GenerateTestKey()
    {
        // Generate a test key that meets 32-character minimum
        return "test-encryption-key-for-unit-testing-32-chars-minimum";
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
