using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using Xunit;

namespace PTDoc.Tests.Security;

/// <summary>
/// Sprint G: Tests validating audit event recording for authentication operations.
///
/// Verifies:
///  1. Login success/failure events are written to the AuditLogs table.
///  2. Audit metadata for auth events contains NO PHI (no PIN, no raw token, no patient data).
///  3. AuditEvent factory methods produce correctly classified events.
///
/// Decision reference: Sprint G — Security Hardening and Compliance Guardrails.
/// </summary>
public class AuthAuditTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;
    private readonly AuditService _auditService;

    public AuthAuditTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.Migrate();

        _auditService = new AuditService(_context);
    }

    // ─── AuditEvent factory method tests ────────────────────────────────────

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_LoginSuccess_HasCorrectEventType()
    {
        var userId = Guid.NewGuid();
        var evt = AuditEvent.LoginSuccess(userId, "192.168.1.1");

        Assert.Equal("LoginSuccess", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(userId, evt.UserId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_LoginSuccess_MetadataContainsNoPin()
    {
        var userId = Guid.NewGuid();
        var evt = AuditEvent.LoginSuccess(userId, "10.0.0.1");

        // Metadata must NOT contain any PIN, password, or token
        Assert.DoesNotContain("Pin", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_LoginFailed_HasCorrectEventType()
    {
        var evt = AuditEvent.LoginFailed("127.0.0.1", "InvalidCredentials");

        Assert.Equal("LoginFailed", evt.EventType);
        Assert.Equal("Warning", evt.Severity);
        Assert.False(evt.Success);
        Assert.Equal("InvalidCredentials", evt.ErrorMessage);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_LoginFailed_MetadataContainsNoCredentials()
    {
        var evt = AuditEvent.LoginFailed("127.0.0.1", "InvalidCredentials");

        // Metadata must NOT contain any PIN, password, or username
        Assert.DoesNotContain("Pin", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_Logout_HasCorrectEventType()
    {
        var userId = Guid.NewGuid();
        var evt = AuditEvent.Logout(userId);

        Assert.Equal("Logout", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(userId, evt.UserId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_TokenValidationFailed_HasCorrectEventTypeAndNoToken()
    {
        var evt = AuditEvent.TokenValidationFailed("10.0.0.5", "SecurityTokenExpiredException");

        Assert.Equal("TokenValidationFailed", evt.EventType);
        Assert.Equal("Warning", evt.Severity);
        Assert.False(evt.Success);

        // No raw token value in metadata
        Assert.DoesNotContain("Token", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SigningKey", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    // ─── AuditService persistence tests ─────────────────────────────────────

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogAuthEventAsync_LoginSuccess_PersistsToAuditLog()
    {
        var userId = Guid.NewGuid();
        var evt = AuditEvent.LoginSuccess(userId, "192.168.0.1");

        await _auditService.LogAuthEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "LoginSuccess")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("LoginSuccess", record.EventType);
        Assert.Equal(userId, record.UserId);
        Assert.True(record.Success);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogAuthEventAsync_LoginFailed_PersistsWithWarningAndNoCredentials()
    {
        var evt = AuditEvent.LoginFailed("10.10.10.10", "UserNotFound");

        await _auditService.LogAuthEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "LoginFailed")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("Warning", record.Severity);
        Assert.False(record.Success);

        // Audit metadata JSON must not contain credentials
        Assert.DoesNotContain("pin", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogAuthEventAsync_Logout_PersistsWithUserId()
    {
        var userId = Guid.NewGuid();
        var evt = AuditEvent.Logout(userId);

        await _auditService.LogAuthEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "Logout")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal(userId, record.UserId);
        Assert.True(record.Success);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogAuthEventAsync_TokenValidationFailed_PersistsWithNoRawToken()
    {
        var evt = AuditEvent.TokenValidationFailed("172.16.0.1", "SecurityTokenExpiredException");

        await _auditService.LogAuthEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "TokenValidationFailed")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("Warning", record.Severity);

        // Metadata JSON must not contain raw token value
        Assert.DoesNotContain("eyJ", record.MetadataJson);    // JWT header prefix
        Assert.DoesNotContain("signingkey", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Multiple auth events ─────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Security")]
    public async Task MultipleAuthEvents_AllPersisted_InChronologicalOrder()
    {
        var userId = Guid.NewGuid();

        await _auditService.LogAuthEventAsync(AuditEvent.LoginFailed("1.2.3.4", "InvalidCredentials"));
        await _auditService.LogAuthEventAsync(AuditEvent.LoginSuccess(userId, "1.2.3.4"));
        await _auditService.LogAuthEventAsync(AuditEvent.Logout(userId));

        var records = await _context.AuditLogs
            .Where(a => a.EventType == "LoginFailed" || a.EventType == "LoginSuccess" || a.EventType == "Logout")
            .OrderBy(a => a.TimestampUtc)
            .ToListAsync();

        Assert.Equal(3, records.Count);
        Assert.Equal("LoginFailed", records[0].EventType);
        Assert.Equal("LoginSuccess", records[1].EventType);
        Assert.Equal("Logout", records[2].EventType);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
