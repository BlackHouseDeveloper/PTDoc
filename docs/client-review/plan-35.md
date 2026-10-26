# Plan PR 35: Decide the zero-authorization-visit documentation safeguard

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R089
- Original plan status: Needs Clarification
- Branch: `codex/plan-35-authorization-safeguard`
- Base: `main`
- Expected GitHub number: #189; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-35)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

PatientInsuranceAuthorization stores nullable decimal AuthorizedUnits/UsedUnits, status, date range, visit-limit period, alert threshold and policy/patient/clinic identity. InsurancePolicyService and sync validate nonnegative inputs; policy writes optionally check ExpectedLastModifiedUtc. The schema does not establish that a unit always equals a visit. Existing RulesEngine progress-note warnings/stops use their own note-count/date policy, not the authorization balance. ClinicalVisitOrdinalAllocator allocates appointment visit order with unique-index protection; it is not an atomic authorization reservation ledger. No runtime zero-balance conversion defect is claimed, and third-party signing anecdotes do not establish one.

## Dependencies, blockers and owners

- January/operations owner: choose warning, Progress-note suggestion or enforced block; specify the triggering action and approved override roles/reasons.
- Clinical/operations owner: define units versus visits, multiple-policy precedence, expired/pending coverage, cancellations/corrections and when utilization is consumed.
- Engineering: after policy approval, define authoritative concurrency/reservation behavior for simultaneous visits; ordinal uniqueness alone is insufficient.

## Decisions and options

| Synthetic case | Distinction that must remain visible | Decision still required |
| --- | --- | --- |
| Approved, active dates, authorized 10 / used 10 | Known exhausted balance | Warn only, suggest Progress, or block which action? |
| Authorized 10 / used 9 | One remaining unit, not necessarily one visit | Warn at configured threshold? When is the unit consumed? |
| Authorized null or used null | Unknown, not zero | Explain missing coverage and permitted workflow |
| Pending request with zero entered | Pending, not approved exhausted coverage | Different warning and override? |
| Approved but end date passed | Expired, distinct from exhausted | Applicable dates/time zone and behavior |
| Primary exhausted; secondary active | Multiple policies cannot be blindly summed | Which policy/service applies and who selects it? |
| Two simultaneous encounters with one unit remaining | Race between read and consumption | Reserve, recount at a named action, or warn without reservation? |
| Correction/cancellation after consumption | Reversible accounting event | Approved correction roles and audit/reversal semantics |

Any option must preserve the clinician’s draft type and text. A Progress suggestion requires explicit choice and never manufactures authorization. No warning/block/override option is approved in this PR.

## Development specification

### User Problem

The client wants to avoid discovering too late that authorized visits are exhausted and supporting progress documentation is needed.

### Confirmed Requirement

Unlike the surrounding third-party complaints, January explicitly connected a zero-visit safeguard to PTDoc. Defaulting toward progress documentation was exploratory and lacks approved trigger/override rules.

### Expected Behavior

An approved policy defines when to warn, suggest progress documentation or restrict an action, and who can override it.

### Scope

Decision with concrete remaining-visit/date/multiple-policy examples; implementation only after approval.

### Out of Scope

Automatic note-type conversion, fabricated authorizations, bypassing signature controls or building third-party billing/copay integrations.

### Implementation Guidance

**Technical Recommendation:** compute eligibility using authoritative server data and existing visit accounting; distinguish pending requests, expired authorizations, exhausted visits and unknown coverage. If adopted, show an explainable suggestion before creating a note.

### Data and Migration Considerations

No automatic migration of prior notes or authorization balances. Define how concurrent visits and corrections affect counts before changing workflow.

### Security and Privacy Considerations

**Technical Recommendation:** any override must follow approved role/audit rules; authorization status must not silently change clinical documentation.

### Acceptance Criteria

- [ ] **AC1:** Given approved examples for zero, unknown and available visits, when evaluated, then each produces the explicitly agreed warning/suggestion/block behavior.
- [ ] **AC2:** Given a draft of another note type, when authorization status changes, then its type/content is not silently converted.
- [ ] **AC3:** Given simultaneous visit creation, when counts change, then the safeguard uses authoritative consistent utilization.

### Rollout and Regression Risks

An aggressive safeguard may block necessary clinical work; an overly permissive one may mislead. Policy must precede implementation.

## Verification performed

| Command/check | Result |
| --- | --- |
| Inspect InsuranceModels, InsurancePolicyService, SyncEngine, RulesEngine and ClinicalVisitOrdinalAllocator | PASS current fields, validation and distinct accounting concerns documented; no production data or runtime defect claimed. |
| python3 workflow.py check 35; git diff --check | PASS traceability, decision-case coverage, links, sensitive-path and whitespace checks. |

## Remaining acceptance work

AC1–AC3 require the named policy decision. Then implement a server-owned eligibility result with reason codes and observed authorization version, reconcile all relevant write paths, and test same-clinic scope, unknown/expired/pending distinctions, simultaneous actions, corrections and role-bound overrides. Preserve existing progress-frequency and signature enforcement.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
