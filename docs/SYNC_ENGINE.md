# PTDoc Offline-First Sync Engine

## Overview

The PTDoc sync engine enables offline-first documentation with automatic conflict resolution. It manages bidirectional synchronization between local devices (MAUI) and the central server (API), ensuring data consistency across distributed environments.

## How Sync Works

### Architecture

```
┌─────────────────┐         ┌─────────────────┐
│  Local Device   │         │  Central Server │
│  (MAUI + SQLite)│◄───────►│  (API + SQLite) │
└─────────────────┘         └─────────────────┘
       ↓                             ↓
  Interceptor                   Interceptor
  auto-enqueues                 auto-enqueues
  changes                       changes
       ↓                             ↓
  SyncQueueItem ───Push──────► Process & Resolve
       ↑                             │
       └──────────Pull◄──────────────┘
```

### Sync Cycle

1. **Automatic Enqueuing** (via `SyncMetadataInterceptor`)
   - Every entity save triggers `ModifiedByUserId` stamping
   - `SyncState` transitions from `Synced` → `Pending`
   - Change is automatically enqueued in `SyncQueueItem` table

2. **Push Phase** (`POST /api/v1/sync/push`)
   - Retrieves all pending queue items ordered by `EnqueuedAt`
   - Sends local changes to server
   - Server applies conflict resolution rules
   - Successful items marked `Completed`, failures increment `RetryCount`

3. **Pull Phase** (`GET /api/v1/sync/pull?sinceUtc=...`)
   - Fetches server changes since last sync timestamp
   - Applies changes locally with conflict detection
   - Conflicts archived in `SyncConflictArchive` table

4. **Full Sync** (`POST /api/v1/sync/run`)
   - Executes push then pull atomically
   - Returns combined results with conflict summary
   - Updates `LastSyncAt` timestamp

## Conflict Resolution Rules

PTDoc uses **deterministic conflict resolution** to ensure data integrity and compliance:

### 1. Draft Entities (Last-Write-Wins)

**Rule**: Most recent `LastModifiedUtc` wins, older version archived.

**Example**:
```
Local:  Patient modified at 10:00 UTC
Server: Patient modified at 10:05 UTC
Result: Server wins, local version archived
```

**Archive**: Both versions stored in `SyncConflictArchive` for review.

### 2. Signed Entities (Immutable)

**Rule**: Once `SignatureHash` is set, entity becomes immutable.

**Applies to**: `ClinicalNote` (after PT/PTA signature)

**Example**:
```
Local:  Attempt to modify signed note
Server: Reject update
Result: Conflict logged as "RejectedImmutable"
```

**Why**: Medicare compliance requires signed notes to be unalterable.

### 3. Locked Intake Forms

**Rule**: Once `IsLocked = true`, intake form cannot be modified.

**Applies to**: `IntakeForm` (after initial evaluation)

**Example**:
```
Local:  Attempt to modify locked intake
Server: Reject update
Result: Conflict logged as "RejectedLocked"
```

**Why**: Intake data must remain static after clinical use begins.

## Queue Management

### SyncQueueItem States

```
Pending ──┐
          ├──► Processing ──► Completed
          │                      ▲
          └──► Failed ───────────┘
                (retry)
```

- **Pending**: Waiting to sync
- **Processing**: Currently being sent to server
- **Completed**: Successfully synced
- **Failed**: Error occurred (will retry up to `MaxRetries`)
- **Cancelled**: Manually cancelled (future feature)

### Retry Logic

- **Max Retries**: 3 (configurable via `SyncQueueItem.MaxRetries`)
- **Backoff**: None currently (linear retry)
- **Permanent Failure**: After max retries, status remains `Failed`

### Deduplication

If the same entity is modified multiple times before sync:
- Existing pending queue item is **updated** (not duplicated)
- `Operation` reflects most recent change (Create → Update)
- `EnqueuedAt` timestamp refreshed

## API Endpoints

### POST /api/v1/sync/run

Trigger a complete sync cycle (push + pull).

**Response**:
```json
{
  "success": true,
  "completedAt": "2026-02-17T04:45:00Z",
  "durationMs": 1234,
  "push": {
    "total": 5,
    "success": 4,
    "failed": 1,
    "conflicts": 0
  },
  "pull": {
    "total": 3,
    "applied": 2,
    "skipped": 0,
    "conflicts": 1
  },
  "conflicts": [
    {
      "entityType": "ClinicalNote",
      "entityId": "...",
      "resolution": "RejectedImmutable",
      "reason": "Entity is signed and immutable"
    }
  ]
}
```

### POST /api/v1/sync/push

Push local changes to server.

**Response**: Similar to `push` section above.

### GET /api/v1/sync/pull?sinceUtc=2026-02-17T00:00:00Z

Pull server changes since timestamp.

