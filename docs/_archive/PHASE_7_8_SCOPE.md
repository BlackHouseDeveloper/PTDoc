# Phase 7 & 8 Scope Clarification

## Phase 7: Security & Observability Foundation (COMPLETE)

**Scope: Abstractions, Interfaces, and Server-Side Implementations Only**

### 7.1 SQLite Encryption - Abstraction Layer
✅ **Implemented:**
- `IDbKeyProvider` interface (Application layer)
- `EnvironmentDbKeyProvider` for API/server (uses env var or dev default)
- Key validation (32+ character requirement for SQLCipher compatibility)

❌ **Explicitly Deferred to Phase 8:**
- SQLCipher end-to-end wiring (connection string configuration)
- EF Core migration compatibility with encrypted database
- MAUI `SecureStorageDbKeyProvider` implementation
- Platform-specific key rotation testing

**Rationale:** Phase 7 establishes the abstraction layer and server-side implementation. Platform-specific MAUI integration requires SecureStorage which is platform-dependent and needs Android/iOS/MacCatalyst testing.

---

### 7.2 Telemetry & Structured Logging
✅ **Implemented:**
- `ITelemetrySink` interface for vendor-agnostic telemetry
- `ConsoleTelemetrySink` for development logging
- Structured event contracts with NO PHI enforcement
- Correlation ID support in audit events

✅ **Complete:** No deferrals.

---

### 7.3 PDF Export - Contract & Skeleton
✅ **Implemented:**
- `IPdfRenderer` interface (Application layer)
- `MockPdfRenderer` with signature blocks and Medicare compliance sections
- POST `/api/v1/notes/{noteId}/export/pdf` endpoint (normalized RESTful path)
- Audit logging for PDF exports (NO PHI - metadata only)
- PDF does NOT modify note or SyncState

❌ **Explicitly Deferred to Phase 8:**
- Real QuestPDF implementation (replace MockPdfRenderer)
- Server-side rendering with actual PDF library
- Storage abstraction (local file → Azure Blob stub)
- Fax-ready formatting validation

**Rationale:** Phase 7 provides the endpoint contract, audit logging, and layout skeleton. Real PDF rendering requires QuestPDF library integration and server-side testing which is out of scope for the abstraction-focused Phase 7.

---

### 7.4 CodeQL Security Scanning
✅ **Implemented:**
- GitHub CodeQL workflow for C# security scanning
- Runs on push/PR and weekly schedule
- Fails CI on critical/high severity alerts

✅ **Complete:** No deferrals.

---

## Phase 8: Platform Implementations & Integration Testing (PENDING)

**Scope: End-to-End Implementations and Cross-Platform Validation**

### 8.1 SQLCipher End-to-End Integration
- [ ] SQLCipher NuGet package integration
- [ ] Connection string configuration with runtime key injection
- [ ] EF Core migration compatibility testing with encrypted database
- [ ] MAUI `SecureStorageDbKeyProvider` implementation
- [ ] Platform-specific testing (Android, iOS, MacCatalyst)
- [ ] Key rotation failure testing (controlled failure, no silent corruption)

### 8.2 Real PDF Renderer Implementation
- [ ] QuestPDF library integration
- [ ] Replace MockPdfRenderer with QuestPdfRenderer
- [ ] Server-side rendering validation
- [ ] PDF storage abstraction (local file + Azure Blob stub)
- [ ] Fax-ready formatting verification (monochrome safe, clean margins)

### 8.3 Integration Tests
- [ ] EF Core migration tests (encrypted + unencrypted databases)
- [ ] RBAC enforcement tests across all endpoints
- [ ] End-to-end sync tests (offline → reconnect → sync → conflict resolution)
- [ ] Cross-platform build matrix validation

### 8.4 Platform-Specific Validation
- [ ] **Android**: APK builds, SecureStorage key provider, encrypted DB read/write
- [ ] **iOS**: IPA builds (unsigned), SecureStorage key provider, encrypted DB read/write
- [ ] **macOS**: Catalyst build, SecureStorage key provider, encrypted DB read/write
- [ ] **Web**: Blazor Web App loads with API backend

---

## Endpoint Naming Convention (Normalized in Phase 7)

**Decision:** Use RESTful resource-based paths

✅ **Adopted Convention:**
```
POST /api/v1/notes/{noteId}/export/pdf
POST /api/v1/notes/{noteId}/sign
POST /api/v1/notes/{noteId}/addendum
GET  /api/v1/notes/{noteId}/verify-signature
```

❌ **Rejected Convention:**
```
POST /api/v1/pdf/notes/{noteId}/export  (groups by action, not resource)
```

**Rationale:** RESTful design groups endpoints by resource (clinical notes), making the API more intuitive and consistent with existing note-related operations.

---

## Testing Strategy

**Phase 7 Testing (Unit Tests Only):**
- 11 unit tests for Phase 7 components
- All abstractions and server-side implementations tested
- NO platform-specific testing (deferred to Phase 8)

**Phase 8 Testing (Integration + Platform Tests):**
- Integration tests for EF migrations with encrypted databases
- Platform-specific tests for MAUI SecureStorage integration
- Cross-platform build validation
- End-to-end workflow tests

---

## Summary

**Phase 7 Status:** ✅ **COMPLETE**
- All abstractions and server-side implementations delivered
- 59/59 unit tests passing
- CodeQL security scanning enabled
- Foundation ready for Phase 8 platform implementations

**Phase 8 Scope:** Clearly defined with specific deliverables for:
- SQLCipher end-to-end integration with MAUI SecureStorage
- Real PDF renderer with QuestPDF
- Comprehensive integration and platform-specific testing

**Key Principle:** Phase 7 establishes contracts and server-side implementations. Phase 8 delivers platform-specific integrations and validates the complete system end-to-end.
