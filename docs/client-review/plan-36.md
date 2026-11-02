# Plan PR 36: Support approved clinician-administered outcome measures

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R067
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-36-clinician-outcomes`
- Base: `main`
- Expected GitHub number: #190; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-36)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The user selected every supplied instrument for the first release: clinician M14–M17 are Berg, 5x sit-to-stand, Tinetti and TUG. The directly supplied clinician folder contains the same four PDF hashes as the original archive; no newer protocol or scoring approval was added. Main has aggregate OutcomeMeasureResult/registry/service contracts, which do not retain the full administered item/trial evidence. Plan 23 (#180) provides the separate versioned-definition/answer/scorer approval boundary and defaults to no activated packages. This PR documents the clinician consumer; it does not implement or activate protocol-aware entry.

## Dependencies, blockers and owners

- January/clinical owner: approve each exact source version, task wording, administration rules, missing/N/A/unable/not-performed semantics and scored example fixtures.
- January/clinical owner: confirm the supplied 5xSTS standing endpoint and the TUG two-counted-trial aggregation rule; no familiar alternative is substituted.
- Content owner: record reproduction permission for each exact supplied version.
- Engineering merge checkpoint: accept Plan 23 shared contracts before implementing independent Plan 36; coordinate completed result/link contracts with Plan 25. Plan 24 is not a prerequisite.

## Decisions and options

| Source | Supplied protocol evidence | Required approval/fixtures |
| --- | --- | --- |
| M14 Berg, 4 pages | 14 tasks, 0–4, total 56; conditional sitting credit after standing two minutes | Conditional scoring, low/high/typical examples and incomplete/unable behavior |
| M15 5x sit-to-stand, 1 content page plus blank page | 16-inch chair; timing ends standing on fifth repetition | Confirm this endpoint, units/precision, assistance/device rules where required, unable/not-performed handling |
| M16 Tinetti, 2 pages | Balance 16 plus gait 12; item weights differ | Item/side mapping and weighted fixtures; incomplete observations |
| M17 TUG, 1 page | 3 m, one practice and two counted trials; back-touching-chair endpoint | Preserve both trials; approve average/best/other combination rather than guessing; invalid/unable trial examples |

All four are in scope; no cohort decision remains. The exact per-instrument release record in [Plan 23 approval matrix](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-23-outcome-contracts/docs/client-review/outcome-approval-matrix.md) remains incomplete. Reproduction and clinical approval are separate from choosing first-release scope.

## Development specification

### User Problem

Some outcomes require clinician observation or timing and cannot be moved into patient self-report intake.

### Confirmed Requirement

Preserve clinician administration for measures such as Berg and Tinetti; supplied timed tests belong to an approved clinician workflow rather than patient questionnaires.

### Expected Behavior

Authorized clinicians record approved task scores or timed trials, with totals/results calculated according to that exact approved protocol and visible provenance.

### Scope

Protocol-aware clinician entry and scorer fixtures, phased by approved instrument.

### Out of Scope

Patient self-administration, unsourced fall-risk interpretations, substituting a different timing endpoint or inventing TUG trial aggregation.

### Implementation Guidance

**Technical Recommendation:** share PR 23 definitions/results but use clinician-specific administration controls. Retain item scores/trials, units, device/help context only where the approved protocol requires it; distinguish unable/not performed from zero.

### Data and Migration Considerations

Preserve legacy aggregate scores and historical protocols. Do not recompute prior results under a newly selected version.

### Security and Privacy Considerations

**Technical Recommendation:** server enforces clinician permission, patient/clinic ownership and immutable signed-note associations.

### Acceptance Criteria

- [ ] **AC1:** Given an approved Berg or Tinetti definition, when valid item observations are entered, then the total matches approved weighted fixtures.
- [ ] **AC2:** Given an approved timed-test protocol, when trials are recorded, then duration units and the approved endpoint/combination rule are preserved.
- [ ] **AC3:** Given a patient intake link, when requesting clinician-only measure entry, then it is unavailable and the API denies unauthorized submission.
- [ ] **AC4:** Given an incomplete/unable assessment, when saved, then it is not silently scored as a complete zero result.

### Rollout and Regression Risks

Protocol substitution would change clinical meaning. Release each approved instrument independently.

## Verification performed

| Command/check | Result |
| --- | --- |
| Compare supplied folder SHA-256 inventory with original archive; inspect Plan 23 approval matrix and existing aggregate registry/service | PASS four clinician source files match; all four selected, protocol gaps and legacy aggregate distinction documented. |
| python3 workflow.py check 36; git diff --check | PASS requirement traceability, named approval gates, source links, sensitive-path and whitespace checks. |

## Remaining acceptance work

Implement after approvals: (1) register trusted definition/scorer/fixture manifests from Plan 23; (2) render clinician-only item observations and raw trials with units and source/version; (3) enforce current clinic/patient, existing note-type write capabilities and clinician administration on the server; reject patient-link submissions even if forged; (4) save incomplete/unable states without a numeric completed-zero result; (5) retain author, encounter/note ID, definition/scorer/source hashes, item/trial evidence and derived result; (6) bind result IDs to Plan 25 signed-note snapshots. Tests must cover weighted/conditional items, trial aggregation, version mismatch, invalid/missing states, denied patient access, other-clinic access and immutable signed associations. Legacy aggregate scores retain their original meaning and remain distinct from new administered results.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