**Response**: Similar to `pull` section above.

### GET /api/v1/sync/status

Get current queue health.

**Response**:
```json
{
  "pending": 5,
  "processing": 1,
  "failed": 2,
  "oldestPendingAt": "2026-02-17T04:30:00Z",
  "lastSyncAt": "2026-02-17T04:40:00Z"
}
```

## Troubleshooting

### Queue Not Draining

**Symptoms**: `pending` count never decreases.

**Checks**:
1. Verify network connectivity to server
2. Check `SyncQueueItem` table for `ErrorMessage` in failed items
3. Review server logs for API errors
4. Ensure authentication token is valid

**Fix**:
```bash
# Check queue status
curl GET http://localhost:5170/api/v1/sync/status \
  -H "Authorization: Bearer YOUR_TOKEN"

# Manually trigger sync
curl POST http://localhost:5170/api/v1/sync/run \
  -H "Authorization: Bearer YOUR_TOKEN"
```

### Conflicts Not Resolving

**Symptoms**: Same conflict appearing repeatedly.

**Root Causes**:
- **Signed Note**: Cannot modify signed notes - intended behavior
- **Locked Intake**: Cannot modify locked intakes - intended behavior
- **Timestamp Skew**: Local/server clocks out of sync

**Fix**:
```sql
-- Review archived conflicts
SELECT * FROM SyncConflictArchives 
WHERE IsResolved = 0 
ORDER BY DetectedAt DESC;

-- Check specific conflict
SELECT 
  EntityType,
  ResolutionType,
  Reason,
  ArchivedVersionLastModifiedUtc,
  ChosenVersionLastModifiedUtc
FROM SyncConflictArchives 
WHERE EntityId = 'YOUR_ENTITY_ID';
```

### Failed Items Not Retrying

**Symptoms**: Items stuck in `Failed` status.

**Checks**:
1. Verify `RetryCount < MaxRetries`
2. Check if error is transient or permanent

**Fix**:
```sql
-- Reset retry count for manual retry
UPDATE SyncQueueItems 
SET Status = 0, RetryCount = 0 
WHERE Status = 3 AND RetryCount >= MaxRetries;
```

### Data Loss Concerns

**Prevention**:
- All conflicts are archived in `SyncConflictArchive`
- Losing version (LWW) stored as JSON for recovery
- Audit trail maintained via `AuditLog` (future feature)

**Recovery**:
```sql
-- Find archived version
SELECT ArchivedDataJson 
FROM SyncConflictArchives 
WHERE EntityType = 'Patient' 
  AND EntityId = 'YOUR_PATIENT_ID'
ORDER BY DetectedAt DESC 
LIMIT 1;
```

## Performance Considerations

### Queue Size

- **Recommended**: < 100 pending items
- **Warning**: 100-500 pending items (may slow sync)
- **Critical**: > 500 pending items (review sync frequency)

### Batch Processing

Current implementation: Sequential (one item at a time)

Future optimization: Batch API calls to process multiple items per request.

### Index Strategy

Optimized indexes on:
- `SyncQueueItem`: `(Status, EnqueuedAt)` for queue processing
- `SyncQueueItem`: `(EntityType, EntityId)` for deduplication
- `SyncConflictArchive`: `(EntityType, EntityId)` for conflict review
- `SyncConflictArchive`: `IsResolved` for pending conflict dashboard

## Security & Privacy

### PHI Protection

- **Queue Items**: Contain only entity type and ID (no patient data)
- **Conflict Archives**: Store full entity as JSON - **contains PHI**
  - Access restricted via RBAC
  - Encrypted at rest (SQLCipher - Phase 7)
- **Audit Logs**: Metadata only, no PHI (future feature)

### Authentication

All sync endpoints require authentication:
```
Authorization: Bearer YOUR_SESSION_TOKEN
```

Invalid or expired tokens return `401 Unauthorized`.

## Future Enhancements

### Phase 4+ Features

- **Automatic Sync Loop**: Background service syncing every 30 seconds
- **Exponential Backoff**: Smarter retry with increasing delays
- **Batch API**: Process multiple queue items per request
- **Conflict UI**: Dashboard for manual conflict resolution
- **Selective Sync**: Sync specific entity types only
- **Delta Compression**: Only send changed fields, not entire entity

## Related Documentation

- [Clean Architecture](../ARCHITECTURE.md) - Layer boundaries and dependencies
- [EF Migrations](../EF_MIGRATIONS.md) - Database schema evolution
- [Offline Sync Design](../PTDocs+_Offline_Sync_Conflict_Resolution.md) - Original design specs
- [Backend TDD](../PTDocs+_Backend_TDD.md) - Test-driven development approach

---

**Last Updated**: February 2026  
**Version**: Phase 3 Complete  
**Status**: Production-ready foundation
