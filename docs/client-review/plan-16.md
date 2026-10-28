# Plan PR 16: Create appointments from empty clinician slots and enforce schedulable roles

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R032, R033, R034, R035
- Original plan status: Ready for Development
- Branch: `codex/plan-16-empty-schedule-slots`
- Base: `main`
- Expected GitHub number: #170; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-16)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented R032–R035. Today retains every active eligible clinician returned by the authorized clinic roster even with no bookings. Week selection includes eligible clinicians without appointments; historical assignees can still be inspected but cannot create new appointments. Empty slots are native keyboard-operable buttons and populate a stable clinician ID, clinic-local date and time. Existing occupied slots remain appointment-detail actions.

The API overview returns the clinic time-zone identifier. Schedule dates, Today labels, appointment positions, detail/reschedule values and new-slot values now agree with server conversion. Missing timezone metadata from older servers retains the previous local-time fallback; current API always returns the effective zone. DST gap/ambiguous slots are disabled and direct API attempts remain rejected. Duration uses UTC elapsed time.

Only active PT/PTA users (including the existing full-name aliases) in the patient's clinic are assignable. Admin/Owner/PracticeManager and generic Clinician/Provider roles no longer grant assignment eligibility. Settings schedule-block validation uses the same eligibility set. The current model has a single authoritative User.Role; a license number or extra UI/auth claim does not invent a second clinical role. No old appointment is reassigned or deleted. A full edit of an old administrative booking requires choosing an eligible replacement; its details and existing narrow actions remain available under unchanged permissions.

Browser QA also reproduced stale aria-hidden state after cancelling a prefilled modal. Modal accessibility cleanup now runs before parent close notification; successful save defers clearing the keyed initial-value state until close. Both Cancel and Save restore the accessible schedule. No schema migration or deployment.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Existing schedule visibility and server availability rules remain the default. Off-duty column suppression and new non-admin default views are not introduced. The current single-role model admits PT/PTA assignments only; an administrator needs an authoritative clinical-role model before dual-role scheduling can be added. No inferred clinical grant from a license or generic Provider label.

## Development specification

### User Problem

Empty schedules hide useful clinician columns and users cannot create a prefilled appointment from a blank slot.

### Confirmed Requirement

Admin Today shows clinician columns, Week can focus a clinician, empty slots remain usable, and selecting a slot prepopulates clinician/date/time. Schedulable clinicians are PT/PTA. Horizontal scrolling was accepted.

### Expected Behavior

Eligible clinicians are shown independently of bookings, empty slots launch the appointment form and UI/API agree on eligible assignees.

### Scope

Empty-grid population, accessible slot action, prefill and server-enforced role filtering.

### Out of Scope

Schedule faxing, drag-and-drop, availability optimization, or assuming every administrator is a treating clinician.

### Implementation Guidance

**Technical Recommendation:** derive columns from authorized clinic roster and schedule rules, not appointment keys. Use clinic time-zone conversion and existing appointment validation. Permit an admin who also has an eligible clinical role only under the approved role matrix.

### Data and Migration Considerations

No bulk reassignment of old appointments. Historical appointments with now-ineligible assignees remain readable; future edits follow an explicit compatibility rule.

### Security and Privacy Considerations

**Technical Recommendation:** reject ineligible/cross-clinic clinician IDs at API boundaries; UI filtering alone is insufficient.

### Acceptance Criteria

- [ ] **AC1:** Given a day with no bookings and eligible clinicians, when Admin opens Today, then usable clinician/time columns are visible.
- [ ] **AC2:** Given an empty slot, when activated by click or keyboard, then the form has the slot’s clinician/date/time and allows completion.
- [ ] **AC3:** Given Week view, when an individual clinician is selected, then that clinician’s schedule appears without requiring a pre-existing booking.
- [ ] **AC4:** Given an admin-only or other-clinic assignee, when submitting directly to the API, then creation is rejected.
- [ ] **AC5:** Given a daylight-saving boundary, when selecting a slot, then the saved instant and displayed clinic time agree.

### Rollout and Regression Risks

Historical administrative bookings and multi-role users need compatibility review; preserve old records.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Appointment&#124;FullyQualifiedName~Scheduling&#124;FullyQualifiedName~ScheduleBlock' --verbosity minimal -m:1 | PASS: 170 tests, 0 failed/skipped, including real HTTP + SQLite clinic/role enforcement and DST instants. Initial new-fixture database initialization and time-string assertions were corrected. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~AppointmentComponentsTests&#124;FullyQualifiedName~AppointmentsPageTests' --verbosity minimal -m:1 | PASS: 72 tests after the browser-reproduced modal cleanup fix, including cleanup-before-close regression. |
| dotnet format whitespace <changed Core/Application/Infrastructure/Api/Tests project> --no-restore --verify-no-changes --include <changed C# files> | PASS for changed C# files and final added modal test. |
| Playwright CLI local synthetic Today/Week, keyboard prefill, Cancel/Save accessibility, 390/768/1440 viewport checks, post-DST create and SQLite readback | PASS: clinician/date/time retained, empty Week usable; 2026-11-09 09:00 America/Los_Angeles persisted 17:00 UTC and rendered 9:00 AM; page widths exactly 390/768/1440 while schedule scrolls internally. Desktop screenshot visually inspected. |
| python3 workflow.py check 16; git diff --check | PASS requirements, sections, local links, path and whitespace checks. |

## Remaining acceptance work

Client acceptance of off-duty visibility/default-view examples and native MAUI publish/install remain open. Existing clinic-hours, lunch, blocks, buffers and overbooking checks still run on Save; a blank grid cell does not override those rules. Review dual-role modeling separately if required; it is not represented in the current User.Role contract.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
