# PTDoc - AI Coding Assistant Instructions

## Working Agreement

**Before starting any task:**
1. **Restate the task** in 1-2 lines to confirm understanding
2. **Identify 0-3 relevant docs** from the Doc Map below (only if needed for the task)
3. **Use existing patterns** - prefer reusing over inventing new approaches
4. **Small commits** - implement changes incrementally
5. **Don't refactor unrelated code** unless explicitly required
6. **File placement first** - consult `docs/ARCHITECTURE.md` for file placement and architectural boundaries, then use the File Organization Quick Reference in this document.
7. **Update the changelog** - if the session changes repository contents, update `docs/CHANGELOG.md` before handing off. If no entry is appropriate, say so explicitly.

**Build/Test Execution Policy (Session Preference):**
- Do **not** run `dotnet build`, `dotnet test`, or other build/verification commands automatically.
- Ask the user to run builds/tests and provide output.
- Use provided build/test output to drive fixes and iteration.

**Reference Docs Only When Relevant**
- Do NOT open/cite every doc for every task
- Consult docs only when they directly affect your current work
- Trust your knowledge of .NET/Blazor for standard patterns

**Documentation Authority Hierarchy (Highest → Lowest)**
1. **Primary architecture & system design**
  - [PTDocs+ Branch-Specific Database Blueprint and Phased Plan for UI Completion](../docs/PTDocs+%20Branch-Specific%20Database%20Blueprint%20and%20Phased%20Plan%20for%20UI-Completiondeep-research-report.md)
  - [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)
  - [docs/SYNC_ENGINE.md](../docs/SYNC_ENGINE.md)
2. **Repository workflow & guardrails**
  - This file ([.github/copilot-instructions.md](copilot-instructions.md))
  - [docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)
  - [docs/CI.md](../docs/CI.md)
3. **Implementation-specific guidance**
  - [README.md](../README.md)
  - [docs/SECURITY.md](../docs/SECURITY.md)
  - [docs/RUNTIME_TARGETS.md](../docs/RUNTIME_TARGETS.md)
  - [docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md)
  - [docs/TROUBLESHOOTING.md](../docs/TROUBLESHOOTING.md)
  - [docs/BUILD.md](../docs/BUILD.md)
4. **Supporting UI & reference docs**
  - [docs/context/ptdoc-figma-make-prototype-v5-context.md](../docs/context/ptdoc-figma-make-prototype-v5-context.md)
  - [docs/Blazor-Context.md](../docs/Blazor-Context.md)
  - [docs/style-system.md](../docs/style-system.md)
  - [docs/ACCESSIBILITY_USAGE.md](../docs/ACCESSIBILITY_USAGE.md)
  - [docs/design-system/THEME_VISUAL_GUIDE.md](../docs/design-system/THEME_VISUAL_GUIDE.md)
5. **General framework knowledge**
  - Use external framework guidance only when repository documentation does not define the behavior.

---

## Authoritative Documentation

> **Governance rule:** If the AI encounters a design decision, it **must consult these documents instead of making assumptions**. These are the canonical PFPT specification documents that govern all system behavior, compliance requirements, and architecture decisions.

| Document | Scope | When to Consult |
|----------|-------|-----------------|
| **PTDoc (PFPT) Backend Technical Design Document (TDD)** | Backend architecture, service contracts, data models | Any backend design decision, adding services, changing APIs |
| **PTDoc (PFPT) Unified Functional Specification Document (Master FSD)** | Functional requirements, feature definitions, user stories | Implementing any user-facing feature or business workflow |
| **PTDoc (PFPT) AI Prompt Specification – Assessment & Plan of Care** | AI behavior constraints, prompt design, output rules | Any feature that produces, stores, or acts on AI-generated content |
| **PTDoc (PFPT) Medicare Rules Engine Specification** | CMS compliance rules, billing codes, documentation requirements | Any feature touching clinical notes, billing, or Medicare documentation |
| **PTDoc (PFPT) Offline Sync & Conflict Resolution Specification** | Offline-first behavior, sync queue semantics, conflict strategies | Any change to sync logic, queue state, or MAUI offline behavior |
| **PTDoc (PFPT) Blazor Component & Page Mapping** | UI structure, component hierarchy, page routing | Implementing or restructuring Blazor pages and components |
| **PTDoc (PFPT) QA Acceptance Test Matrix** | Acceptance criteria, test scenarios, coverage expectations | Defining test coverage for any new feature or changed behavior |

