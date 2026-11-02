# Plan PR 25: Carry computed outcomes into clinical notes with provenance

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R066
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-25-outcome-note-projection`
- Base: `main`
- Expected GitHub number: #179; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-25)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Existing `OutcomeMeasureResult` records PatientId, MeasureType, Score, DateRecorded, ClinicianId, optional NoteId and ClinicId. It cannot prove item-level computation or an exact version. Plan 23 #180 introduces provenance without migrating aggregates. Plans 24 and 36 still lack approved instrument/result persistence and Plan 08 is partial. Therefore a computed note projection cannot yet consume completed contracts without inventing associations. This PR documents the explicit result-linking and signed-snapshot design; no runtime or data migration changes.

## Dependencies, blockers and owners

- Engineering: complete Plan 24 patient responses and Plan 36 clinician results after Plan 23 #180; their independent branches must reconcile on a common merged foundation before Plan 25 implementation.
- Engineering: Plan 08 must provide stable encounter/note-type intake routing; never use latest patient intake/result as an implicit current result.
- January/clinical owner: approve correction and prior-result display policy; no reassignment or retrospective rescore is authorized.

## Decisions and options

Proposed invariant: current means explicitly associated with this encounter, never latest in an array. Prior candidates must share patient/clinic and compatible instrument/version, precede completion time, and visibly retain their source/version. Pending clinical choice: which prior result(s) to display and how attributable corrections are requested/reviewed. Until decided, no automatic correction, reassignment or cross-version trend calculation.

## Development specification

### User Problem

Clinicians should not retype patient-completed scores or confuse historical scores with current measurements.

### Confirmed Requirement

Carry calculated intake outcome results into evaluation, progress and discharge as read-only results while preserving clinician-administered measures as a separate workflow.

### Expected Behavior

Notes show the associated instrument, score, completion date and version/source. Clinicians cannot overwrite a computed score as ordinary free text.

### Scope

Encounter/result association, read-only presentation and export compatibility; distinguish current from prior results.

### Out of Scope

Rescoring historical results under new algorithms, new daily outcome collection or copying the latest patient result into every encounter without linkage.

### Implementation Guidance

**Technical Recommendation:** link immutable completed result IDs to the intended note and snapshot necessary display metadata when signing. Corrections should create an attributable corrected result rather than rewriting signed content.

### Data and Migration Considerations

Preserve prior aggregate/manual results and signed-note snapshots. Define current/prior ordering by encounter/completion metadata, not array order.

### Security and Privacy Considerations

**Technical Recommendation:** enforce note/patient/result clinic alignment and signed-note immutability server-side.

### Acceptance Criteria

- [ ] **AC1:** Given a completed intake result linked to an evaluation/progress/discharge encounter, when its note opens, then the correct computed score and provenance appear read-only.
- [ ] **AC2:** Given several results for the same instrument, when displaying current and prior, then the associated encounter determines current and historical values remain distinct.
- [ ] **AC3:** Given a signed note, when a later questionnaire is completed, then the signed note’s recorded result does not silently change.
- [ ] **AC4:** Given a clinician-entered administered measure, when displayed, then its source is distinguished from patient questionnaire scoring.

### Rollout and Regression Risks

Wrong-encounter carry-forward is the main risk; require explicit association and visible provenance.

## Verification performed

| Command/check | Result |
| --- | --- |
| python3 workflow.py check 25; git diff --check | Required traceability sections, local links, sensitive-path and whitespace checks pass. Documentation only; no feature test claimed. |

## Remaining acceptance work

All four projection acceptance criteria remain open. This planning PR does not display computed note results. Implement after both consumer result contracts and encounter routing are ready; test multiple encounters, equal timestamps, corrected results, foreign-clinic IDs and immutable signed export snapshots.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
