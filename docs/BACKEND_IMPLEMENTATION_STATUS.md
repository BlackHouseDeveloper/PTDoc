# PTDoc Backend Implementation - Phase 1+2 Status

## Executive Summary

This PR establishes the **foundational layer** for PTDoc's offline-first backend system. The implementation follows Clean Architecture principles and creates a robust, type-safe foundation for all future development.

### What's Been Completed ✅

**1. Domain Model (Core Layer - ZERO dependencies)**
- All entity models with proper sync tracking (ISyncTrackedEntity)
- Signature support for immutable notes (ISignedEntity)
- Soft delete support (ISoftDeletable)
- Comprehensive enums for state machines
- Navigation properties configured correctly

**2. Application Contracts (Application Layer - interfaces only)**
- Identity & authentication abstractions
- Sync engine interfaces with conflict resolution strategies
- Medicare rules engine interfaces
- AI service interfaces (stateless by design)
- External integration interfaces (Payment, Fax, HEP)
- Patient management with deduplication
- Signature service for SHA-256 hashing
- Audit service (PHI-safe)
- Telemetry abstractions

**3. Database Context (Infrastructure Layer - persistence)**
- ApplicationDbContext with all DbSets configured
- Entity relationships properly mapped
- Indexes for performance and uniqueness
- Soft delete query filter
- Automatic sync metadata stamping
- Support for both SQLite (offline) and SQL Server (future Azure)

## Architecture Decision Records

### ADR-001: Offline-First with Deterministic Sync

**Decision**: All persisted entities implement ISyncTrackedEntity with LastModifiedUtc, ModifiedByUserId, and SyncState.

**Rationale**:
- MAUI apps need full offline functionality
- Sync must be deterministic and auditable
- Conflict resolution requires timestamps and user context
- Medicare compliance requires knowing who made changes when

**Implementation**: EF Core SaveChanges automatically stamps LastModifiedUtc and sets SyncState=Pending on modifications.

### ADR-002: Signature Immutability via Hash

**Decision**: Signed notes use SHA-256 hash of canonical content. ISignedEntity interface enforces this pattern.

**Rationale**:
- Medicare requires signed notes to be immutable
- Hash provides cryptographic proof of tampering
- Addendum workflow is the only post-sign modification path
- Canonical serialization ensures consistent hashing

**Implementation**: ISignatureService generates hash from note content in deterministic order.

### ADR-003: No Duplicate Patients Across External Systems

**Decision**: ExternalSystemMapping has unique constraint on (ExternalSystemName, ExternalId).

**Rationale**:
- Wibbi, EMR, and other integrations must not create duplicate patients
- Each external system gets exactly one mapping per PTDoc patient
- Patient merge workflow can remap external IDs

**Implementation**: Database unique index enforces constraint; IPatientService checks before creating mappings.

### ADR-004: Audit Logs Exclude PHI

**Decision**: AuditLog entity stores event type, user, entity ID, but NO PHI content.

**Rationale**:
- HIPAA compliance requires PHI minimization
- Audit logs are for security monitoring, not clinical review
- Entity ID + timestamp is sufficient for correlation

**Implementation**: IAuditService enforces structured event types; no free-form text allowed.

### ADR-005: AI Services Are Stateless

**Decision**: IAIService accepts DTOs and returns text. Zero database access. All persistence via explicit clinician save.

**Rationale**:
- AI cannot write to DB (safety + determinism)
- Clinician review is mandatory before saving AI output
- Audit trail tracks AI usage (template version + model ID) but not content

**Implementation**: AI endpoints are POST-only, return JSON with generated text, no side effects.

## Migration Strategy

### Existing Code Compatibility

The PR preserves existing interfaces:
- `ITokenService` - still used for JWT generation
- `IUserService` - will be replaced by IIdentityProvider implementation
- `ICredentialValidator` - incorporated into LocalIdentityProvider
- `ISyncService` - expanded into ISyncEngine with full conflict resolution

### Database Evolution

**Phase 1**: Create initial migration with all tables
- Users, UserSessions
- Patients, ExternalSystemMappings
- Appointments
- ClinicalNotes
- IntakeTemplates, IntakeForms
- SyncQueueItems
- AuditLogs

**Phase 2**: Seed development data
- Admin user (PIN: demo)
- Sample patients
- CPT/ICD reference data (separate seeder)

**Phase 3**: Backfill metadata for any manually created records
- Set LastModifiedUtc = CreatedAt for existing rows
- Set SyncState = Synced for stable data

## Next Implementation Steps

### Priority 1: Core Services (Required for MVP)

1. **IdentityContextAccessor** (3-4 hours)
   - Read current user from HttpContext.User claims
   - Provide UserId for ModifiedByUserId stamping
   - Used by all write operations

2. **LocalIdentityProvider** (6-8 hours)
   - PIN hashing with salted BCrypt/Argon2
   - Session creation with 15min inactivity + 8hr absolute timeout
   - Failed attempt tracking + lockout
   - Audit logging for all auth events

3. **SignatureService** (4-6 hours)
   - Canonical JSON serialization of note content
   - SHA-256 hash generation
   - Signature immutability enforcement
   - Addendum workflow

4. **MedicareRulesEngine** (8-10 hours)
   - Progress note frequency checker (10 visits OR 30 days)
   - 8-minute rule validator with CPT lookup table
   - Warning thresholds (8 visits, 25 days)
   - Structured rule evaluation results

