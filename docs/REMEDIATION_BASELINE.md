# PTDoc Remediation Baseline — Sprint K

**Effective:** Sprint K  
**Status:** Approved  
**Scope:** Audit contradiction resolution, governance baseline, and CI enforcement for Sprints O–T

---

## Purpose

This document resolves the cross-audit contradictions identified after Sprint J, establishes the
**official secret management policy**, confirms background service existence, and defines the
"definition of done" for remediation Sprints O–T.

---

## 1. Secret Management Policy

### 1.1 Resolved Contradiction

*Audit A* treated any committed signing-key string in config files as a severe defect ("secret
leakage"). *Audit B* accepted placeholders as non-secrets provided the runtime enforced real-key
injection. This contradiction is now **resolved** as follows.

### 1.2 Official PFPT Secret Policy

| Scenario | Policy Decision | Rationale |
|---|---|---|
| **Placeholder values** in tracked `appsettings*.json` | ✅ **Permitted** | They are intentionally non-functional; `Program.cs` throws at startup if a placeholder reaches runtime. |
| **Real signing keys** (JWT, IntakeInvite) in tracked files | ❌ **Forbidden** | A committed real key is a credential leak regardless of expiry. |
| **Real signing keys** in `.gitignore`d or `UserSecrets` store | ✅ **Permitted** | Platform-standard local developer workflow; keys never enter VCS. |
| **Real signing keys** injected as CI environment variables | ✅ **Permitted** | Standard ephemeral secret injection; not persisted in VCS. |
| **Encryption keys** (DB, backup) in tracked files | ❌ **Forbidden** | Same rule as signing keys. Must come from OS SecureStorage (MAUI) or environment (API). |
| **Third-party API keys** (`Ai:ApiKey`, `Integrations.*:ApiKey`) | ❌ **Forbidden in tracked files** | Must be `""` (empty) or absent in tracked config. Real values via UserSecrets/env only. |

### 1.3 Approved Placeholder Values

The following values are the **only** non-empty signing key values permitted in tracked config files:

| Config File | Config Key | Permitted Placeholder |
|---|---|---|
| `src/PTDoc.Api/appsettings.json` | `Jwt:SigningKey` | `REPLACE_WITH_A_MIN_32_CHAR_SECRET` |
| `src/PTDoc.Api/appsettings.Development.json` | `Jwt:SigningKey` | `DEV_ONLY_REPLACE_WITH_A_MIN_32_CHAR_SECRET` |
| `src/PTDoc.Web/appsettings.Development.json` | `IntakeInvite:SigningKey` | Any value beginning with `REPLACE_` |

### 1.4 Developer Workflow for Real Secrets

```bash
# Local development (one-time setup after cloning)
./setup-dev-secrets.sh        # macOS/Linux
.\setup-dev-secrets.ps1       # Windows PowerShell

# This script generates and stores keys in .NET User Secrets
# (stored outside the repository in ~/.microsoft/usersecrets/)
```

### 1.5 CI Secret Injection

CI workflows generate ephemeral secrets at job start:

```yaml
# From ci-core.yml
- name: Generate ephemeral CI secrets
  run: |
    echo "Jwt__SigningKey=$(openssl rand -base64 64)" >> "$GITHUB_ENV"
    echo "IntakeInvite__SigningKey=$(openssl rand -base64 32)" >> "$GITHUB_ENV"
```

These environment variables override any placeholder values in config files for the duration of the
CI job only. They are **never persisted** to the repository.

### 1.6 Startup Fail-Closed Enforcement

`Program.cs` validates keys at startup and **throws** `InvalidOperationException` if:
- A JWT or IntakeInvite key matches a known placeholder.
- A key is shorter than 32 characters.

This ensures placeholders cannot silently reach production.

### 1.7 CI Enforcement

The new `ci-secret-policy.yml` workflow (Sprint K) enforces this policy on every pull request by:

1. **Config file scan** — uses a Python/bash script to extract signing key values from tracked JSON
   files and verify they match the approved placeholder list or are empty.
2. **`[Category=SecretPolicy]` tests** — runs the existing `ConfigurationValidationTests` suite,
   which asserts the same invariant independently via the .NET configuration stack.

Both gates must pass for a PR to merge. See `.github/workflows/ci-secret-policy.yml` and
[Secret Policy CI (Sprint K)](CI.md#secret-policy-ci-sprint-k) in `docs/CI.md`.

---

## 2. Background Processing Evidence

### 2.1 Resolved Contradiction

*Audit A* reported background service implementations as **not found**. *Audit B* reported them as
**present but not production-proven**. This contradiction is now resolved by code inspection.

### 2.2 Verified Hosted Services

| Service Class | Location | Registration | Responsibility |
|---|---|---|---|
| `SyncRetryBackgroundService` | `PTDoc.Infrastructure/BackgroundJobs/SyncRetryBackgroundService.cs` | `AddHostedService<SyncRetryBackgroundService>()` in `Program.cs` | Periodically retries failed `SyncQueueItems` (Status=Failed, RetryCount < MaxRetries, LastAttemptAt > MinRetryDelay). Calls `ISyncEngine.PushAsync()` to process reset items. |
| `SessionCleanupBackgroundService` | `PTDoc.Infrastructure/BackgroundJobs/SessionCleanupBackgroundService.cs` | `AddHostedService<SessionCleanupBackgroundService>()` in `Program.cs` | Periodically invokes `IAuthService.CleanupExpiredSessionsAsync()` to revoke expired PIN sessions. Default interval: 5 minutes. |

Both services implement `BackgroundService` (from `Microsoft.Extensions.Hosting`) and the internal
`IBackgroundJobService` marker interface.

### 2.3 Reliability Characteristics

| Characteristic | SyncRetryBackgroundService | SessionCleanupBackgroundService |
|---|---|---|
| **Retry logic** | ✅ Yes — MinRetryDelay prevents hot-retry; MaxRetries cap prevents infinite loops | ✅ Yes — single cleanup call, failure logged and loop continues |
| **Idempotency** | ✅ Yes — status check prevents double-processing the same item | ✅ Yes — `CleanupExpiredSessionsAsync` is idempotent by design |
| **Error isolation** | ✅ Yes — exceptions are caught, logged, and the loop continues | ✅ Yes — same pattern |
| **Concurrency safety** | ⚠️ Partial — single-instance: no distributed locking for multi-node deployments | ⚠️ Partial — same; safe in single-node deployments |
| **PHI in logs** | ✅ None — only counts, statuses, and entity IDs logged | ✅ None — no user data logged |
| **Shutdown handling** | ✅ Yes — `OperationCanceledException` breaks loop cleanly | ✅ Yes — same pattern |

### 2.4 Reliability Gap (Remediation Sprint S/T)

For **production multi-node deployments**, both services should eventually acquire a distributed lock
(e.g., database advisory lock or Redis) before executing to prevent duplicate processing. This is
tracked as a Sprint S/T hardening item and does not block current single-node staging readiness.

### 2.5 Test Coverage

Existing tests in `tests/PTDoc.Tests/BackgroundJobs/BackgroundJobTests.cs`:

| Test | What It Covers |
|---|---|
| `SyncRetryJob_DoesNothing_WhenNoFailedItems` | No-op when queue is empty |
| `SyncRetryJob_SkipsItems_AtMaxRetries` | MaxRetries cap prevents infinite retry |
| `SyncRetryJob_SkipsItems_TooRecentlyFailed` | MinRetryDelay prevents hot-retry |
| `SyncRetryJob_ResetsEligibleFailedItem_ToPending_ThenProcesses` | Eligible items are reset and pushed |
| `SessionCleanupJob_CallsCleanupExpiredSessionsAsync` | Session cleanup delegates correctly |
| `SyncRetryOptions_HasExpectedDefaults` | Default configuration values |
| `SessionCleanupOptions_HasExpectedDefaults` | Default configuration values |

**Verdict:** Background services exist, are wired as `IHostedService`, and have test coverage
for their core reliability characteristics. The Audit A "missing" finding is incorrect.
Audit B's "present but not fully production-proven" finding is partially accurate for
multi-node concurrency; tracked for Sprint T.

---

## 3. Policy Contradiction Resolution Summary

| Contradiction | Audit A Position | Audit B Position | Resolution |
|---|---|---|---|
| **Secret management** | Any committed key string = FAIL | Placeholders acceptable if CI enforces real keys | **Resolved**: Placeholders are acceptable; real secrets are forbidden. CI gate added (Sprint K). |
| **Background service existence** | Services not found | Services present, partially proven | **Resolved**: Services exist, are wired, and have tests. Multi-node hardening deferred to Sprint T. |

---

## 4. CI Governance Baseline

The following CI gates are active as of Sprint K:

| Workflow | Gate | Sprint Added |
|---|---|---|
| `ci-core.yml` | Build + all tests + format check | Sprint A |
| `ci-core.yml` | MAUI iOS simulator build | Sprint E |
| `ci-db.yml` | SQLite migration apply + persistence tests | Sprint C |
| `ci-db.yml` | SQL Server schema creation + persistence tests | Sprint C |
| `ci-db.yml` | PostgreSQL schema creation + persistence tests | Sprint C |
| `ci-db.yml` | Migration drift detection (`has-pending-model-changes`) | Sprint F |
| `ci-db.yml` | `[Category=Observability]` health-check tests | Sprint F |
| `codeql.yml` | CodeQL static security analysis (C#) | Phase 7 |
| **`ci-secret-policy.yml`** | **Config file scan + `[Category=SecretPolicy]` tests** | **Sprint K** |

### 4.1 Gaps Tracked for Future Sprints

| Gap | Target Sprint |
|---|---|
| RBAC role matrix tests for sensitive endpoints | Sprint P |
| Tenant isolation regression tests | Sprint S |
| Provider-specific migration apply gate (SQL Server / Postgres) | Sprint Q |
| Offline sync integration tests for required entities | Sprint R |
| Medicare compliance enforcement gate | Sprint S |

---

## 5. Sprint-by-Sprint Remediation Assignments

Each Sprint A–J gap identified in the cross-audit has been assigned to a remediation sprint:

| Gap | Severity | Assigned Sprint |
|---|---|---|
| ObjectiveMetric entity missing | Critical | Sprint O |
| IntakeResponse vs IntakeForm contract drift | Critical | Sprint O |
| Missing Patient CRUD endpoints | Critical | Sprint O |
| Missing Intake submission/retrieval endpoints | Critical | Sprint O |
| Missing ClinicalNote draft create/update endpoints | Critical | Sprint O |
| Provider-specific migrations not populated | Critical | Sprint Q |
| Tenant isolation: ClinicId == null leakage | Critical | Sprint S |
| Server-side RBAC not enforced | High | Sprint P |
| Secret management policy ambiguity | High | **Sprint K ✅ Resolved** |
| Dev encryption key fallback | High | Sprint P |
| Offline sync incomplete | High | Sprint R |
| Medicare rules not integrated into note lifecycle | High | Sprint S |
| Background job reliability (multi-node concurrency) | Medium | Sprint T |
| Audit surface incomplete | Medium | Sprint S |
| CI migration-apply parity per provider | Medium | Sprint Q |
| Documentation drift | Low | Inline with functional sprints |

---

## 6. Definition of Done for Remediation Sprints

A remediation sprint (O–T) is **complete** when:

1. All acceptance criteria from the original Sprint A–J gap have a passing automated test or
   documented CI gate.
2. No new critical/high items were introduced.
3. The `ACCEPTANCE_EVIDENCE_MAP.md` is updated to reflect the new evidence.
4. `CHANGELOG.md` `[Unreleased]` section is updated.
5. CI passes on the branch without manual override.

---

*Last updated: Sprint K — March 2026*