These documents supersede any general .NET/Blazor framework knowledge when there is a conflict. They also supersede assumptions derived from code patterns alone when the system behavior is non-obvious or compliance-sensitive.

---

## Doc Map - When to Consult What

### Setup & Running
- **[README.md](../README.md)** - First-time setup, running platforms, prerequisites
  - **Use when:** Setup issues, build errors, platform-specific launch problems
  - **Skip when:** Writing code, implementing features

### Architecture & Boundaries  
- **[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)** - Clean Architecture layers, dependency rules
  - **Use when:** Adding services, crossing layer boundaries, data access changes
  - **Skip when:** UI-only changes, styling, component props

### Blazor Specifics
- **[docs/Blazor-Context.md](../docs/Blazor-Context.md)** - Lifecycle, parameters, pitfalls, MAUI Hybrid
  - **Use when:** Component not rendering, lifecycle issues, parameter binding problems
  - **Skip when:** Standard component creation, basic event handling

### UI Implementation
- **[docs/context/ptdoc-figma-make-prototype-v5-context.md](../docs/context/ptdoc-figma-make-prototype-v5-context.md)** - Design system, component specs
  - **Use when:** Implementing UI from Figma, converting React→Blazor, design tokens
  - **Skip when:** Backend logic, API changes, database migrations
  - **Figma Link:** https://www.figma.com/make/1Fd3pzaGzvHboxFKuCz4dY/PTDoc-Prototype-v5

### Styling
- **[docs/style-system.md](../docs/style-system.md)** - Design tokens (`tokens.css`), theming
  - **Use when:** Styling components, adding colors/spacing, theme support
  - **Skip when:** Logic changes, not touching CSS

### Database Operations
- **[docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md)** - EF Core migrations, multi-provider workflow (Sprint B), production deployment commands (Sprint E)
  - **Use when:** Schema changes, adding migrations, switching database provider, configuring `Database:Provider`, database errors, **production deployments**
  - **Skip when:** UI work, no data model changes
  - **Multi-provider:** Migrations are split into `PTDoc.Infrastructure.Migrations.{Sqlite|SqlServer|Postgres}` assemblies
  - **Provider config:** Set `Database:Provider` to `Sqlite` (default), `SqlServer`, or `Postgres`
  - **Production deployment:** See *Production Deployment* section in `docs/EF_MIGRATIONS.md` for environment variables and CLI migration commands

### Production Database Deployment (Sprint E)
- **[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)** — *Production Database Configuration* section
  - **Use when:** Configuring `Database:AutoMigrate`, choosing a production provider, understanding migration safety defaults
  - **Key facts:**
    - `Database:AutoMigrate` defaults to `true` in Development, `false` in Production
    - Override via env var `Database__AutoMigrate=true/false`
    - Production providers (SqlServer/Postgres) require `ConnectionStrings__PTDocsServer`
- **[docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)** — *Production Deployment* section
  - **Use when:** Setting up production environment variables, running deployment migrations, troubleshooting startup failures

### Platform Differences
- **[docs/RUNTIME_TARGETS.md](../docs/RUNTIME_TARGETS.md)** - Web vs MAUI differences
  - **Use when:** Platform-specific behavior (auth, storage, API URLs)
  - **Skip when:** Shared component logic

### MAUI Offline Persistence & Local Database
- **[docs/MOBILE_ARCHITECTURE.md](../docs/MOBILE_ARCHITECTURE.md)** - MAUI encrypted local SQLite, offline-first persistence
  - **Use when:** Adding local cache entities, implementing offline data access, debugging SQLCipher init, extending sync scaffolding, reviewing encryption key lifecycle
  - **Skip when:** Web-only changes, server-side API work, no MAUI-specific behavior

### Development Workflows
- **[docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)** - Scripts, testing, workflows
  - **Use when:** Running tests, using helper scripts, debugging setup
  - **Skip when:** Implementation work

### Security Policy
- **[docs/SECURITY.md](../docs/SECURITY.md)** - Auth design, secrets management, HIPAA security controls
  - **Use when:** Implementing auth, handling secrets/keys, session policy, security-sensitive changes
  - **Skip when:** UI-only work with no auth or config changes

