# Phase 8 Implementation Plan

## Overview
Phase 8 delivers platform-specific integrations and end-to-end testing for the offline-first backend foundation completed in Phase 7.

## Implementation Order

### 1. SQLCipher End-to-End Integration ✅ IN PROGRESS
**Priority: HIGH** - Critical for HIPAA-compliant data-at-rest encryption

#### 1.1 Package Installation
- [ ] Add `Microsoft.Data.Sqlite.Core` to PTDoc.Infrastructure
- [ ] Add `SQLitePCLRaw.bundle_e_sqlcipher` to PTDoc.Infrastructure (SQLCipher support)
- [ ] Add `SQLitePCLRaw.provider.dynamic_cdecl` for cross-platform support

#### 1.2 Connection String Wiring
- [ ] Create `SqlCipherConnectionStringBuilder` helper in Infrastructure
- [ ] Wire `IDbKeyProvider` into `ApplicationDbContext` configuration
- [ ] Add PRAGMA key configuration for SQLCipher
- [ ] Ensure migrations work with encrypted database

#### 1.3 MAUI SecureStorageDbKeyProvider
- [ ] Create `SecureStorageDbKeyProvider` in PTDoc.Maui project
- [ ] Implement platform-specific key generation (32+ chars)
- [ ] Implement key persistence in MAUI SecureStorage
- [ ] Add fail-closed behavior when SecureStorage unavailable

#### 1.4 Testing
- [ ] Unit test: Key length validation (32+ chars)
- [ ] Integration test: Encrypted DB open and read/write
- [ ] Integration test: Unencrypted DB still works (backwards compatibility)
- [ ] Integration test: EF migrations apply to encrypted DB

---

### 2. QuestPDF Real Renderer ✅ IN PROGRESS
**Priority: HIGH** - Required for production PDF export

#### 2.1 Package Installation
- [ ] Add `QuestPDF` to PTDoc.Infrastructure
- [ ] Verify QuestPDF license compatibility (Community MIT)

#### 2.2 QuestPdfRenderer Implementation
- [ ] Create `QuestPdfRenderer` implementing `IPdfRenderer`
- [ ] Implement signature footer (SignedBy, SignedUtc, Hash)
- [ ] Implement "UNSIGNED DRAFT" watermark for unsigned notes
- [ ] Implement Medicare compliance block (CPT summary, 8-minute rule, PN frequency)
- [ ] Implement fax-ready formatting (monochrome safe, clean margins)

#### 2.3 Replace MockPdfRenderer
- [ ] Update DI registration in Program.cs
- [ ] Preserve MockPdfRenderer for testing purposes
- [ ] Ensure PDF export does NOT mutate note or SyncState

#### 2.4 Testing
- [ ] Unit test: Signed note generates PDF with signature footer
- [ ] Unit test: Unsigned note shows "UNSIGNED DRAFT" watermark
- [ ] Unit test: Medicare compliance block includes CPT summary
- [ ] Integration test: Generated PDF is valid and non-empty

---

### 3. Integration Tests ✅ IN PROGRESS
**Priority: HIGH** - Required for production readiness

#### 3.1 Migration Tests
- [ ] Test: EF migrations apply to encrypted database
- [ ] Test: EF migrations apply to unencrypted database (backwards compat)
- [ ] Test: Encrypted DB can be opened after migration
- [ ] Test: Data persists correctly in encrypted DB

#### 3.2 RBAC Enforcement Tests
- [ ] Test: Auth endpoints require valid session token
- [ ] Test: Sync endpoints require authentication
- [ ] Test: Compliance endpoints require authentication
- [ ] Test: AI endpoints require authentication + feature flag
- [ ] Test: Integration endpoints require authentication + feature flag
- [ ] Test: PDF export endpoints require authentication

#### 3.3 NO PHI Validation Tests
- [ ] Test: Telemetry events contain NO patient names
- [ ] Test: Telemetry events contain NO note content
- [ ] Test: Audit logs contain NO clinical data
- [ ] Test: Audit logs contain only entity IDs and metadata

#### 3.4 End-to-End Sync Tests
- [ ] Test: Offline edit creates queue item
- [ ] Test: Reconnect triggers sync
- [ ] Test: Queue drains correctly
- [ ] Test: Draft conflict resolves with Last-Write-Wins
- [ ] Test: Signed note conflict rejects update (immutable)

---

### 4. Platform Build Validation ⏸️ DEFERRED
**Priority: MEDIUM** - Requires CI/CD infrastructure

#### 4.1 Android (net8.0-android)
- [ ] Verify PTDoc.Maui builds for Android
- [ ] Verify SecureStorageDbKeyProvider works on Android
- [ ] Verify encrypted DB open/read/write on Android

#### 4.2 iOS (net8.0-ios)
- [ ] Verify PTDoc.Maui builds for iOS
- [ ] Verify SecureStorageDbKeyProvider works on iOS
- [ ] Verify encrypted DB open/read/write on iOS

#### 4.3 macOS (net8.0-maccatalyst)
- [ ] Verify PTDoc.Maui builds for macOS Catalyst
- [ ] Verify SecureStorageDbKeyProvider works on macOS
- [ ] Verify encrypted DB open/read/write on macOS

#### 4.4 Web (Blazor Web App)
- [ ] Verify PTDoc.Web builds successfully
- [ ] Verify PTDoc.Web does NOT reference EF packages
- [ ] Verify API backend connection works

---

## Hard Constraints (Enforced)

✅ **NO UI changes** - Backend/infrastructure only
✅ **Clean Architecture boundaries** - EF packages ONLY in Infrastructure
✅ **All 59 existing tests passing** - No regressions
✅ **NO PHI in logs/telemetry** - Metadata only
✅ **PDF export immutable** - Does NOT modify note or SyncState
✅ **Encrypted key validated** - 32+ characters required

---

## Success Criteria

**Phase 8 Complete When:**
1. ✅ SQLCipher end-to-end wiring complete (API + MAUI hosts)
2. ✅ MAUI SecureStorageDbKeyProvider implemented with fail-closed behavior
3. ✅ QuestPDF renderer replaces MockPdfRenderer
4. ✅ All integration tests passing (migrations, RBAC, NO PHI, sync)
5. ✅ Platform builds succeed (Android, iOS, macOS)
6. ✅ All 59+ tests passing (no regressions)

---

## Implementation Status

**Current Status:** Starting Phase 8 implementation
**Last Updated:** 2026-02-17

