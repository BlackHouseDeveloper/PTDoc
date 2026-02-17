# Phase 8 Design Specifications

**Purpose:** Detailed technical design addressing the 6 guardrails before implementation.

**Created:** 2026-02-17  
**Status:** Pre-Implementation Design Review

---

## 🔒 Guardrail #1: Encryption Must Be Toggleable

### Configuration Design

**Location:** `appsettings.json` and `appsettings.Development.json`

```json
{
  "Database": {
    "Path": "PTDoc.db",
    "Encryption": {
      "Enabled": false,
      "KeyMinimumLength": 32
    }
  }
}
```

### Behavioral Rules

| Scenario | Enabled | Key Present | Key Length | Behavior |
|----------|---------|-------------|------------|----------|
| Development Default | `false` | N/A | N/A | Normal SQLite (existing behavior) |
| Production Encrypted | `true` | ✅ Yes | ≥32 chars | SQLCipher with encryption |
| Production Encrypted | `true` | ❌ Missing | N/A | **FAIL CLOSED** - Throw at startup |
| Production Encrypted | `true` | ✅ Yes | <32 chars | **FAIL CLOSED** - Throw at startup |
| Development Testing | `true` | ✅ Yes (dev key) | ≥32 chars | SQLCipher with dev key |

### Fail-Closed Logic

```csharp
// In Program.cs during DbContext configuration
var encryptionEnabled = builder.Configuration.GetValue<bool>("Database:Encryption:Enabled");

if (encryptionEnabled)
{
    // MUST validate key before configuring DbContext
    var keyProvider = serviceProvider.GetRequiredService<IDbKeyProvider>();
    await keyProvider.ValidateAsync(); // Throws if key invalid/missing
    
    var key = await keyProvider.GetKeyAsync();
    
    if (key.Length < 32)
    {
        throw new InvalidOperationException(
            "Database encryption key must be at least 32 characters for SQLCipher.");
    }
    
    // Use encrypted connection
    var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();
    await connection.ExecuteNonQueryAsync($"PRAGMA key = '{key}'");
    
    options.UseSqlite(connection);
}
else
{
    // Normal SQLite (existing behavior)
    options.UseSqlite($"Data Source={dbPath}");
}
```

### Key Points

✅ **Default is `false`** - Existing development workflows unchanged  
✅ **Explicit opt-in** - Production must set `Encryption.Enabled: true`  
✅ **Fail closed** - Invalid configuration throws at startup, NOT runtime  
✅ **Backwards compatible** - Existing unencrypted DBs continue working when `Enabled: false`

---

## 🔒 Guardrail #2: Connection Must Be Opened Before EF Uses It

### Critical Flow

**Problem:** If EF opens the connection internally, SQLCipher PRAGMA key is never applied → DB appears to work but is NOT encrypted.

**Solution:** Pre-open connection and set PRAGMA key BEFORE handing to EF.

### Implementation Pattern

```csharp
// WRONG - EF opens connection internally, PRAGMA never applied
options.UseSqlite($"Data Source={dbPath};Password={key}");

// CORRECT - Pre-open, set PRAGMA, hand to EF
var connection = new SqliteConnection($"Data Source={dbPath}");
await connection.OpenAsync();
await connection.ExecuteNonQueryAsync($"PRAGMA key = '{key}'");
options.UseSqlite(connection);
```

### DbContext Configuration Flow

```
1. Check if Encryption.Enabled = true
2. If true:
   a. Get IDbKeyProvider from DI
   b. Await ValidateAsync() → throws if misconfigured
   c. Await GetKeyAsync() → retrieve key
   d. Validate key length ≥ 32 chars
   e. Create SqliteConnection with Data Source only
   f. OpenAsync() the connection
   g. ExecuteNonQueryAsync("PRAGMA key = '...'")
   h. Pass open connection to UseSqlite(connection)
3. If false:
   a. Use existing UseSqlite($"Data Source={dbPath}")
```

### Verification