### CI/CD Pipeline
- **[docs/CI.md](../docs/CI.md)** - CI principles, build standards, secrets in CI, branching strategy, migration validation (Sprint F)
  - **Use when:** Modifying workflows, understanding CI behavior, adding CI secrets, branching/deployment
  - **Skip when:** Local-only development with no CI impact

### Observability & Operational Diagnostics (Sprint F)
- **[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)** — *Observability & Health Monitoring* section
  - **Use when:** Adding health checks, implementing diagnostics endpoints, reviewing `/health/live` or `/health/ready` endpoint contracts
  - **Skip when:** UI-only work, no database or operational monitoring changes
  - **Key facts:**
    - `GET /health/live` — liveness, unauthenticated, no database checks
    - `GET /health/ready` — readiness, checks `DatabaseHealthCheck` + `MigrationStateHealthCheck`, returns JSON
    - `GET /diagnostics/db` — requires Bearer token, returns provider/migration/connectivity state (no secrets)
- **[docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)** — *Operational Diagnostics & Observability* section
  - **Use when:** Diagnosing migration drift, interpreting health check responses, troubleshooting startup logs
  - **Key commands:**
    - `curl http://localhost:5170/health/ready` — check readiness
    - `dotnet ef migrations has-pending-model-changes` — detect model drift
- **[docs/CI.md](../docs/CI.md)** — *Migration Validation (Sprint F)* section
  - **Use when:** Understanding what the `db-migration-validate` CI job validates or reproducing it locally

### Backend Sprint Plans (authoritative for backend assumptions)
- **PTDocs+ Branch-Specific Database Blueprint and Phased Plan** - Sprint definitions, acceptance criteria, architectural decisions for phased backend work
  - **Use when:** Making ANY backend assumption about config, secrets, database, auth design, or phased sprint scope. Consult BEFORE making assumptions — especially for Sprint A (secrets/config), Sprint B+ (provider switching, migrations), or any config/startup behavior.
  - **Skip when:** Pure UI component work with zero backend or config impact
  - **Location:** Referenced in PR descriptions and issue context for active sprints

---

## Quick Reference - Critical Rules Only

### Project Structure (Clean Architecture)
```
PTDoc.Core         → Domain entities (ZERO dependencies)
PTDoc.Application  → Interfaces/contracts (depends only on Core)
PTDoc.Infrastructure → Implementations (depends on Application)
PTDoc.Api/Web/Maui → Presentation (wires up DI)
PTDoc.UI           → Shared Blazor components
```

**Never:** Reference Infrastructure from Application or Core

### Blazor Components (Most Common Rules)

**Component Naming:**
- **MUST** use PascalCase: `PTDocMetricCard.razor` (never lowercase)
- Update `_Imports.razor` when adding new namespaces

**Parameters:**
- Mark with `[Parameter]` attribute
- **NEVER mutate parameters after initialization** (treat as read-only)
- Use `[EditorRequired]` for required params
- Two-way binding: `[Parameter] T Value` + `[Parameter] EventCallback<T> ValueChanged`

**Wrapper Components:**
- **MUST** define `[Parameter] RenderFragment? ChildContent`  
- **MUST** render `@ChildContent` in markup

**Async Loading:**
```csharp
@if (isLoading) {
    <p>Loading...</p>
} else if (data != null) {
    <!-- Render data -->
}
```
**Always show loading state** - never leave blank during async operations

**Lifecycle:**
- `OnInitializedAsync` - data fetching
- `OnAfterRenderAsync(bool firstRender)` - JS interop (check `firstRender`)
- Rarely call `StateHasChanged()` - only for external events (timers, non-Blazor callbacks)

**Styling:**
- **Always use tokens**: `var(--primary)`, `var(--spacing-4)` from `tokens.css`
- **Never hardcode** colors, spacing, or typography
- Support light/dark themes via token switching

### Platform-Specific Patterns

**API Base URLs:**
- Android emulator: `http://10.0.2.2:5170`
- iOS/Mac: `http://localhost:5170`
- Override: `PTDoc_API_BASE_URL` environment variable

