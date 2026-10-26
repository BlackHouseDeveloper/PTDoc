# Plan PR 06: Decide individual subscription and organization onboarding rules

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R084, R086
- Original plan status: Needs Clarification
- Branch: `codex/plan-06-onboarding-decision`
- Base: `main`
- Expected GitHub number: #160; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-06)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Current UserRegistrationService requires an active clinic and retains approval checks. Existing copay payment workflows are not subscription entitlements. The meeting favors immediate paid individual EMR-only access and organization demo/onboarding, then explicitly leaves alternatives for further exploration.

## Dependencies, blockers and owners

- January/business owner: approve individual/organization signup, payment provider and server entitlement lifecycle, staff limits, failure recovery and treatment of existing pending registrations.
- Plan 05 ownership decisions precede organization-dependent onboarding implementation.

## Decisions and options

| Decision | Options to review (none selected) | Affected acceptance |
| --- | --- | --- |
| Individual activation | Confirmed paid entitlement grants access; retained manual approval | AC1 |
| Organization onboarding | Demo plus approved provisioning; another explicitly approved flow | AC1 |
| Provisioning failure | Idempotent retry/repair before access; explicit manual recovery | AC2 |
| Existing registrations | Retain pending status; reviewed migration with explicit approval | AC3 |

Neutral question: is the favored EMR-only immediate-access proposal approved, and which verified server event grants or removes access? Owner: January/business owner. No provider, price, trial or payment policy is chosen here.

## Development specification

### User Problem

An individual buyer should not wait for the platform owner to manually approve use, while organizations need a different onboarding path.

### Confirmed Requirement

January favored immediate paid single-provider EMR access, no added staff and no billing bundle; organization customers would use a demo/onboarding flow. The discussion ended with further exploration, so it does not approve a production entitlement flow.

### Expected Behavior

A signed-off decision defines registration, payment confirmation, access, limits, organization enrollment and account recovery before code is scoped.

### Scope

Decision and integration brief with example success/failure journeys; keep individual versus organization paths explicit.

### Out of Scope

Choosing prices, payment provider, trials, tax policy, recurring-charge terms or deadlines.

### Implementation Guidance

**Technical Recommendation:** separate identity, tenant membership and paid entitlements; never grant access solely from a browser success redirect. Reuse registration validation and server-side authorization; specify idempotent provisioning and failure recovery after product decisions.

### Data and Migration Considerations

Existing pending registrations, memberships and approved users need an explicit compatibility policy. Do not auto-approve existing users during migration.

### Security and Privacy Considerations

**Technical Recommendation:** no card data in PTDoc logs; use the selected provider’s approved payment flow. Paid access cannot bypass clinic isolation or clinician scope.

### Acceptance Criteria

- [ ] **AC1:** Given a proposed individual signup, when the decision is approved, then its payment trigger, immediate-access boundary and staff limit are unambiguous.
- [ ] **AC2:** Given failed or duplicated payment/provisioning events, when the eventual implementation processes them, then it neither grants unpaid access nor creates duplicate organizations.
- [ ] **AC3:** Given an existing pending registration, when onboarding changes roll out, then its approved migration rule is applied without silently broadening access.

### Rollout and Regression Risks

Commercial and access-control risks are unresolved; no payment or production-access work is ready.

## Verification performed

| Command/check | Result |
| --- | --- |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 6 | PASS: requirement IDs, specification/acceptance sections, local links, no private source paths and git diff --check. |

## Remaining acceptance work

After Plan 05 and commercial decisions, design entitlement state transitions and test provider sandbox success/failure/duplicates, existing pending users and clinic isolation.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
