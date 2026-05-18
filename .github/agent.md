# PTDoc Agent Behavioral Contract

This file defines mandatory behavioral rules for all agents (AI-assisted or automated) operating in the PTDoc repository. Rules in this file are authoritative and non-negotiable.

---

## Mandatory Rule: Changelog Enforcement

### Rule ID: AGENT-CHANGELOG-001

> **This rule is mandatory. No working session is considered complete until the changelog requirement is satisfied. There are no exceptions except as explicitly permitted by the bypass conditions in Section 6.**

---

### 1. Ongoing Session Updates

A changelog entry **must** be created or updated at the end of every working session.

- A **"session"** is defined as any continuous interaction or work period where changes are made to code, configuration, documentation, or system behavior.
- The changelog update **must occur before the session is considered complete**.
- The changelog file is located at: `docs/CHANGELOG.md`
- All entries must be added to the `## [Unreleased]` section under the appropriate sub-heading (`### Added`, `### Changed`, `### Fixed`, `### Removed`, `### Security`, `### Deprecated`).

---

### 2. Retroactive (Catch-Up) Updates

- If any recent changes were made **without** being recorded in the changelog, they **must be identified and documented immediately** at the start of the next session.
- The changelog must always reflect the **current and accurate state** of the project, including all recent modifications.
- When beginning a session, agents must verify whether the last committed change has a corresponding changelog entry. If not, that entry must be written before proceeding with new work.

---

### 3. Definition of a Change

A **"change"** includes any of the following:

| Category | Examples |
|----------|----------|
| **Code** | New files, deleted files, modifications to existing source files |
| **Configuration** | Changes to `appsettings*.json`, `.csproj`, `global.json`, CI/CD workflows, `.github/` files |
| **Documentation** | Updates to `docs/`, `README.md`, inline comments, or any `.md` file |
| **Refactoring** | Structural reorganization, renames, extraction of methods/classes |
| **Behavioral/Logic** | Changes to business rules, service behavior, or system outputs |
| **Dependencies** | Adding, removing, or upgrading NuGet packages or other dependencies |
| **Database** | EF Core migrations, schema changes, seed data modifications |
| **Security** | Auth policy changes, role assignments, secret handling |

---

### 4. Changelog Entry Requirements

Each entry **must** include all of the following:

1. **Concise description** — a clear, human-readable summary of what changed (1–3 sentences).
2. **Affected files, components, or systems** — explicitly list the primary files, namespaces, endpoints, or UI components involved.
3. **Purpose or reason** — explain *why* the change was made (feature addition, bug fix, compliance, refactor, etc.).

**Required format:**

```markdown
### Added | Changed | Fixed | Removed | Security | Deprecated

#### <Feature, System, or Area Name>
- **`<PrimaryFile.cs>` / `<ComponentName>`** — <Concise description of the change>. Affects: <list of affected files/systems>. Reason: <purpose>.
```

**Example:**

```markdown
### Added

#### Intake EnsureDraft API
- **`IntakeEndpoints.cs` / `IIntakeService`** — Added `POST /api/v1/intake/drafts/{patientId}` (EnsureIntakeDraft) and `GET /api/v1/intake/patients/eligible` endpoints. Extends `IIntakeService` with `EnsureDraftAsync` and `SearchEligiblePatientsAsync`. Reason: support clinician-initiated intake draft creation with idempotency and eligibility pre-screening.
```

---

### 5. Consistency and Enforcement

- All entries must follow the **Keep a Changelog** format (<https://keepachangelog.com/en/1.0.0/>).
- The file **must not** contain duplicate entries for the same change.
- This rule applies to **all contributors**: human developers, automated CI agents, and AI-assisted tools.
- **No session is considered complete until `docs/CHANGELOG.md` is fully updated.**
- If a PR is submitted without a changelog update, it is considered **incomplete** unless the `no-changelog` label has been explicitly applied (reserved for non-user-visible changes such as CI config-only fixes or reformats with no behavioral impact).

---

### 6. CI Enforcement

The `changelog-required.yml` workflow enforces this rule at the PR level:
- The PR gate **fails** if `docs/CHANGELOG.md` is not modified in the pull request.
- The gate **passes** when the `no-changelog` label is present (explicit bypass for non-visible changes).
- Agents must never use the `no-changelog` label to bypass changelog requirements for substantive code changes.

---

### Applicability

| Contributor Type | Applies? |
|-----------------|----------|
| Human developer | ✅ Yes |
| GitHub Copilot (AI coding agent) | ✅ Yes |
| Automated CI/CD agent | ✅ Yes |
| GitHub Actions bot (auto-generated) | Exempt if no substantive code change |

---

## Release Branching Guidance

### Rule ID: AGENT-RELEASE-001

Agents must follow the branching and deployment model in `docs/CI.md`.

- Keep `main` as the production-ready branch.
- Do not create a long-lived `production` or `prod` branch only to represent the deployed application.
- Use short-lived `release/vX.Y.Z` branches only for specific release preparation.
- Identify production deployments with immutable release tags such as `v1.0.0`.
- If a deployment provider requires a production branch, document the exception and require branch protection at least as strict as `main`.

---

*These rules are enforced by: `AGENT-CHANGELOG-001`, `AGENT-RELEASE-001` | Last Updated: May 2026*