✅ **Connection state:** Must be `Open` before EF receives it  
✅ **PRAGMA execution:** Must succeed (no exceptions)  
✅ **Migrations:** Must apply to already-open encrypted connection  
✅ **EF does NOT re-open:** Connection is reused, not recreated

---

## 🔒 Guardrail #3: MAUI SecureStorage Must Fail Closed

### Fail-Closed Behavior Matrix

| Platform | SecureStorage Available | Key Exists | Behavior |
|----------|------------------------|------------|----------|
| **Android** | ✅ Yes | ✅ Yes | Return existing key |
| **Android** | ✅ Yes | ❌ No | Generate + store new 32-char key |
| **Android** | ❌ No | N/A | **THROW** - App does NOT start |
| **iOS** | ✅ Yes | ✅ Yes | Return existing key |
| **iOS** | ✅ Yes | ❌ No | Generate + store new 32-char key |
| **iOS** | ❌ No | N/A | **THROW** - App does NOT start |
| **macOS** | ✅ Yes | ✅ Yes | Return existing key |
| **macOS** | ✅ Yes | ❌ No | Generate + store new 32-char key |
| **macOS** | ❌ No | N/A | **THROW** - App does NOT start |

### Implementation: SecureStorageDbKeyProvider

**Location:** `src/PTDoc.Maui/Security/SecureStorageDbKeyProvider.cs`

```csharp
using PTDoc.Application.Security;
using System.Security.Cryptography;

namespace PTDoc.Maui.Security;

public class SecureStorageDbKeyProvider : IDbKeyProvider
{
    private const string KeyName = "PTDoc.DbEncryptionKey";
    private const int KeyLengthBytes = 32;

    public async Task<string> GetKeyAsync()
    {
        try
        {
            // Try to retrieve existing key
            var existingKey = await SecureStorage.GetAsync(KeyName);
            
            if (!string.IsNullOrWhiteSpace(existingKey))
            {
                return existingKey;
            }
            
            // Generate new key if none exists
            var newKey = GenerateSecureKey();
            await SecureStorage.SetAsync(KeyName, newKey);
            
            return newKey;
        }
        catch (Exception ex)
        {
            // FAIL CLOSED - SecureStorage unavailable
            throw new InvalidOperationException(
                "Cannot retrieve database encryption key. SecureStorage is unavailable on this device. " +
                "PTDoc cannot start without secure key storage.", ex);
        }
    }

    public async Task ValidateAsync()
    {
        // Attempt retrieval to validate SecureStorage is available
        await GetKeyAsync();
    }

    private static string GenerateSecureKey()
    {
        var keyBytes = new byte[KeyLengthBytes];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyBytes);
        return Convert.ToBase64String(keyBytes); // 44 chars (>32 required)
    }
}
```

### NO Fallback in MAUI Production

❌ **DO NOT** use dev keys in MAUI  
❌ **DO NOT** allow app to continue if SecureStorage fails  
✅ **THROW** exception at startup if SecureStorage unavailable

### API Host Fallback (Development Only)

```csharp
// EnvironmentDbKeyProvider.cs (API host only)
if (string.IsNullOrWhiteSpace(key))
{
    // ONLY in Development environment
    if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
    {
        return await Task.FromResult("dev-encryption-key-minimum-32-chars-required-for-sqlcipher");
    }
    
    // Production MUST provide key
    throw new InvalidOperationException(
        $"Database encryption key not found. Set {KeyEnvironmentVariable} environment variable.");
}
```

---

## 🔒 Guardrail #4: QuestPDF Must Be Infrastructure-Only

### Architectural Boundary

```
PTDoc.Application (Interfaces)
    ↓
    IPdfRenderer.GeneratePdfAsync(NoteExportDto dto)
    ↓
PTDoc.Infrastructure (Implementation)
    ↓
    QuestPdfRenderer : IPdfRenderer
```

### Critical Rules

❌ **NEVER** inject `DbContext` into `QuestPdfRenderer`  
❌ **NEVER** load note via `new DbContext()` inside renderer  
✅ **ONLY** receive `NoteExportDto` as parameter  
✅ **NO** database access from renderer

### Endpoint Flow