### Priority 2: Sync & Persistence (Required for Offline)

5. **SyncEngine** (12-16 hours)
   - Queue processing in priority order
   - Conflict detection (timestamp comparison)
   - Conflict resolution strategies (draft=last-write-wins, signed=reject)
   - Sync API endpoints (push, pull, status)

6. **EF Core Migrations** (2-3 hours)
   - Create initial migration
   - Seed development data
   - Document migration workflow

### Priority 3: Patient & Audit (Required for Production)

7. **PatientService** (6-8 hours)
   - Duplicate detection algorithm (fuzzy matching on name+DOB)
   - Patient merge workflow
   - Merge audit trail

8. **AuditService** (3-4 hours)
   - Structured event logging
   - PHI exclusion enforcement
   - Entity audit history queries

### Priority 4: External Integration Stubs

9. **Payment/Fax/HEP Service Stubs** (4-6 hours total)
   - Minimal implementations for testing
   - Audit logging
   - Error handling

### Priority 5: API Endpoints

10. **API Controllers** (16-20 hours)
    - Patient CRUD
    - Appointment CRUD
    - Note CRUD + sign
    - Intake CRUD + token access
    - Dashboard aggregations
    - Sync push/pull
    - AI generation (stateless)

## Testing Strategy

### Unit Tests (High Priority)

- **MedicareRulesEngine**
  - Test all thresholds (10 visits, 30 days, 8-minute rule)
  - Test warning vs hard stop
  - Test override permissions by role

- **SignatureService**
  - Test hash stability (same input = same hash)
  - Test immutability enforcement
  - Test addendum workflow

- **SyncEngine Conflict Resolution**
  - Test last-write-wins for drafts
  - Test reject for signed entities
  - Test intake lock after eval

### Integration Tests (Medium Priority)

- **EF Core Migrations**
  - Test migration application
  - Test seed data insertion
  - Test soft delete query filter

- **API Endpoints + RBAC**
  - Test PT can sign notes
  - Test PTA cannot sign final notes
  - Test Admin can manage users
  - Test Aide has limited access

### Offline Resilience Tests (Lower Priority)

- Test sync queue persistence across app restart
- Test conflict surfacing to UI
- Test partial sync failure recovery

## Known Limitations & Future Work

### Current Limitations

1. **No Real-Time Sync**: Uses 30-second polling, not WebSockets/SignalR
2. **SQLite Only**: Azure SQL migration path defined but not implemented
3. **No Encryption**: Local SQLite encryption documented but not coded
4. **Stub Integrations**: Payment/Fax/HEP are interfaces only
5. **No AI Implementation**: OpenAI/Azure OpenAI integration not included

### Future Enhancements (Out of Scope for This PR)

- Real-time collaborative editing (complex, requires CRDT or OT)
- Cloud database migration (Azure SQL + Blob Storage)
- Full payment provider integration (Authorize.Net Accept.js)
- Production AI service (OpenAI API with prompt templates)
- Advanced analytics dashboard (BI queries on aggregated data)

## Code Review Checklist

Before merging, verify:

- [ ] All entities compile and follow ISyncTrackedEntity pattern
- [ ] ApplicationDbContext builds and configures relationships correctly
- [ ] No PHI in AuditLog entity or logs
- [ ] Soft delete query filter works for Patient
- [ ] ISignedEntity prevents post-signature mutation
- [ ] ExternalSystemMapping unique constraint enforced
- [ ] All interfaces follow dependency rules (Core→Application→Infrastructure)
- [ ] Documentation explains migration strategy
- [ ] Security summary added after CodeQL run

## Timeline Estimate

**Foundation (Completed)**: 8-10 hours
- Core models
- Application interfaces
- Database context

**Priority 1 (Core Services)**: 21-28 hours
- Identity + sessions
- Signature service
- Medicare rules

**Priority 2 (Sync & Persistence)**: 14-19 hours
- Sync engine
- EF migrations

**Priority 3 (Patient & Audit)**: 9-12 hours
- Patient dedup/merge
- Audit service

**Priority 4-5 (Integrations + API)**: 20-26 hours
- External service stubs
- API endpoints

**Testing**: 12-16 hours
- Unit tests
- Integration tests
- Offline tests

**Total Estimate**: 84-111 hours (10-14 days for 1 developer, 5-7 days for 2 developers)

## Security Considerations

- **Authentication**: PIN hashing uses modern algorithms (BCrypt/Argon2)
- **Session Management**: Timeout enforcement prevents session hijacking
- **Audit Trail**: All privileged actions logged (no PHI)
- **Signature Integrity**: SHA-256 hash prevents tampering
- **No Card Storage**: Payment tokenization enforced
- **PHI Minimization**: Audit logs exclude patient data
- **RBAC**: Role-based authorization on all write operations

## Success Criteria

This PR is successful when:

1. **Domain model compiles** with zero dependency violations
2. **Database migrations work** on clean SQLite database
3. **Core services implement** authentication, signatures, and rules
4. **Sync engine handles** offline → online with conflict resolution
5. **API endpoints enforce** RBAC and Medicare rules
6. **Tests pass** for rules engine, signatures, and sync conflicts
7. **No PHI leaks** in audit logs or error messages
8. **Documentation exists** for migration and deployment

---

**Last Updated**: 2026-02-17
**Author**: GitHub Copilot
**Status**: Foundation Complete, Implementation In Progress
