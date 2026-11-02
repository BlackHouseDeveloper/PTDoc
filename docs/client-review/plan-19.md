# Plan PR 19: Add clinician-authored patient alerts to chart and note headers

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R043
- Original plan status: Needs Clarification
- Branch: `codex/plan-19-patient-alerts`
- Base: `main`
- Expected GitHub number: #173; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-19)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The current [patient context header](../../src/PTDoc.UI/Components/Notes/PatientContextHeader.razor) displays patient identity and DOB. It is reused by [note workspace](../../src/PTDoc.UI/Pages/Patient/NoteWorkspacePage.razor) and [patient profile](../../src/PTDoc.UI/Pages/PatientProfile.razor). That is the appropriate shared display boundary after alert semantics are approved.

The [clinical information card](../../src/PTDoc.UI/Components/Patients/Profile/PatientClinicalInfoCardEditable.razor) has timeline, notes, documents and communications sections, but no clinician-authored patient-alert tab. [PatientProfileVm](../../src/PTDoc.UI/Components/Patients/Profile/Models/PatientProfileVm.cs) has FlagsAndComorbidities; these existing labels do not establish alert authorship or history and must not be silently converted.

[UserNotification](../../src/PTDoc.Core/Models/UserNotification.cs), [notification writer](../../src/PTDoc.Infrastructure/Services/UserNotificationWriter.cs) and [DbContext](../../src/PTDoc.Infrastructure/Data/ApplicationDbContext.cs) implement a per-user inbox. Its read/archive lifecycle is different from an active patient clinical alert. The inspected model has no matching patient-alert aggregate. No tab, alert persistence, permission or signed-note behavior is implemented in this planning PR.

## Dependencies, blockers and owners

- January / clinical owner: approve author/edit/deactivate rights, including another clinician’s alert; active/history lifecycle; and signed-note display semantics. AC1, AC3 and AC4 remain gated.
- Engineering: choose revision/audit persistence and migration behavior only after the approved lifecycle; existing inbox notifications do not implement patient clinical alerts.

## Decisions and options

**Unresolved — January / clinical owner approval required.** A question is pending; current roles, flag meanings and signed notes are preserved.

| Decision | Options to decide | Acceptance impact |
| --- | --- | --- |
| Author roles | Explicitly list PT, PTA and any additional allowed clinical roles | AC1/AC4; no Admin/Owner grant inferred from administrative access |
| Edit someone else’s alert | Author only; authorized clinic clinicians; designated role | AC1/AC4; explicit audit and optimistic concurrency required |
| Deactivate / reactivate | Decide who can do each action and whether reason is required | AC3/AC4; do not equate inactive with deleted |
| Active vs historical display | Active header list with separate history; other agreed presentation | AC1/AC3; history must stay available to authorized users |
| Signed-note view | Clearly separate current chart alerts from immutable note; show a signing-time snapshot; neither until specified | AC1/AC3; do not silently insert current text into signed content or PDF |
| Edit history | Full text revisions, event audit or approved retention representation | AC3; no destructive overwrite without the agreed historical record |
| Presentation limits | Agree on useful length, active count, collapse/expand and urgency semantics | Avoid hidden alerts and invented clinical severity; narrow-screen acceptance |

Record the chosen permissions matrix and signed-view behavior with an owner/date and one example of an edited, deactivated and historically signed alert. General notification permissions are insufficient approval.

### Proposed implementation contract after approval