```csharp
// PdfEndpoints.cs
app.MapPost("/api/v1/notes/{noteId}/export/pdf", async (
    Guid noteId,
    ApplicationDbContext db,
    IPdfRenderer renderer,
    IIdentityContextAccessor identity) =>
{
    // 1. LOAD note from DB (in endpoint, NOT renderer)
    var note = await db.ClinicalNotes.FindAsync(noteId);
    
    // 2. MAP to DTO
    var dto = new NoteExportDto
    {
        PatientName = $"{note.PatientFirstName} {note.PatientLastName}",
        Content = note.Content,
        SignedBy = note.SignedBy,
        SignedUtc = note.SignedUtc,
        // ... etc
    };
    
    // 3. PASS DTO to renderer (no DB access)
    var pdfBytes = await renderer.GeneratePdfAsync(dto);
    
    // 4. Return PDF (do NOT modify note or SyncState)
    return Results.File(pdfBytes, "application/pdf", $"note-{noteId}.pdf");
});
```

### QuestPdfRenderer Implementation

```csharp
using PTDoc.Application.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PTDoc.Infrastructure.Pdf;

public class QuestPdfRenderer : IPdfRenderer
{
    // NO DbContext injected
    // NO constructor dependencies except configuration
    
    public async Task<byte[]> GeneratePdfAsync(NoteExportDto dto)
    {
        return await Task.Run(() =>
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(1, Unit.Inch);
                    
                    // Header
                    page.Header().Text("PTDoc Clinical Note").FontSize(20);
                    
                    // Content
                    page.Content().Column(column =>
                    {
                        column.Item().Text($"Patient: {dto.PatientName}");
                        column.Item().Text($"Created: {dto.CreatedUtc:g}");
                        column.Item().PaddingVertical(10);
                        column.Item().Text(dto.Content);
                        
                        // Signature block (if signed)
                        if (dto.SignedUtc.HasValue && !string.IsNullOrEmpty(dto.SignedBy))
                        {
                            column.Item().PaddingTop(20).Text("Signature");
                            column.Item().Text($"Signed by: {dto.SignedBy}");
                            column.Item().Text($"Signed on: {dto.SignedUtc:g}");
                            column.Item().Text($"Hash: {dto.SignatureHash}");
                        }
                        else
                        {
                            // Watermark for unsigned notes
                            column.Item().PaddingTop(20).Text("UNSIGNED DRAFT")
                                .FontColor(Colors.Red.Medium)
                                .FontSize(24);
                        }
                    });
                    
                    // Footer with Medicare compliance
                    page.Footer().Column(footer =>
                    {
                        footer.Item().Text($"CPT Codes: {dto.MedicareCptSummary}");
                        footer.Item().Text($"Total Units: {dto.Medicare8MinuteUnits}");
                        footer.Item().Text($"PN Frequency: {dto.MedicarePnFrequencyMet}");
                    });
                });
            });
            
            return document.GeneratePdf();
        });
    }
}
```

### DI Registration

```csharp
// Program.cs
// Replace MockPdfRenderer with QuestPdfRenderer
builder.Services.AddScoped<IPdfRenderer, QuestPdfRenderer>();

// Keep MockPdfRenderer available for testing
// builder.Services.AddScoped<IPdfRenderer, MockPdfRenderer>();
```

---

## 🔒 Guardrail #5: Integration Tests Must Include 5 Cases

### 1. Encryption Tests

**File:** `tests/PTDoc.Tests/Infrastructure/EncryptionIntegrationTests.cs`

