# Plan PR 03: Correct patient timeline timestamps using the agreed display time zone

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R007
- Original plan status: Verification Required
- Branch: `codex/plan-03-timeline-timezones`
- Base: `main`
- Expected GitHub number: #157; actual: [#158](https://github.com/BlackHouseDeveloper/PTDoc/pull/158)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-03)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced on main-derived code with a UTC host: a New York browser displayed synthetic intake instant 2026-10-26T01:30:00Z as 10/26 1:30 AM instead of 10/25 9:30 PM. After the fix, the same stored event displays 10/25/2026 9:30 PM UTC-04:00 with both UTC and Asia/Tokyo hosts. Browser comparisons of all five rendered synthetic rows (notes, appointments and intake) matched their UTC instants and remained in descending instant order. Stored timestamps and date-of-service fields are unchanged. No database migration.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

User confirmed 2026-10-26: display timeline dates/times in the viewer’s local time zone; preserve stored UTC timestamps.

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
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~TimelineTimestampFormatterTests&#124;FullyQualifiedName~PageScopedAppointmentUsageTests&#124;FullyQualifiedName~PatientClinicalInfoCardEditableTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 34 tests, 0 failed, 0 skipped. |
| TZ=UTC and TZ=Asia/Tokyo: dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TimelineTimestampFormatterTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 9 tests in each host time zone. |
| Local Playwright browser comparison, America/New_York viewer; hosts TZ=UTC and TZ=Asia/Tokyo | PASS: original intake instant renders identically at 10/25/2026 9:30 PM UTC-04:00; all rendered event types match raw instants and UTC ordering. |
| dotnet format whitespace (PTDoc.UI and PTDoc.Tests, --no-restore --verify-no-changes --include changed C# files) | PASS. |
| python3 workflow.py check 3; git diff --check | PASS: document sections, requirements, local links and whitespace. |

## Remaining acceptance work

AC1–AC3 passed for the bounded timeline display change. Native MAUI platform publish/install and client acceptance remain unverified.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
