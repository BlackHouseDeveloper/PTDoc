using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Data;
using PTDoc.Models;
using PTDoc.Services;

namespace PTDoc.Tests.Audit;

/// <summary>
/// Verifies that audit log entries are written for note edits, signatures, and exports.
/// </summary>
[Trait("Category", "Audit")]
public sealed class AuditLoggingTests : IAsyncDisposable
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid NoteId = Guid.NewGuid();
    private const string UserId = "test-user";

    private readonly SqliteConnection _connection;
    private readonly PTDocDbContext _context;
    private readonly AuditService _auditService;

    public AuditLoggingTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PTDocDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new PTDocDbContext(options);
        _context.Database.EnsureCreated();
        _auditService = new AuditService(_context, NullLogger<AuditService>.Instance);
    }

    [Fact]
    public async Task LogNoteEdited_WritesAuditEntry()
    {
        await _auditService.LogNoteEditedAsync(ClinicId, NoteId, UserId);

        var entry = Assert.Single(await _context.AuditLogs.ToListAsync());
        Assert.Equal("NoteEdited", entry.EventType);
        Assert.Equal(NoteId, entry.EntityId);
        Assert.Equal(ClinicId, entry.ClinicId);
        Assert.Equal(UserId, entry.UserId);
    }

    [Fact]
    public async Task LogNoteSigned_WritesAuditEntry()
    {
        await _auditService.LogNoteSignedAsync(ClinicId, NoteId, UserId);

        var entry = Assert.Single(await _context.AuditLogs.ToListAsync());
        Assert.Equal("NoteSigned", entry.EventType);
        Assert.Equal(NoteId, entry.EntityId);
    }

    [Fact]
    public async Task LogNoteExported_WritesAuditEntry()
    {
        await _auditService.LogNoteExportedAsync(ClinicId, NoteId, UserId);

        var entry = Assert.Single(await _context.AuditLogs.ToListAsync());
        Assert.Equal("NoteExported", entry.EventType);
        Assert.Equal(NoteId, entry.EntityId);
    }

    [Fact]
    public async Task MultipleAuditEvents_AllPersisted()
    {
        await _auditService.LogNoteEditedAsync(ClinicId, NoteId, UserId);
        await _auditService.LogNoteSignedAsync(ClinicId, NoteId, UserId);
        await _auditService.LogNoteExportedAsync(ClinicId, NoteId, UserId);

        var entries = await _context.AuditLogs.ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.EventType == "NoteEdited");
        Assert.Contains(entries, e => e.EventType == "NoteSigned");
        Assert.Contains(entries, e => e.EventType == "NoteExported");
    }

    [Fact]
    public async Task AuditEntry_HasCorrectEntityType()
    {
        await _auditService.LogNoteEditedAsync(ClinicId, NoteId, UserId);

        var entry = Assert.Single(await _context.AuditLogs.ToListAsync());
        Assert.Equal(nameof(SOAPNote), entry.EntityType);
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
