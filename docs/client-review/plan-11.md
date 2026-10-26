# Plan PR 11: Group PHI release information and remove intake revocation controls

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R021, R047
- Original plan status: Ready for Development
- Branch: `codex/plan-11-intake-consent`
- Base: `main`
- Expected GitHub number: #165; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-11)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The existing ReviewStep separated the PHI choice/status from contact and recipient fields, exposed four revocation controls, and displayed stored affirmative flags even when revoked. Canonical persistence also removed recorded revocations when legacy convenience flags were absent/false. The new grouped card preserves the legal choice text and recipient validation. Revocation history is read-only, revoked choices are inactive, and canonical normalization merges legacy revocations without removing recorded keys. No EF schema or stored-data migration.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

The meeting confirms this bounded layout/control removal. No change to legal consent text or the separate organizational revocation process is included.

## Development specification

### User Problem

Related PHI-release details are separated and the intake exposes unwanted revocation controls.

### Confirmed Requirement

Place release consent with authorized contacts/additional details and remove the revoke controls from this intake workflow.

### Expected Behavior

Patients review the relevant release choice and its contact details together; this intake no longer offers the identified revocation controls.

### Scope

Layout and input binding changes, with compatible display of existing packets.

### Out of Scope

Changing legal consent text, eliminating all organizational revocation processes, deleting historical revocation facts or inventing compliance policy.

### Implementation Guidance

**Technical Recommendation:** reuse IntakeConsentPacket and existing validation; remove only the identified entry controls. Preserve backend interpretation of historical values and make review output truthful.

### Data and Migration Considerations

No destructive migration. Existing revoked records retain their state and evidence; do not convert them to consent by omission.

### Security and Privacy Considerations

**Technical Recommendation:** consent changes require current authorized intake context and existing audit/provenance behavior. Do not log contact details.

### Acceptance Criteria

- [ ] **AC1:** Given a patient completing release consent, when reviewing it, then the release choice and authorized-contact details appear together.
- [ ] **AC2:** Given a new intake, when reaching consent, then the removed revoke controls are absent.
- [ ] **AC3:** Given a historical revoked packet, when viewed after rollout, then its recorded state remains unchanged and is not presented as affirmative consent.

### Rollout and Regression Risks

Omitted controls can accidentally reset booleans. Test serialization defaults and historical records explicitly.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 296 tests, 0 failed, 0 skipped, including new/resumed/submitted compatibility cases. |
| Local Playwright: edit synthetic authorized contact, select PHI recipient with Space, continue/save and reload | PASS: grouped release choice, contact and recipient persist. |
| dotnet format whitespace (Application and Tests, --no-restore --verify-no-changes --include affected C# files) | PASS. |
| python3 workflow.py check 11; git diff --check | PASS: requirements, specification, local links and whitespace. |

## Remaining acceptance work

AC1–AC3 verified for new, resumed and submitted packet rendering; contact/recipient save and reload verified locally. Native platform installation and client acceptance remain unverified. The clinic policy owner retains responsibility for its separate authorized revocation process.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
