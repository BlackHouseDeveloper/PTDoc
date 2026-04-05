using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Compliance;
using PTDoc.Application.Intake;
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
    /// <summary>JWT compact serialization always starts with this Base64url-encoded header prefix.</summary>
    private const string JwtHeaderPrefix = "eyJ";

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

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_AddendumCreated_HasCorrectEventTypeAndNoPhi()
    {
        var noteId = Guid.NewGuid();
        var addendumId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.AddendumCreated(noteId, addendumId, userId);

        Assert.Equal("ADDENDUM_CREATE", evt.EventType);
        Assert.Equal("ClinicalNote", evt.EntityType);
        Assert.Equal(noteId, evt.EntityId);
        Assert.Equal(userId, evt.UserId);
        Assert.DoesNotContain("Patient", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_EditBlockedSignedNote_HasCorrectEventTypeAndNoPhi()
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.EditBlockedSignedNote(noteId, userId, "SyncEngine.ReceiveClientPushAsync");

        Assert.Equal("EDIT_BLOCKED_SIGNED_NOTE", evt.EventType);
        Assert.Equal("ClinicalNote", evt.EntityType);
        Assert.Equal(noteId, evt.EntityId);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("SyncEngine.ReceiveClientPushAsync", evt.Metadata["Source"]);
        Assert.DoesNotContain("Patient", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_OverrideApplied_HasCorrectEventTypeAndNoPhi()
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.OverrideApplied(noteId, ComplianceRuleType.EightMinuteRule, userId);

        Assert.Equal("OVERRIDE_APPLIED", evt.EventType);
        Assert.Equal("ClinicalNote", evt.EntityType);
        Assert.Equal(noteId, evt.EntityId);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("EightMinuteRule", evt.Metadata["ruleType"]);
        Assert.DoesNotContain("reason", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Patient", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_HardStopTriggered_HasCorrectEventTypeAndNoPhi()
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.HardStopTriggered(noteId, ComplianceRuleType.ProgressNoteRequired, userId);

        Assert.Equal("HARD_STOP_TRIGGERED", evt.EventType);
        Assert.Equal("ClinicalNote", evt.EntityType);
        Assert.Equal(noteId, evt.EntityId);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("ProgressNoteRequired", evt.Metadata["ruleType"]);
        Assert.DoesNotContain("Patient", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
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
        Assert.DoesNotContain(JwtHeaderPrefix, record.MetadataJson);    // JWT header prefix
        Assert.DoesNotContain("signingkey", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogRuleOverrideAsync_OverrideApplied_PersistsToAuditLog()
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.OverrideApplied(noteId, ComplianceRuleType.EightMinuteRule, userId);

        await _auditService.LogRuleOverrideAsync(evt);

        var record = await _context.AuditLogs.SingleAsync(a => a.EventType == "OVERRIDE_APPLIED");
        Assert.Equal("ClinicalNote", record.EntityType);
        Assert.Equal(noteId, record.EntityId);
        Assert.Equal(userId, record.UserId);
        Assert.True(record.Success);
        Assert.Contains("\"ruleType\":\"EightMinuteRule\"", record.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"reason\":", record.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogRuleEvaluationAsync_HardStopTriggered_PersistsToAuditLog()
    {
        var noteId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.HardStopTriggered(noteId, ComplianceRuleType.ProgressNoteRequired, userId);

        await _auditService.LogRuleEvaluationAsync(evt);

        var record = await _context.AuditLogs.SingleAsync(a => a.EventType == "HARD_STOP_TRIGGERED");
        Assert.Equal("ClinicalNote", record.EntityType);
        Assert.Equal(noteId, record.EntityId);
        Assert.Equal(userId, record.UserId);
        Assert.False(record.Success);
        Assert.Contains("\"ruleType\":\"ProgressNoteRequired\"", record.MetadataJson, StringComparison.Ordinal);
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

    // ─── Intake audit events (Sprint UC-Beta) ────────────────────────────────

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeSubmitted_HasCorrectEventType()
    {
        var intakeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.IntakeSubmitted(intakeId, userId);

        Assert.Equal("IntakeSubmitted", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("IntakeForm", evt.EntityType);
        Assert.Equal(intakeId, evt.EntityId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeLocked_HasCorrectEventType()
    {
        var intakeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.IntakeLocked(intakeId, userId);

        Assert.Equal("IntakeLocked", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("IntakeForm", evt.EntityType);
        Assert.Equal(intakeId, evt.EntityId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeReviewed_HasCorrectEventType()
    {
        var intakeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var evt = AuditEvent.IntakeReviewed(intakeId, reviewerId);

        Assert.Equal("IntakeReviewed", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(reviewerId, evt.UserId);
        Assert.Equal("IntakeForm", evt.EntityType);
        Assert.Equal(intakeId, evt.EntityId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeSubmitted_MetadataContainsNoPhiFields()
    {
        var evt = AuditEvent.IntakeSubmitted(Guid.NewGuid(), Guid.NewGuid());

        // Metadata must not contain PHI such as patient name, DOB, or clinical content
        Assert.DoesNotContain("PatientName", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DateOfBirth", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponseJson", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Consents", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeSubmitted_MergesConsentSummaryWithoutPhi()
    {
        var summary = IntakeConsentJson.CreateAuditSummary(new IntakeConsentPacket
        {
            HipaaAcknowledged = true,
            CommunicationEmailConsent = true,
            CommunicationEmail = "patient@example.com",
            AuthorizedContacts =
            [
                new AuthorizedContact { Name = "Pat Smith", PhoneNumber = "555-0100", Relationship = "Parent" }
            ]
        });

        var evt = AuditEvent.IntakeSubmitted(Guid.NewGuid(), Guid.NewGuid(), summary);

        Assert.True(evt.Metadata.ContainsKey("HipaaAcknowledged"));
        Assert.True(evt.Metadata.ContainsKey("AuthorizedContactCount"));
        Assert.DoesNotContain("CommunicationEmail", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunicationPhoneNumber", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Name", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogIntakeEventAsync_IntakeSubmitted_PersistsToAuditLog()
    {
        var intakeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.IntakeSubmitted(intakeId, userId);

        await _auditService.LogIntakeEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "IntakeSubmitted")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("IntakeSubmitted", record.EventType);
        Assert.Equal(userId, record.UserId);
        Assert.Equal("IntakeForm", record.EntityType);
        Assert.Equal(intakeId, record.EntityId);
        Assert.True(record.Success);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogIntakeEventAsync_IntakeReviewed_PersistsToAuditLog()
    {
        var intakeId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var evt = AuditEvent.IntakeReviewed(intakeId, reviewerId);

        await _auditService.LogIntakeEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "IntakeReviewed")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("IntakeReviewed", record.EventType);
        Assert.Equal(reviewerId, record.UserId);
        Assert.Equal("IntakeForm", record.EntityType);
        Assert.Equal(intakeId, record.EntityId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEvent_IntakeConsentRevoked_HasCorrectEventTypeAndNoPhi()
    {
        var intakeId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var evt = AuditEvent.IntakeConsentRevoked(
            intakeId,
            userId,
            ["hipaaAcknowledged", "communicationEmailConsent"],
            hasWrittenReference: true);

        Assert.Equal("IntakeConsentRevoked", evt.EventType);
        Assert.Equal("Info", evt.Severity);
        Assert.True(evt.Success);
        Assert.Equal(userId, evt.UserId);
        Assert.Equal("IntakeForm", evt.EntityType);
        Assert.Equal(intakeId, evt.EntityId);
        Assert.DoesNotContain("PatientName", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DateOfBirth", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunicationEmail", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CommunicationPhoneNumber", evt.Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task LogIntakeEventAsync_IntakeConsentRevoked_PersistsToAuditLog()
    {
        var intakeId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var evt = AuditEvent.IntakeConsentRevoked(
            intakeId,
            userId,
            ["hipaaAcknowledged"],
            hasWrittenReference: false);

        await _auditService.LogIntakeEventAsync(evt);

        var record = await _context.AuditLogs
            .Where(a => a.EventType == "IntakeConsentRevoked")
            .FirstOrDefaultAsync();

        Assert.NotNull(record);
        Assert.Equal("IntakeConsentRevoked", record.EventType);
        Assert.Equal(userId, record.UserId);
        Assert.Equal("IntakeForm", record.EntityType);
        Assert.Equal(intakeId, record.EntityId);
        Assert.DoesNotContain("patient@example.com", record.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
