using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Interceptors;
using Xunit;

namespace PTDoc.Tests.Identity;

/// <summary>
/// Tests for ModifiedByUserId stamping via SyncMetadataInterceptor.
/// </summary>
public class ModifiedByUserIdStampingTests
{
    [Fact]
    public async Task SaveChanges_NewEntity_StampsModifiedByUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var mockIdentityContext = new Mock<IIdentityContextAccessor>();
        mockIdentityContext.Setup(x => x.GetCurrentUserId()).Returns(userId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(new SyncMetadataInterceptor(mockIdentityContext.Object))
            .Options;

        var context = new ApplicationDbContext(options);

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "John",
            LastName = "Doe",
            DateOfBirth = DateTime.UtcNow.AddYears(-30)
        };

        // Act
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(userId, patient.ModifiedByUserId);
        Assert.True(patient.LastModifiedUtc > DateTime.UtcNow.AddSeconds(-5));
        Assert.Equal(SyncState.Pending, patient.SyncState);
    }

    [Fact]
    public async Task SaveChanges_SystemUser_UsesSystemUserId()
    {
        // Arrange
        var mockIdentityContext = new Mock<IIdentityContextAccessor>();
        mockIdentityContext.Setup(x => x.GetCurrentUserId()).Returns(IIdentityContextAccessor.SystemUserId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(new SyncMetadataInterceptor(mockIdentityContext.Object))
            .Options;

        var context = new ApplicationDbContext(options);

        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            ClinicalId = Guid.NewGuid(),
            StartTimeUtc = DateTime.UtcNow,
            EndTimeUtc = DateTime.UtcNow.AddHours(1),
            AppointmentType = AppointmentType.InitialEvaluation,
            Status = AppointmentStatus.Scheduled
        };

        // Act
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(IIdentityContextAccessor.SystemUserId, appointment.ModifiedByUserId);
        Assert.True(appointment.LastModifiedUtc > DateTime.UtcNow.AddSeconds(-5));
    }
}