**Auth Patterns:**
- MAUI: `SecureStorageTokenStore` (JWT tokens)
- Web: Cookie-based (15min inactivity, 8hr absolute)
- API: JWT Bearer

### Database (EF Core)

**Quick Commands:**
```bash
./PTDoc-Foundry.sh --create-migration  # Create & apply migration
./PTDoc-Foundry.sh --seed              # Seed dev data
```

**Path Resolution:** `PFP_DB_PATH` env → `appsettings` → fallback `PTDoc.db`

**Provider Selection (CI / design-time):**
- `EF_PROVIDER=sqlite` (default) – SQLite / SQLCipher
- `EF_PROVIDER=sqlserver` + `Database__ConnectionString=...` – SQL Server
- `EF_PROVIDER=postgres` + `Database__ConnectionString=...` – PostgreSQL
- See `docs/EF_MIGRATIONS.md` for multi-provider migration commands

**Production Runtime (Sprint E):**
- `Database__Provider=SqlServer` or `Postgres`
- `ConnectionStrings__PTDocsServer=...` – full production connection string (injected via secrets)
- `Database__AutoMigrate=false` (default in production) – run migrations via CLI
- See `docs/EF_MIGRATIONS.md` — *Production Deployment* and `docs/ARCHITECTURE.md` — *Production Database Configuration*

### CI Database Provider Validation (Sprint C)

- `[Category=DatabaseProvider]` integration tests run in CI for all three providers
- SQLite tests use `MigrateAsync()` to validate the full migration history
- SQL Server and PostgreSQL tests use `EnsureCreated()` to validate schema compatibility
- Tests skip automatically when `Database__ConnectionString` is not set (local dev)
- Provider jobs are in `.github/workflows/ci-db.yml`; see `docs/CI.md` for details

### Observability & Migration Safety (Sprint F)

- `[Category=Observability]` integration tests run in CI via `db-migration-validate` job
- Tests assert: no pending migrations after `MigrateAsync()`, applied count equals assembly count, `CanConnectAsync()` returns true
- CI also runs `dotnet ef migrations has-pending-model-changes` — exits non-zero on model drift
- Health endpoints: `GET /health/live` (liveness), `GET /health/ready` (JSON readiness with DB + migration checks)
- Diagnostics endpoint: `GET /diagnostics/db` (requires Bearer token; returns provider, migration status, connectivity — no secrets)
- See `docs/ARCHITECTURE.md` — *Observability & Health Monitoring* and `docs/CI.md` — *Migration Validation (Sprint F)*

### Healthcare & Accessibility

- **HIPAA**: Maintain audit trails for patient data
- **WCAG 2.1 AA**: Keyboard nav + screen reader support mandatory
- **Session limits**: 15min inactivity, 8hr absolute (web)

---

## Implementation Rules

The following rules are **mandatory** and apply to every code change made in this repository. They enforce the architecture defined in the Backend TDD and HIPAA compliance requirements.

1. **Do not violate the architecture defined in the Backend TDD.**  
   Clean Architecture layer boundaries must be respected. `PTDoc.Application` must never reference `PTDoc.Infrastructure`. `PTDoc.Core` must have zero dependencies.

2. **All business rules must be enforced server-side, not only in the UI.**  
   Validation in Blazor components is for user experience only. The API must independently validate all business rules, access controls, and clinical logic.

3. **Signed clinical data must remain immutable.**  
   Once a clinical record (assessment, plan of care, note) has been countersigned or finalized, no code path may overwrite or silently update it. All amendment workflows must create versioned records or audit entries.

4. **AI output must never be persisted automatically.**  
   AI-generated suggestions, summaries, or clinical text must always pass through an explicit clinician review and acceptance step before being written to the database. No background process or auto-save may persist AI output directly.

5. **Offline-first behavior must be preserved.**  
   Any change to sync logic, queue state machines, or MAUI data access must maintain the offline-first guarantee. Consult the *PTDoc (PFPT) Offline Sync & Conflict Resolution Specification* before modifying sync behavior.

6. **Medicare documentation rules must always be enforced.**  
   Any feature involving clinical notes, time tracking, billing codes, or plan-of-care generation must comply with the rules defined in the *PTDoc (PFPT) Medicare Rules Engine Specification*. These rules are not optional.

---

## Testing Requirements

