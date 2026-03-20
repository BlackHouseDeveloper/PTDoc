using Microsoft.EntityFrameworkCore;
using PTDoc.Application.LocalData.Entities;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.LocalData;
using Xunit;

namespace PTDoc.Tests.LocalData;

/// <summary>
/// Tests for the LocalDbContext schema and entity configuration.
/// Uses an in-memory EF Core provider to avoid platform SQLite/SQLCipher dependencies.
/// </summary>
public class LocalDbContextTests
{
    private static LocalDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<LocalDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new LocalDbContext(options);
    }

    [Fact]
    public async Task EnsureCreated_InitialisesAllEntitySets()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act — EnsureCreated on in-memory just confirms sets exist
        await context.Database.EnsureCreatedAsync();

        // Assert — all DbSets are accessible without throwing
        Assert.NotNull(context.UserProfiles);
        Assert.NotNull(context.PatientSummaries);
        Assert.NotNull(context.AppointmentSummaries);
        Assert.NotNull(context.SyncMetadata);
    }

    [Fact]
    public async Task UserProfiles_CanInsertAndRetrieve()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var profile = new LocalUserProfile
        {
            ServerId = Guid.NewGuid(),
            Username = "jsmith",
            FirstName = "John",
            LastName = "Smith",
            Role = "Clinician",
            SyncState = SyncState.Synced,
            LastModifiedUtc = DateTime.UtcNow
        };

        // Act
        context.UserProfiles.Add(profile);
        await context.SaveChangesAsync();

        var retrieved = await context.UserProfiles.FirstOrDefaultAsync(u => u.Username == "jsmith");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("John", retrieved.FirstName);
        Assert.Equal("Smith", retrieved.LastName);
        Assert.Equal(SyncState.Synced, retrieved.SyncState);
        Assert.True(retrieved.LocalId > 0); // auto-increment assigned
    }

    [Fact]
    public async Task PatientSummaries_CanInsertAndRetrieve()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var patient = new LocalPatientSummary
        {
            ServerId = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Doe",
            MedicalRecordNumber = "MRN-001",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };

        // Act
        context.PatientSummaries.Add(patient);
        await context.SaveChangesAsync();

        var retrieved = await context.PatientSummaries.FirstAsync();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Jane", retrieved.FirstName);
        Assert.Equal("Doe", retrieved.LastName);
        Assert.Equal("MRN-001", retrieved.MedicalRecordNumber);
        Assert.Equal(SyncState.Pending, retrieved.SyncState);
    }

    [Fact]
    public async Task AppointmentSummaries_CanInsertAndRetrieve()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var appointment = new LocalAppointmentSummary
        {
            ServerId = Guid.NewGuid(),
            PatientServerId = Guid.NewGuid(),
            PatientFirstName = "Alice",
            PatientLastName = "Johnson",
            StartTimeUtc = DateTime.UtcNow,
            EndTimeUtc = DateTime.UtcNow.AddHours(1),
            Status = "Scheduled",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        };

        // Act
        context.AppointmentSummaries.Add(appointment);
        await context.SaveChangesAsync();

        var retrieved = await context.AppointmentSummaries.FirstAsync();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Alice", retrieved.PatientFirstName);
        Assert.Equal("Scheduled", retrieved.Status);
        Assert.Equal(SyncState.Pending, retrieved.SyncState);
    }

    [Fact]
    public async Task SyncMetadata_CanInsertAndRetrieve()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var meta = new LocalSyncMetadata
        {
            EntityType = "Patient",
            LastPulledAt = DateTime.UtcNow.AddMinutes(-5),
            PendingCount = 3
        };

        // Act
        context.SyncMetadata.Add(meta);
        await context.SaveChangesAsync();

        var retrieved = await context.SyncMetadata.FirstOrDefaultAsync(m => m.EntityType == "Patient");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Patient", retrieved.EntityType);
        Assert.Equal(3, retrieved.PendingCount);
        Assert.NotNull(retrieved.LastPulledAt);
    }

    [Fact]
    public async Task LocalId_IsAutoIncrementedOnInsert()
    {
        // Arrange
        using var context = CreateInMemoryContext();

        // Act — insert two records
        context.PatientSummaries.Add(new LocalPatientSummary
        {
            FirstName = "Patient",
            LastName = "One",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        });
        context.PatientSummaries.Add(new LocalPatientSummary
        {
            FirstName = "Patient",
            LastName = "Two",
            SyncState = SyncState.Pending,
            LastModifiedUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        var all = await context.PatientSummaries.ToListAsync();

        // Assert
        Assert.Equal(2, all.Count);
        Assert.NotEqual(all[0].LocalId, all[1].LocalId);
    }
}
