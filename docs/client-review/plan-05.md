# Plan PR 05: Decide organization and clinic ownership before migrating tenancy

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R001, R085
- Original plan status: Needs Clarification
- Branch: `codex/plan-05-organization-decision`
- Base: `main`
- Expected GitHub number: #159; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-05)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Current main still models Clinic and User.ClinicId. Settings #153 establishes server-enforced clinic role/capability services; it does not approve organization-wide PHI visibility. The meeting proposes local customer administration and an organization-above-clinic model but leaves membership and ownership rules unresolved.

## Dependencies, blockers and owners

- January with engineering/security: approve organization versus clinic ownership, staff membership, invitation/approval powers and cross-clinic clinical access before any schema/access migration.

## Decisions and options

| Decision | Options to review (none selected) | Affected acceptance |
| --- | --- | --- |
| Ownership | Independent clinics; organization with separately administered clinics | AC1 |
| Staff membership | One clinic per user; explicit many-clinic memberships | AC1–AC3 |
| Parent administrator access | Administrative metadata only; separately granted clinical memberships | AC1/AC3 |
| Legacy migration | Explicit organization backfill after ownership review; retain current clinic IDs | AC2 |

Neutral question: who owns each organization/clinic, who may invite/approve clinicians, and which clinics’ patient records may each role access? Owner: January with engineering/security. All answers remain unresolved.

## Development specification

### User Problem

The current clinic-first model does not express the proposed organization owner managing multiple clinics and their staff.

### Confirmed Requirement

The developer proposed organization above clinic; January wants a customer’s owner/admin to add its clinicians without platform-owner approval for every person. Exact hierarchy, cross-clinic access and approval authority remain unresolved.

### Expected Behavior

A decision brief establishes ownership, membership, invitations, cross-clinic access and conversion rules before schema work starts.

### Scope

Document an explicit role/tenant matrix and migration design with single-clinic and multi-clinic examples.

### Out of Scope

Automatic access to all clinics in an organization, platform-wide PHI access, billing implementation or schema mutation in this planning task.

### Implementation Guidance

**Technical Recommendation:** retain clinic data boundaries while adding organization relationships only after approval. Analyze null/legacy clinic IDs, background jobs, sync/export/fax scopes and claims refresh. Choose additive backfill and validation rather than silently changing every ClinicId meaning.

### Data and Migration Considerations

Potential migration of clinics, users and role relationships; patient/note ownership must remain stable. Signed records must not be reassigned as a side effect of subscription or organization setup.

### Security and Privacy Considerations

**Technical Recommendation:** explicitly separate platform administration, organization administration and clinical access. No implicit parent-organization access to clinical records.

### Acceptance Criteria

- [ ] **AC1:** Given a proposed organization with two clinics, when the client reviews the role matrix, then permitted patient access and staff administration are explicitly recorded for each role.
- [ ] **AC2:** Given legacy single-clinic users and records, when a migration rehearsal runs after approval, then every record retains a verifiable owning clinic and no access broadens unintentionally.
- [ ] **AC3:** Given an administrator from another organization, when accessing clinic records or memberships, then access is denied.

### Rollout and Regression Risks

Highest architectural risk: accidental access expansion and destructive tenant remapping. Block schema implementation until ownership decisions are explicit.

## Verification performed

| Command/check | Result |
| --- | --- |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 5 | PASS: requirement IDs, specification/acceptance sections, local links, no private source paths and git diff --check. |

## Remaining acceptance work

Decision approval precedes schema design. Then rehearse with two organizations/three synthetic clinics, legacy users, claims refresh, exports/fax/sync and denied cross-organization access.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