```csharp
[Fact]
public async Task Migrations_Succeed_When_Encryption_Enabled()
{
    // Arrange: Create encrypted connection
    var key = GenerateTestKey();
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await connection.ExecuteNonQueryAsync($"PRAGMA key = '{key}'");
    
    // Act: Apply migrations
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(connection)
        .Options;
    
    using var context = new ApplicationDbContext(options);
    await context.Database.MigrateAsync();
    
    // Assert: DB is functional
    context.Patients.Add(new Patient { FirstName = "Test", LastName = "Patient" });
    await context.SaveChangesAsync();
    
    var patient = await context.Patients.FirstAsync();
    Assert.Equal("Test", patient.FirstName);
}

[Fact]
public async Task Migrations_Fail_When_Key_Invalid()
{
    // Arrange: Create connection with wrong key
    var connection = new SqliteConnection("Data Source=test.db");
    await connection.OpenAsync();
    await connection.ExecuteNonQueryAsync("PRAGMA key = 'wrong-key'");
    
    // Act/Assert: Migration fails
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(connection)
        .Options;
    
    using var context = new ApplicationDbContext(options);
    await Assert.ThrowsAsync<SqliteException>(() => context.Database.MigrateAsync());
}

[Fact]
public async Task Plain_Mode_Still_Works()
{
    // Arrange: Normal SQLite (no encryption)
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    
    // Act: Apply migrations without PRAGMA key
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(connection)
        .Options;
    
    using var context = new ApplicationDbContext(options);
    await context.Database.MigrateAsync();
    
    // Assert: DB works normally
    context.Patients.Add(new Patient { FirstName = "Plain", LastName = "Mode" });
    await context.SaveChangesAsync();
    
    Assert.Equal(1, await context.Patients.CountAsync());
}
```

### 2. RBAC Tests

**File:** `tests/PTDoc.Tests/Integration/RbacIntegrationTests.cs`

```csharp
[Fact]
public async Task Unauthenticated_Request_Returns_401()
{
    // Act: Call protected endpoint without token
    var response = await _client.GetAsync("/api/v1/sync/queue");
    
    // Assert
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task Wrong_Role_Returns_403()
{
    // Arrange: Login as patient (not clinician)
    var token = await LoginAsPatient();
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
    // Act: Try to sign note (requires clinician role)
    var response = await _client.PostAsync("/api/v1/notes/{noteId}/sign", null);
    
    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task Valid_Role_Returns_200()
{
    // Arrange: Login as clinician
    var token = await LoginAsClinician();
    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    
    // Act: Access allowed endpoint
    var response = await _client.GetAsync("/api/v1/sync/queue");
    
    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

### 3. NO PHI Tests

**File:** `tests/PTDoc.Tests/Security/NoPHIIntegrationTests.cs`

```csharp
[Fact]
public async Task Telemetry_Contains_No_Patient_Names()
{
    // Arrange: Create patient
    var patient = new Patient { FirstName = "John", LastName = "Doe" };
    await _db.Patients.AddAsync(patient);
    await _db.SaveChangesAsync();
    
    // Act: Trigger telemetry event
    await _telemetrySink.LogEventAsync("PatientCreated", new Dictionary<string, object>
    {
        { "EntityId", patient.Id },
        { "EventType", "Create" }
    });
    
    // Assert: Telemetry metadata contains NO names
    var lastEvent = _telemetrySink.Events.Last();
    Assert.DoesNotContain("John", lastEvent.MetadataJson);
    Assert.DoesNotContain("Doe", lastEvent.MetadataJson);
    Assert.Contains(patient.Id.ToString(), lastEvent.MetadataJson);
}

[Fact]
public async Task Audit_Logs_Contain_No_Clinical_Text()
{
    // Arrange: Create signed note
    var note = new ClinicalNote { Content = "Patient has shoulder pain" };
    await _db.ClinicalNotes.AddAsync(note);
    await _db.SaveChangesAsync();
    
    // Act: Sign note (triggers audit)
    await _auditService.LogNoteSignedAsync(note.Id, "Dr. Smith");
    
    // Assert: Audit log contains NO content
    var auditLog = await _db.AuditLogs.LastAsync();
    Assert.DoesNotContain("shoulder pain", auditLog.MetadataJson);
    Assert.Contains(note.Id.ToString(), auditLog.MetadataJson);
}

