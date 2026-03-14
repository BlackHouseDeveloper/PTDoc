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
    /// Well-known ID for the default development clinic (Sprint J).
    /// Matches the demo clinic_id claim in CredentialValidator.
    /// </summary>
    public static readonly Guid DefaultClinicId = Guid.Parse("00000000-0000-0000-0000-000000000100");

    /// <summary>
    /// Seeds a test user for development and testing.
    /// Username: "testuser", PIN: "1234"
    /// Idempotent: each entity type is checked independently so upgrading an existing
    /// dev database to Sprint J (which adds the Clinic table) still seeds the default clinic
    /// and attaches testuser to it.
    /// </summary>
    public static async Task SeedTestDataAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Checking seed data...");

        // Sprint J: Ensure the default development clinic exists regardless of whether
        // users were already seeded. This allows existing dev databases to gain the
        // default clinic after upgrading to Sprint J without a full re-seed.
        var clinicExists = await context.Clinics
            .AnyAsync(c => c.Id == DefaultClinicId);

        if (!clinicExists)
        {
            logger.LogInformation("Seeding default development clinic...");
            var defaultClinic = new Clinic
            {
                Id = DefaultClinicId,
                Name = "PTDoc Development Clinic",
                Slug = "ptdoc-dev",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Clinics.Add(defaultClinic);
            await context.SaveChangesAsync();
            logger.LogInformation("Default clinic seeded.");
        }

        // Check if we already have users
        var hasUsers = await context.Users.AnyAsync();
        if (hasUsers)
        {
            // Existing users: attach testuser to the default clinic if it's not already assigned.
            var testUser = await context.Users
                .FirstOrDefaultAsync(u => u.Username == "testuser");
            if (testUser != null && testUser.ClinicId == null)
            {
                testUser.ClinicId = DefaultClinicId;
                await context.SaveChangesAsync();
                logger.LogInformation("Attached testuser to default development clinic.");
            }
            logger.LogInformation("Database already contains users, skipping full seed");
            return;
        }

        logger.LogInformation("Seeding test data...");

        // Create system user (for background operations — no clinic assignment)
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

        // Create test user for development — assigned to the default clinic
        var newTestUser = new User
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
            LicenseExpirationDate = DateTime.UtcNow.AddYears(2),
            ClinicId = DefaultClinicId // Sprint J: assign to default clinic
        };

        context.Users.AddRange(systemUser, newTestUser);
        await context.SaveChangesAsync();

        logger.LogInformation("Test data seeded successfully. Test user: {Username} (PIN configured separately)", "testuser");
    }
}