Every feature implemented in this repository **must include tests**. Pull requests without tests for changed behavior are incomplete. Test coverage must align with the scenarios defined in the *PTDoc (PFPT) QA Acceptance Test Matrix*.

### Required Test Types

| Type | Coverage Target | Location |
|------|----------------|----------|
| **Unit Tests** | Business rules, services, rules engine logic, DTO validation | `tests/PTDoc.UnitTests/` |
| **Integration Tests** | API endpoints, persistence behavior, EF Core queries | `tests/PTDoc.IntegrationTests/` |
| **Compliance Tests** | Medicare rules, AI output constraints, role-based permissions, PHI access | `tests/PTDoc.ComplianceTests/` |
| **Offline Sync Tests** | Conflict resolution strategies, sync queue state transitions | `tests/PTDoc.IntegrationTests/` (MAUI sync sub-area) |

> **Note:** Test projects `PTDoc.UnitTests`, `PTDoc.IntegrationTests`, and `PTDoc.ComplianceTests` are the canonical targets. Until these are scaffolded, place tests in `tests/PTDoc.Tests/` using `[Category=...]` attributes to logically segregate them.

### Minimum Coverage Expectations

- **Business rules** — every rule defined in the Master FSD must have at least one positive and one negative unit test
- **API endpoints** — all new endpoints require at least one integration test covering the happy path and one covering authorization rejection
- **Sync queue** — any change to `SyncQueueStatus` state transitions must have tests covering all reachable states
- **Medicare rules engine** — every rule in the Medicare Rules Engine Specification must have a compliance test
- **AI constraints** — tests must verify that AI output is never written to the database without explicit acceptance

### Test Placement Rules

- Unit tests targeting `PTDoc.Core` or `PTDoc.Application` → `tests/PTDoc.UnitTests/`
- Integration tests targeting API or EF Core behavior → `tests/PTDoc.IntegrationTests/`
- Compliance, Medicare, and AI constraint tests → `tests/PTDoc.ComplianceTests/`
- Background service and hosted service tests → unit tests using `IServiceScopeFactory` mocks
- No pull request should be considered complete if required tests are missing.

---

## Release Quality Gate

A feature is considered **complete and mergeable** only when all of the following conditions are met:

