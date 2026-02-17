using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Infrastructure.Data.Seeders;

/// <summary>
/// Seeds the database with initial test data for development.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds a test user for development and testing.
    /// Username: "testuser", PIN: "1234"
    /// </summary>
    public static async Task SeedTestDataAsync(ApplicationDbContext context, ILogger logger)
    {
        // Check if we already have users
        var hasUsers = await context.Users.AnyAsync();
        if (hasUsers)
        {
            logger.LogInformation("Database already contains users, skipping seed");
            return;
        }

        logger.LogInformation("Seeding test data...");

        // Create system user (for background operations)
        var systemUser = new User
        {
            Id = IIdentityContextAccessor.SystemUserId,
            Username = "system",
            PinHash = AuthService.HashPin("system-not-for-login"),
            FirstName = "System",
            LastName = "User",
            Role = "System",
            IsActive = false, // System user cannot log in
            CreatedAt = DateTime.UtcNow
        };

        // Create test user for development
        var testUser = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Test",
            LastName = "User",
            Role = "PT",
            Email = "test@ptdoc.local",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LicenseNumber = "PT123456",
            LicenseState = "CA",
            LicenseExpirationDate = DateTime.UtcNow.AddYears(2)
        };

        context.Users.AddRange(systemUser, testUser);
        await context.SaveChangesAsync();

        logger.LogInformation("Test data seeded successfully. Test user: testuser/1234");
    }
}
