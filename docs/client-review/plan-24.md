# Plan PR 24: Deliver approved patient questionnaires with automatic scoring

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R011
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-24-patient-outcomes`
- Base: `main`
- Expected GitHub number: #178; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-24)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Inspected `OutcomeMeasuresStep.razor`: recommendations are followed by Previous Outcome Measure or Functional Score text entry, including instrument and score text. This is historical self-report, not item-level scoring. Existing `OutcomeMeasureResult` stores a numeric aggregate and optional note/clinic IDs without response, definition or scorer versions. Plan 23 [#180](https://github.com/BlackHouseDeveloper/PTDoc/pull/180) now supplies tested versioned contracts and an inactive-by-default server registry. The user selected all 17 supplied instruments, including 13 patient-reported instruments. All 16 newly linked Drive files exactly match the ZIP sources. Cohort selection is resolved; no new clinical approval or assignment mapping was supplied. This PR changes documentation only.

## Dependencies, blockers and owners

- January/clinical owner: approve the exact per-instrument definitions, source anomalies, missing/N/A rules and fixtures in the Plan 23 approval matrix; all 13 patient instruments are selected but none is activated.
- January/clinical owner: approve assignment by body region and encounter/note type, including which of standard ODI, Modified ODI and Roland–Morris applies. All-in-scope does not mean every patient receives every instrument.
- Engineering: consume Plan 23 #180 before implementing its independent patient branch; coordinate Plan 08 encounter/repeat intake binding. No duplicate foundation or activation bypass.

## Decisions and options

Cohort decision: all supplied patient instruments M01–M13. Pending: approved assignment matrix (region × encounter type → instrument/version), whether additional instruments require clinician assignment, and the per-instrument approval record. Do not infer diagnosis-based assignment. Existing historical manual-score text remains distinct from future computed responses.

## Development specification

### User Problem

Patients currently face a score-entry prompt rather than the questions needed to calculate a trustworthy result.

### Confirmed Requirement

Patients answer applicable outcome questions and PTDoc calculates scores; do not ask patients to compute or supply the resulting numeric score.

### Expected Behavior

Patients receive assigned approved questionnaires, can resume them, and see clear completion/review behavior; the server produces versioned results.

### Scope

First delivery PR: one approved cohort and reusable renderer. **Technical Recommendation:** follow with independent instrument-content PRs, each reviewed and fixture-tested, rather than enabling all 17 instruments at once.

### Out of Scope

Clinician-administered Berg/Tinetti/timed tests, guessed missing-answer rules or removal of historical manually entered score evidence.

### Implementation Guidance

**Technical Recommendation:** use PR 23 definitions and server scorers; accessible radio/choice controls, clear N/A versus unanswered semantics, progress and resumability. Derive assignment from approved clinical rules, not ad hoc diagnosis inference.

### Data and Migration Considerations

Persist item answers, completion state and scorer/definition version. Keep historical prior-score text separately identified; do not relabel it a newly computed result.

### Security and Privacy Considerations

**Technical Recommendation:** validate patient-link assignment and submission state; prevent posting answers/results for another patient or arbitrary instrument.

### Acceptance Criteria

- [ ] **AC1:** Given an assigned approved patient measure, when the patient answers and submits, then the server computes the approved result without asking for a numeric score.
- [ ] **AC2:** Given a partially completed questionnaire, when resumed, then prior answers are preserved and missing responses follow the instrument rule.
- [ ] **AC3:** Given a tampered client total, when submitted, then the server ignores/rejects it and calculates from validated responses.
- [ ] **AC4:** Given a clinician-administered measure, when a patient opens intake, then it is not presented for self-administration.

### Rollout and Regression Risks

Instrument burden and scoring errors; release a small approved cohort, then expand per-instrument gates.

## Verification performed

| Command/check | Result |
| --- | --- |
| python3 workflow.py check 24; git diff --check | Required traceability sections, local links, sensitive-path and whitespace checks pass. Documentation only; no feature test claimed. |

## Remaining acceptance work

AC1–AC4 remain open. No questionnaire renderer, response persistence, assignment endpoint or clinical scorer is implemented in this planning PR. After approval, implement one versioned instrument at a time within the selected all-instruments release scope, verify fixtures/resume/tenancy and then activate that version.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