- [ ] All automated tests pass (`dotnet test` exits with code 0)
- [ ] No QA Acceptance Test Matrix scenario is broken or newly excluded
- [ ] Architecture rules are respected — no cross-layer references violate Clean Architecture
- [ ] No PHI (Protected Health Information) is written to logs, error messages, or telemetry
- [ ] Offline-first behavior is preserved for all MAUI/sync-related changes
- [ ] Medicare documentation rules remain enforced for any clinical feature
- [ ] AI output is not auto-persisted anywhere in the changed code paths
- [ ] Signed clinical records cannot be silently overwritten by the change
- [ ] StyleCop formatting passes (`dotnet format --verify-no-changes`)
- [ ] CodeQL security scan reports no new high/critical alerts
- [ ] `docs/CHANGELOG.md` `[Unreleased]` section updated — **mandatory for every session** except as permitted by the bypass in [Mandatory Changelog Rule](#mandatory-changelog-rule)

---

## When to Consult Docs - Decision Checklist

**Consult docs when:**
- [ ] Adding/changing services across architecture layers
- [ ] Implementing UI from Figma (need design specs)
- [ ] Component not rendering (lifecycle/parameter issues)
- [ ] Database schema changes (migrations needed)
- [ ] Switching database provider (`Database:Provider` config)
- [ ] Adding migrations for a new provider (Sqlite/SqlServer/Postgres)
- [ ] Configuring production database environment variables (`Database__Provider`, `ConnectionStrings__PTDocsServer`, `Database__AutoMigrate`)
- [ ] Running production deployment migrations (consult `docs/EF_MIGRATIONS.md` — *Production Deployment* section)
- [ ] Adding or modifying health checks, diagnostics endpoints, or observability logic (consult `docs/ARCHITECTURE.md` — *Observability* section)
- [ ] Interpreting `GET /health/ready` responses or migration drift in production (consult `docs/DEVELOPMENT.md` — *Operational Diagnostics* section)
- [ ] Platform-specific behavior (auth, storage, APIs)
- [ ] First-time setup or build issues
- [ ] Accessibility requirements unclear
- [ ] Making any backend assumption about config, secrets, startup validation, or sprint scope (consult Sprint Blueprint + docs/SECURITY.md + docs/CI.md)

**Don't consult docs for:**
- Standard .NET/C# patterns you already know
- Basic Blazor component creation
- Simple event handling or prop passing  
- Standard CRUD operations following existing patterns
- CSS styling using existing tokens

---

## File Organization Quick Reference

```
PTDoc.Core/        → Domain entities (zero deps)
PTDoc.Application/ → Interfaces, DTOs (depends on Core)
PTDoc.Infrastructure/ → EF Core, services (implements Application)
PTDoc.Infrastructure.Migrations.Sqlite/    → SQLite migrations assembly (Sprint B)
PTDoc.Infrastructure.Migrations.SqlServer/ → SQL Server migrations assembly (Sprint B)
PTDoc.Infrastructure.Migrations.Postgres/  → PostgreSQL migrations assembly (Sprint B)
PTDoc.Api/         → REST API, JWT auth
PTDoc.Web/         → Blazor WebAssembly web app
PTDoc.Maui/        → .NET MAUI Blazor app (mobile/desktop)
PTDoc.UI/          → Shared Blazor components (reusable)
  ├── Components/  → UI components
  ├── Pages/       → Routable pages
  └── wwwroot/css/ → Design tokens, styles
```

**Location Rules:**
- Reusable components → `PTDoc.UI/Components/`
- Platform-specific → `PTDoc.Web/` or `PTDoc.Maui/`
- DTOs → `PTDoc.Application/DTOs/`
- Domain entities/models/enums/value objects → `PTDoc.Core/Models/`
- Services interfaces → `PTDoc.Application/Services/`
- Services implementations → `PTDoc.Infrastructure/Services/`
- EF Core migrations (SQLite) → `PTDoc.Infrastructure.Migrations.Sqlite/Migrations/`
- EF Core migrations (SQL Server) → `PTDoc.Infrastructure.Migrations.SqlServer/Migrations/`
- EF Core migrations (Postgres) → `PTDoc.Infrastructure.Migrations.Postgres/Migrations/`

---

## Common Pitfalls - Quick Fixes

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| Component invisible | Lowercase name / missing `_Imports.razor` | PascalCase + add `@using` |
| Blank during load | No loading indicator | Add `@if (isLoading)` block |
| Parameter overwritten | Mutating `[Parameter]` property | Use internal field instead |
| DI lifetime issue | Singleton with user state in Blazor Server | Use Scoped services |
| Build errors | Wrong project reference | Check layer boundaries in architecture |

---

## Quick Commands

```bash
# First-time dev secrets setup (REQUIRED after cloning)
./setup-dev-secrets.sh          # macOS/Linux: generate & store JWT + IntakeInvite keys
.\setup-dev-secrets.ps1         # Windows PowerShell equivalent

# Setup & Build
./PTDoc-Foundry.sh              # Environment setup
./cleanbuild-ptdoc.sh           # Clean build
dotnet build PTDoc.sln          # Build all

# Run
./run-ptdoc.sh                  # Interactive launcher
dotnet run --project src/PTDoc.Api --urls http://localhost:5170   # API
dotnet run --project src/PTDoc.Web                                # Web

# Database (multi-provider - Sprint B; production deployment - Sprint E)
./PTDoc-Foundry.sh --create-migration --seed   # Setup SQLite DB (default)

# Add migration - specify provider-specific migrations project
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api --context ApplicationDbContext

# Apply migration (dev - SQLite)
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s src/PTDoc.Api

# Apply migration (production - SQL Server; uses DesignTimeDbContextFactory)
# EF_PROVIDER=sqlserver Database__ConnectionString="..." \
#   dotnet ef database update -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api

# Apply migration (production - PostgreSQL; uses DesignTimeDbContextFactory)
# EF_PROVIDER=postgres Database__ConnectionString="..." \
#   dotnet ef database update -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api

# Test
dotnet test                     # All tests
dotnet test --filter "Category=Unit"   # Unit tests only
```

---

## AI Development Behavior

### Core Principles

The AI agent must adhere to these behavioral rules in every session:

- **Prefer small, incremental changes.** Each commit should address one concern. Large refactors must be explicitly requested.
- **Avoid refactoring unrelated files.** Only touch files directly relevant to the task at hand.
- **Never delete documentation.** Existing doc files and inline comments must be preserved unless the user explicitly requests removal.
- **Avoid speculative architecture.** Do not add abstractions, interfaces, or patterns that are not required by the current task.
- **Consult repository documentation rather than invent behavior.** When uncertain about how something works in this system, read the relevant doc file before writing code.

### When Uncertainty Exists

When the AI is uncertain about:

| Uncertainty Type | Action Required |
|-----------------|-----------------|
| Data model structure or relationships | Consult the **Backend TDD** |
| Business rule logic or edge cases | Consult the **Master FSD** |
| Clinical note or plan-of-care behavior | Consult the **AI Prompt Specification** |
| Medicare billing or documentation rules | Consult the **Medicare Rules Engine Specification** |
| Sync queue behavior or conflict resolution | Consult the **Offline Sync & Conflict Resolution Specification** |
| Component layout or page routing | Consult the **Blazor Component & Page Mapping** |
| Test coverage expectations | Consult the **QA Acceptance Test Matrix** |
| Architecture layer boundaries | Consult `docs/ARCHITECTURE.md` |
| Database migrations or provider config | Consult `docs/EF_MIGRATIONS.md` |
| Security, auth, or HIPAA controls | Consult `docs/SECURITY.md` |

### Mandatory Changelog Rule

> **Rule ID: AGENT-CHANGELOG-001 — This rule is mandatory and non-negotiable. No session is complete until `docs/CHANGELOG.md` is updated.**
> Full specification: [`.github/agent.md`](agent.md)

**Every working session must end with a changelog update.** A "session" is any continuous interaction or work period where code, configuration, documentation, or system behavior is changed.

#### What counts as a change

| Category | Examples |
|----------|----------|
| **Code** | New/deleted/modified source files |
| **Configuration** | `appsettings*.json`, `.csproj`, `global.json`, CI workflows, `.github/` files |
| **Documentation** | `docs/`, `README.md`, inline comments, any `.md` file |
| **Refactoring** | Renames, structural reorganization, method/class extraction |
| **Behavioral/Logic** | Business rule changes, service behavior, system output changes |
| **Dependencies** | NuGet package additions, removals, or upgrades |
| **Database** | EF Core migrations, schema changes, seed data |
| **Security** | Auth policy changes, role assignments, secret handling |

#### Required entry format

```markdown
### Added | Changed | Fixed | Removed | Security | Deprecated

#### <Feature, System, or Area Name>
- **`<PrimaryFile.cs>` / `<ComponentName>`** — <Concise description>. Affects: <files/systems>. Reason: <purpose>.
```

#### Catch-up requirement

Before starting any new work, verify the last committed change has a changelog entry. If missing, write it first before proceeding.

#### Bypass

The `no-changelog` label may bypass the CI gate **only** for non-user-visible changes (CI-config-only fixes, pure reformats). It must **never** be used to skip changelog entries for substantive code changes.

### Prohibited AI Actions

The AI must **never**:

- Commit or suggest committing secrets, keys, or credentials
- Auto-persist AI-generated clinical content without a clinician acceptance step
- Overwrite finalized or countersigned clinical records
- Log PHI, patient identifiers, or raw tokens
- Introduce new external dependencies without consulting the security advisory database
- Make breaking changes to the sync queue state machine without updating tests
- Remove or skip existing tests to make a build pass

---

## Task-Driven Implementation Pattern

For every task, follow this flow:

1. **Understand** - Restate task in 1-2 lines
2. **Identify** - Which 0-3 docs apply? (Use decision checklist above)
3. **Check** - Are there existing patterns to reuse?
4. **Implement** - Small, incremental changes
5. **Verify** - Build, test, check for errors
6. **Document** - Only if creating new patterns (not every change)

---

**Last Updated:** April 2026 (Sprint I: added AGENT-CHANGELOG-001 mandatory changelog enforcement rule; `.github/agent.md` created as agent behavioral contract)  
**Framework:** .NET 8.0 | **Platforms:** Web, iOS, Android, macOS  
**Healthcare:** HIPAA-conscious design required