1. Keep a clinic/patient-linked clinical-alert entity separate from UserNotification. Candidate fields are ID, clinic/patient IDs, text, author, creation/update UTC timestamps, concurrency version and active/deactivation metadata. Revision/audit representation follows the approved retention decision; these are design candidates, not a committed schema.
2. Application contracts separate list/create/edit/deactivate operations. Infrastructure enforces active clinic membership, patient access and the approved role/ownership matrix. Infer actor and clinic from server context, validate text, reject stale versions and audit identifier/action metadata without copying alert text into logs.
3. Add the patient Notifications tab with explicit active/history views and accessible empty/loading/error states. Reuse PatientContextHeader for applicable alerts across Evaluation, Daily, Progress and Discharge; clear old patient state immediately on route changes and ignore stale async responses.
4. Any current-chart alert shown beside a signed note must be visibly separate from its immutable content. A signing-time snapshot, if chosen, needs a versioned export contract and explicit approval; do not retrofit existing signed JSON or PDFs.
5. Preserve author and prior history on edit/deactivation. Do not repurpose FlagsAndComorbidities, inbox IsRead or IsArchived as the alert lifecycle. No external messages, diagnosis generation or AI severity are part of this feature.
6. After decisions, generate additive migrations for all providers, verify up/down behavior with historical text preservation, and document rollout for old clients. Enforce permissions on direct API calls, not only in the authoring UI.

### Verification specification after approval

Use synthetic patients in two clinics. Verify allowed and denied create/edit/deactivate for each role/ownership pair, reload persistence, concurrent edits, historical revision access, inactive alert display, signed-note invariance, and revoked access. Rapidly switch patients during delayed alert loading to prove no prior-patient leakage. Exercise all four note types, long text, keyboard navigation, screen-reader announcements and 390/768/1440 widths. Assert alert text is absent from notification previews, analytics and logs.

## Development specification

### User Problem

Clinicians need important patient-specific reminders visible while documenting instead of buried in the chart.

### Confirmed Requirement

Add a clinician-authored Notifications tab and show persistent patient alerts in the note header.

### Expected Behavior

Authorized clinicians can maintain patient alerts and see applicable alerts consistently across note types.

### Scope

Patient-alert create/edit/display and a clear active/inactive lifecycle using existing clinician access boundaries.

### Out of Scope

Automated diagnosis alerts, externally sent messages, AI risk prediction or replacing handoff inbox notifications.

### Implementation Guidance

**Technical Recommendation:** keep patient clinical alerts distinct from user inbox delivery. Reuse shared header to avoid inconsistent visibility and define authorship/timestamp/version fields with server validation.

### Data and Migration Considerations

Likely additive clinic/patient-linked alert persistence; preserve prior text or audit history on edit/deactivation. Do not migrate unrelated flag meanings without mapping.

### Security and Privacy Considerations

**Technical Recommendation:** patient access controls apply to alert text; avoid sensitive text in push/email previews or analytics. No cross-clinic retrieval.

### Acceptance Criteria

- [ ] **AC1:** Given an authorized clinician, when saving a patient alert, then it appears in that patient’s Notifications tab and note headers after reload.
- [ ] **AC2:** Given a different patient, when opening a note, then the previous patient’s alert is absent.
- [ ] **AC3:** Given a deactivated alert, when opening a new note, then active presentation follows the agreed lifecycle while history remains accessible to authorized users.
- [ ] **AC4:** Given a patient or unauthorized user, when attempting authoring directly, then existing clinician restrictions are enforced.

### Rollout and Regression Risks

Overly broad or stale alerts can overwhelm documentation. Keep initial scope manual and patient-specific.

## Verification performed

| Command/check | Result |
| --- | --- |
| Read-only review of PatientContextHeader, NoteWorkspacePage, PatientProfile, PatientClinicalInfoCardEditable, PatientProfileVm, UserNotification, UserNotificationWriter and ApplicationDbContext | PASS: shared display seam and distinct inbox/flag models verified; no matching patient clinical-alert aggregate or authoring tab in inspected source. |
| python3 workflow.py check 19; git diff --check | PASS: requirements, sections, local source links, path and whitespace checks. No unrelated .NET tests required for documentation-only changes. |

## Remaining acceptance work

AC1/AC3/AC4 require the clinical decisions; AC2 must be verified once patient-alert loading exists. No feature acceptance is claimed. After approval, implement persistence/authorization/shared display, migrations and the cross-patient, history, signed-record and responsive tests above.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
