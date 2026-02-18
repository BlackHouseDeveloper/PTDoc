using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Identity;
using Xunit;

namespace PTDoc.Tests.Identity;

/// <summary>
/// Tests for AuthService covering PIN hash verification, session expiry logic.
/// </summary>
public class AuthServiceTests
{
    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidPin_ReturnsAuthResult()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var authService = new AuthService(context, NullLogger<AuthService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Test",
            LastName = "User",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act
        var result = await authService.AuthenticateAsync("testuser", "1234", "127.0.0.1", "TestAgent");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("testuser", result.Username);
        Assert.Equal("PT", result.Role);
        Assert.NotEmpty(result.Token);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);

        // Verify session was created
        var session = await context.Sessions.FirstOrDefaultAsync(s => s.UserId == user.Id);
        Assert.NotNull(session);
        Assert.False(session.IsRevoked);

        // Verify login attempt was logged
        var loginAttempt = await context.LoginAttempts.FirstOrDefaultAsync(la => la.Username == "testuser");
        Assert.NotNull(loginAttempt);
        Assert.True(loginAttempt.Success);
        Assert.Equal("127.0.0.1", loginAttempt.IpAddress);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidPin_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var authService = new AuthService(context, NullLogger<AuthService>.Instance);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PinHash = AuthService.HashPin("1234"),
            FirstName = "Test",
            LastName = "User",
            Role = "PT",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Act
        var result = await authService.AuthenticateAsync("testuser", "wrong", "127.0.0.1", "TestAgent");

        // Assert
        Assert.Null(result);

        // Verify failed login attempt was logged
        var loginAttempt = await context.LoginAttempts.FirstOrDefaultAsync(la => la.Username == "testuser" && !la.Success);
        Assert.NotNull(loginAttempt);
        Assert.False(loginAttempt.Success);
        Assert.Equal("Invalid PIN", loginAttempt.FailureReason);
    }

    [Fact]
    public void HashPin_SamePin_ProducesDifferentHashes()
    {
        // Act
        var hash1 = AuthService.HashPin("1234");
        var hash2 = AuthService.HashPin("1234");

        // Assert - BCrypt should produce different hashes due to salt
        Assert.NotEqual(hash1, hash2);

        // But both should verify successfully
        Assert.True(BCrypt.Net.BCrypt.Verify("1234", hash1));
        Assert.True(BCrypt.Net.BCrypt.Verify("1234", hash2));
    }
}