[Fact]
public async Task Sync_Queue_Contains_Only_Entity_Type_And_ID()
{
    // Arrange: Create patient
    var patient = new Patient { FirstName = "Jane", LastName = "Smith" };
    await _db.Patients.AddAsync(patient);
    await _db.SaveChangesAsync();
    
    // Act: Queue item auto-created via interceptor
    var queueItem = await _db.SyncQueueItems.FirstAsync(q => q.EntityId == patient.Id);
    
    // Assert: Queue item contains NO PHI
    Assert.Equal("Patient", queueItem.EntityType);
    Assert.Equal(patient.Id, queueItem.EntityId);
    Assert.DoesNotContain("Jane", queueItem.EntityType);
    Assert.DoesNotContain("Smith", queueItem.EntityType);
}
```

### 4. PDF Tests

**File:** `tests/PTDoc.Tests/Pdf/PdfIntegrationTests.cs`

```csharp
[Fact]
public async Task Signed_Note_Includes_Signature_Footer()
{
    // Arrange: Create signed note
    var dto = new NoteExportDto
    {
        SignedBy = "Dr. Smith",
        SignedUtc = DateTime.UtcNow,
        SignatureHash = "abc123"
    };
    
    // Act: Generate PDF
    var pdfBytes = await _renderer.GeneratePdfAsync(dto);
    
    // Assert: PDF contains signature block (basic validation)
    Assert.NotEmpty(pdfBytes);
    Assert.True(pdfBytes.Length > 1000); // Reasonable size
    
    // Advanced: Parse PDF and verify content
    var pdfText = ExtractTextFromPdf(pdfBytes);
    Assert.Contains("Signed by: Dr. Smith", pdfText);
    Assert.Contains("Hash: abc123", pdfText);
}

[Fact]
public async Task Unsigned_Note_Includes_Watermark()
{
    // Arrange: Create unsigned note
    var dto = new NoteExportDto
    {
        SignedBy = null,
        SignedUtc = null
    };
    
    // Act: Generate PDF
    var pdfBytes = await _renderer.GeneratePdfAsync(dto);
    
    // Assert: PDF contains watermark
    var pdfText = ExtractTextFromPdf(pdfBytes);
    Assert.Contains("UNSIGNED DRAFT", pdfText);
}

[Fact]
public async Task Export_Does_Not_Change_SyncState()
{
    // Arrange: Create note
    var note = new ClinicalNote { Content = "Test", SyncState = SyncState.PendingSync };
    await _db.ClinicalNotes.AddAsync(note);
    await _db.SaveChangesAsync();
    var originalSyncState = note.SyncState;
    
    // Act: Export to PDF
    var dto = MapToDto(note);
    await _renderer.GeneratePdfAsync(dto);
    
    // Assert: SyncState unchanged
    await _db.Entry(note).ReloadAsync();
    Assert.Equal(originalSyncState, note.SyncState);
}
```

### 5. Sync Tests

**File:** `tests/PTDoc.Tests/Sync/SyncIntegrationTests.cs`

```csharp
[Fact]
public async Task Encrypted_DB_Does_Not_Break_Queue_Persistence()
{
    // Arrange: Encrypted DB setup
    var key = GenerateTestKey();
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await connection.ExecuteNonQueryAsync($"PRAGMA key = '{key}'");
    
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite(connection)
        .Options;
    
    using var context = new ApplicationDbContext(options);
    await context.Database.MigrateAsync();
    
    // Act: Create sync queue item
    var queueItem = new SyncQueueItem
    {
        EntityType = "Patient",
        EntityId = Guid.NewGuid(),
        Operation = SyncOperation.Update
    };
    
    context.SyncQueueItems.Add(queueItem);
    await context.SaveChangesAsync();
    
    // Assert: Queue item persisted in encrypted DB
    var retrieved = await context.SyncQueueItems.FirstAsync();
    Assert.Equal(queueItem.EntityId, retrieved.EntityId);
}
```

---

## 🔒 Guardrail #6: Platform Validation Must Be CI-Automatable

### Build Matrix Configuration

**File:** `.github/workflows/phase8-validation.yml`

```yaml
name: Phase 8 Platform Validation

on:
  pull_request:
    branches: [main, develop]
  push:
    branches: [main, develop]

