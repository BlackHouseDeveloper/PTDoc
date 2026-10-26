# Plan PR 03: Correct patient timeline timestamps using the agreed display time zone

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R007
- Original plan status: Verification Required
- Branch: `codex/plan-03-timeline-timezones`
- Base: `main`
- Expected GitHub number: #157; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-03)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

At main 6276027, PatientProfile constructs note/appointment/intake timeline entries with DateTime.SpecifyKind(value, Utc).ToLocalTime(). Display therefore depends on the server process local zone. This is a source-verified presentation dependency, not proof which client-visible time is wrong. Clinic.TimeZoneId exists, but the meeting did not choose clinic versus viewer time. No timestamps were rewritten.

## Dependencies, blockers and owners

- January/product owner: choose clinic-time or viewer-time display, including cross-zone users.
- Engineering/QA: compare the same synthetic UTC note/intake/appointment events on two host zones, including midnight and daylight-saving boundaries, after the display contract is approved.

## Decisions and options

| Option | Behavior | Tradeoff |
| --- | --- | --- |
| Clinic time | Use the existing clinic TimeZoneId for event display | Consistent clinic chronology; remote viewers see clinic-local time |
| Viewer time | Resolve and label the viewer zone | Familiar local time; different viewers can see different dates |

**Unresolved:** Which display zone should be used? Owner: January/product owner. No option is selected by this PR. Event instants and date-only clinical fields must not be migrated by a blanket offset.

## Development specification

### User Problem

The meeting showed timeline dates/times that did not match the participants’ expectation.

### Confirmed Requirement

Investigate and correct patient clinical-info timeline timestamps. The meeting did not specify whether display follows clinic time, browser time or event location.

### Expected Behavior

Timeline ordering and displayed date/time represent the stored event consistently in the agreed zone, including date boundaries.

### Scope

Timestamp source, DateTime kind and presentation conversion for the three timeline event types.

### Out of Scope

Changing dates of service or rewriting historical event instants.

### Implementation Guidance

**Technical Recommendation:** trace UTC values before presentation; distinguish LastModified, submitted and appointment-start events. Use existing clinic TimeZoneId if product selects clinic time; do not assume the developer’s workstation zone. Keep date-only clinical fields date-only.

### Data and Migration Considerations

Normally a presentation fix. Investigate stored data before deciding on any migration; do not apply a blanket time offset.

### Security and Privacy Considerations

**Technical Recommendation:** inspect synthetic events and record only identifiers/reason codes in logs.

### Acceptance Criteria

- [ ] **AC1:** Given the same UTC event in two deployment host time zones, when displayed under the agreed product zone, then the visible date/time is identical.
- [ ] **AC2:** Given events near midnight or a daylight-saving transition, when sorted and rendered, then chronology and local dates remain correct.
- [ ] **AC3:** Given an intake without a submission timestamp, when shown in the timeline, then its documented fallback is used without inventing a completed event.

### Rollout and Regression Risks

A display fix must not shift signed dates or persisted events. No data rewrite unless separately evidenced and reviewed.

## Verification performed

| Command/check | Result |
| --- | --- |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 3 | PASS: requirement IDs, specification/acceptance sections, local links, no private source paths and git diff --check. |

## Remaining acceptance work

AC1–AC3 remain open until the display contract is approved and synthetic conversion/browser tests run.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