jobs:
  build-matrix:
    strategy:
      matrix:
        target:
          - net8.0
          - net8.0-android
          - net8.0-ios
          - net8.0-maccatalyst
    
    runs-on: ${{ matrix.target == 'net8.0' && 'ubuntu-latest' || 'macos-latest' }}
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      
      - name: Restore dependencies
        run: dotnet restore PTDoc.sln
      
      - name: Build ${{ matrix.target }}
        run: dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -f ${{ matrix.target }} --no-restore
      
      - name: Run tests (net8.0 only)
        if: matrix.target == 'net8.0'
        run: dotnet test --no-build --verbosity normal
```

### Manual Validation Checklist

For local development validation:

```bash
# Build all targets
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -f net8.0
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -f net8.0-android
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -f net8.0-ios
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -f net8.0-maccatalyst

# Run all tests
dotnet test PTDoc.sln

# Run StyleCop
dotnet format PTDoc.sln --verify-no-changes

# Run Roslynator
dotnet build PTDoc.sln /p:EnforceCodeStyleInBuild=true
```

---

## 🧠 Strategic Order of Execution

### Phase 8A: SQLCipher Wiring (API Host Only)

1. Add packages to PTDoc.Infrastructure
2. Add `Database.Encryption.Enabled` config to appsettings
3. Update Program.cs DbContext configuration with toggle logic
4. Add encryption integration tests
5. Validate encrypted migrations work
6. Validate plain mode still works

**Checkpoint:** 59+ tests passing, encrypted and plain modes validated

### Phase 8B: MAUI SecureStorageDbKeyProvider

1. Create `SecureStorageDbKeyProvider` in PTDoc.Maui
2. Register in MauiProgram.cs
3. Add fail-closed validation tests
4. Validate key generation and retrieval

**Checkpoint:** MAUI builds, SecureStorage integration tested

### Phase 8C: QuestPDF Replacement

1. Add QuestPDF package to PTDoc.Infrastructure
2. Implement `QuestPdfRenderer` with signature/watermark logic
3. Update DI registration in Program.cs
4. Add PDF integration tests
5. Validate signed/unsigned rendering

**Checkpoint:** PDF export functional, tests passing

### Phase 8D: Integration Tests Expansion

1. Add RBAC enforcement tests
2. Add NO PHI validation tests
3. Add end-to-end sync tests
4. Validate all 5 test categories pass

**Checkpoint:** All integration tests passing

### Phase 8E: Platform Build Validation

1. Validate net8.0-android build
2. Validate net8.0-ios build
3. Validate net8.0-maccatalyst build
4. Add CI workflow for automated validation

**Checkpoint:** All platform targets build successfully

### Phase 8F: Final Regression

1. Run full test suite (59+ tests)
2. Run StyleCop formatting
3. Run Roslynator analysis
4. Update CHANGELOG
5. Final code review

**Checkpoint:** Phase 8 complete

---

## 🚨 Risk Mitigation

### Top 3 Risks

1. **Breaking Clean Architecture**
   - **Mitigation:** Never inject DbContext into QuestPdfRenderer
   - **Validation:** Code review checks for `DbContext` references in Infrastructure/Pdf

2. **Accidentally Introducing PHI into Telemetry**
   - **Mitigation:** Add NO PHI integration tests BEFORE implementing features
   - **Validation:** Test telemetry metadata contains only entity IDs, NOT names/content

3. **Migrations Failing Under Encrypted Mode in CI**
   - **Mitigation:** Add encrypted migration tests in Phase 8A BEFORE MAUI work
   - **Validation:** In-memory encrypted DB migration tests pass

---

## ✅ Pre-Implementation Checklist

Before writing ANY SQLCipher code, confirm:

- [x] Encryption toggle design documented
- [x] Connection pre-open flow documented
- [x] MAUI fail-closed behavior documented
- [x] QuestPDF architectural boundary documented
- [x] 5 integration test cases documented
- [x] Platform validation strategy documented
- [x] Strategic execution order defined
- [x] Risk mitigation strategies defined

---

## 📋 Implementation Readiness

This design specification addresses all 6 guardrails from comment #3915836341.

**Ready to proceed with Phase 8A: SQLCipher Wiring**

