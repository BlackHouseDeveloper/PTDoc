# PTDoc Beta End-to-End Testing Plan

## Executive Summary

This plan defines execution-ready end-to-end coverage for the hosted PTDoc beta environment. It is intended for QA engineers, developers, or automated agents validating release readiness across authentication, scheduling, patient management, intake, clinical documentation, payments, integrations, responsive behavior, accessibility, and regression risk.

Primary beta targets:

- Web: `https://ptdoc.bhdevsites.com`
- API: `https://api-ptdoc.bhdevsites.com`

Use the current beta PIN provided out of band by the beta owner. Do not paste the beta PIN into bug reports, screenshots, source files, committed docs, issue text, or chat logs.

Expected behavior comes from this repository, especially `docs/BETA_QA.md` and `docs/audits/ptdoc-live-audit-2026-06-21.md`, plus recent PR intent and any separately shared product, prototype, client-feedback, or audit notes. Treat those sources as intent; classify final results from observable beta behavior only.

The beta should be treated as **Beta Ready** only if seeded Admin, PT, PTA, and Patient users can complete their core workflows without data loss, hidden clinician-only access, unhandled errors, or unusable layout at the documented beta floor viewport.

## Testing Strategy

### Principles

- Test hosted beta first. Use localhost only for reproducing or debugging after a beta issue is logged.
- Use seeded `.test` accounts and seeded patients before creating new data.
- Use fake data only: `Audit Test <timestamp>`, `audit+<timestamp>@example.test`, and clearly fake phone/address values.
- Do not enter real PHI, real payer data, real cards, real credentials, or clinic secrets.
- Do not sign notes, submit irreversible clinical statuses, send external SMS/email, or process real payments unless the beta owner explicitly provides a reversible sandbox fixture and approves the action.
- Record one bug per report with role, route, viewport, steps, expected behavior, observed behavior, severity, and screenshot when safe.
- Treat screenshot/prototype-dependent expectations as **Needs Product Confirmation** unless beta behavior clearly confirms or contradicts them.

### Evidence Required For Each Finding

- Timestamp and timezone.
- Browser, OS, viewport, and zoom.
- Account role and username, but not PIN.
- Exact route or visible page.
- Visible control clicked.
- Expected result with source anchor.
- Observed result.
- Data created or modified.
- Screenshot/video only if it does not reveal real PHI.
- Evidence status: Confirmed, Partially blocked, Unable to verify.

### Recommended Tooling

- Manual browser testing on Chrome or Edge.
- Playwright browser QA where possible:

```bash
cd tests/PTDoc.Web.UiQa
PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com \
PTDOC_UI_QA_USERNAME=<beta-user> \
PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> \
npm run test:responsive
```

Optional browser QA inputs:

- `PTDOC_UI_QA_PT_USERNAME` and `PTDOC_UI_QA_PT_PIN` for PT-role flows.
- `PTDOC_UI_QA_PATIENT_CHART_PATH=/patient/<patient-id>` for known safe patient chart upload coverage.
- `PTDOC_UI_QA_INTAKE_PATH=/intake/<safe-intake-id>` for patient intake coverage.
- `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<draft-note-id>` only for a safe writable draft.

## Test Data And Accounts

Use accounts from `docs/BETA_QA.md`:

| Role | Username | Primary purpose |
| --- | --- | --- |
| Admin | `january.beta` | Dashboard, settings, patient directory, intake admin workflows, non-clinical readiness |
| PT | `dani.beta` | Appointments, note creation/editing, PDF export, AI when enabled |
| PTA | `pta.beta` | PTA note edit/save and PT-only boundary checks |
| Patient | `patient.beta` | Patient-only permissions and intake/patient-facing surfaces |

Seeded beta patient fixtures:

| MRN | Patient | Diagnosis focus |
| --- | --- | --- |
| `BETA-PT-001` | Avery Adams | Right shoulder pain |
| `BETA-PT-002` | Jordan Lee | Low back pain |
| `BETA-PT-003` | Morgan Patel | Right knee pain |
| `BETA-PT-004` | Riley Chen | Neck pain |

Create additional records only when needed, using fake `.test` data. If a change cannot be reverted confidently, stop before the action and mark the scenario Partially blocked or Unable to verify.

## Feature Coverage Matrix

| Area | Critical paths | Roles | Priority | Readiness decision criteria |
| --- | --- | --- | --- | --- |
| Environment and auth | Web/API health, login, logout, protected routes, session refresh | All | Critical | No seeded account is blocked; patient users cannot access clinician routes |
| Navigation and shell | Sidebar/header, dashboard links, direct routes, stale session state | Admin, PT, Patient | High | Routes are predictable and no stale clinician shell appears after logout |
| Dashboard | Tiles, alerts, authorization grouping, recent activity, POC summaries | Admin, PT | High | Tiles route to actionable workflows; alerts are grouped and meaningful |
| Appointments | Today/week views, clinician grouping, details, type edit, check-in gate, copay readiness | Admin, PT, PTA | Critical | Schedule remains readable; copay/check-in gates prevent invalid status changes |
| Patient directory | Search, filters, add patient, add + send intake | Admin | Critical | New fake patient can be created and found; intake handoff is clear |
| Patient chart | Timeline, notes, docs, communications, insurance/auth | Admin, PT | Critical | Tabs are visible, writable where permitted, and persisted after refresh |
| Intake | Send/open link, multi-step form, validation, submission, clinician seeding | Admin, Patient, PT | Critical | Patient can submit; clinician can use intake context in evaluation |
| SOAP workspace | Evaluation, Daily, Progress, Discharge, Dry Needling | PT, PTA | Critical | Draft save/refresh works; note sections preserve data; Review/PDF match content |
| Interventions/CPT/HEP | Row-level CPT, assistance/cueing, response, HEP linkage | PT | Critical | Selections persist across tabs, save, refresh, Review, and export |
| Payments | Authorize.net config, copay due UI, tokenized payment gate, no card storage | Admin, PT | Critical | Copay-required appointments cannot check in unpaid; sandbox payment flow is safe |
| AI prognosis | Prognosis generation, errors, rate limits, body-part correctness | PT | Medium | AI output matches selected body part and failures are localized and safe |
| Global notes | Pagination, filters/search/sort, open/edit consistency | Admin, PT | High | Large lists are bounded and searchable without UI stalls |
| Settings/progress | Settings visibility, progress tracking exploratory areas | Admin, PT | Medium | Critical prototype settings are reachable or documented as limitations |
| Responsive/dark mode/accessibility | 1280x720 floor, mobile/tablet spot checks, keyboard/focus/contrast | All | High | No critical workflow is unreadable or unreachable by keyboard |
| Compliance and audit UX | PHI-safe messages, role boundaries, PDF export, no sensitive logs visible | Admin, PT, Patient | Critical | User-facing failures avoid PHI leakage and destructive actions are gated |

## Pull Request Verification Matrix

| PR / change group | Intended functionality | Expected beta behavior | Positive tests | Negative / regression tests |
| --- | --- | --- | --- | --- |
| Dashboard alerts and appointment detail UX | Role-scoped dashboard data, alert grouping, appointment readiness indicators, week labels | Dashboard alerts route to workflows; appointment details show billing/intake/document readiness and clinician context | Click every dashboard tile; open appointment detail; switch Today/Week | Empty/no-auth alert states do not show misleading groups; dense week view remains usable |
| Patient insurance, authorization, and intake field expansion | Secondary insurance, adjuster fields, cost sharing, visit limits, auth/referral history | Patient Info and intake expose payer/auth fields with validation and persistence | Add/edit fake insurance/auth data; save; refresh | Invalid date ranges, overlapping auth history, bad numeric values block save visibly |
| Patient chart documents and communications | Patient document upload and communication-log storage | Documents and Communications tabs support create/list states | Upload safe text/PDF fixture; add communication log; refresh | Oversized/invalid file type rejected; Patient role cannot access clinician chart storage |
| SOAP workspace intervention, CPT, HEP, export semantics | Row-level intervention CPT, assistance, cueing, response, HEP linkage, PDF/export summaries | Interventions persist and surface in Review/PDF | Add intervention row; set CPT/assistance/cueing/response/HEP; save/refresh/export | Removing CPT clears linked references; blank/invalid rows show clear validation |
| AI prognosis generation | Prognosis prompt/API/UI integration | PT can generate prognosis when AI is enabled; disabled/rate-limited states are explicit | Generate using a safe draft and selected body part | Disabled AI, rate limit, upstream errors do not break workspace or lose draft |
| Live audit remediation and route-backed UI | `/dashboard`, protected fallback, login validation, notes pagination, week grouping, Add Patient, note chooser | Route-backed controls work after refresh and direct URL entry | Direct routes for dashboard/appointments/patients/notes; route-backed modals/tabs | Malformed query params and logout/direct-route access do not break render |
| Repo guidance and QA docs | QA/runbook clarity and ignored artifacts | QA knows how to run beta tests and what not to commit | Verify docs point to beta URLs and safe-data rules | Browser artifacts, local DBs, and secrets are not included in PRs |
| Authorize.net check-in payments | Tokenized AcceptUI copay collection and payment-aware appointment projections | Copay-required appointment shows payment modal and blocks unpaid check-in | Open payment fixture; verify config, amount, modal, sandbox script | Missing token rejected; no real card data stored; placeholder config disables or blocks payment safely |

## Feature Readiness Evaluation Framework

Use these labels consistently in final reports:

| Status | Criteria |
| --- | --- |
| Incomplete | Missing core functionality, blocked by access/data, or cannot be tested end to end. |
| Needs Work | Mostly implemented but has significant issues, inconsistent states, fragile validation, or unclear UX. |
| Beta Ready | Feature-complete enough for beta testers, with minor issues or documented limitations. |
| Release Ready | Stable, complete, consistent, accessible, permission-safe, and suitable for production use. |

For each module, evaluate:

- Core happy path completes.
- Alternate and error paths are visible and recoverable.
- Role permissions match expectations.
- State persists after refresh and route navigation.
- UI is readable at `1280x720`, dark mode, and one mobile/tablet spot check.
- No destructive or irreversible action can be triggered accidentally.
- Bug reports are limited to minor polish for Release Ready.

## End-to-End Test Suites

### Suite E2E-00: Beta Environment Gate

Objective: Confirm hosted beta is reachable and suitable for testing.

Preconditions:

- Beta deployment owner confirms current beta PIN out of band.
- Testers use a clean browser profile or incognito window.

Test data:

- No patient data required.

Steps:

1. Open `https://ptdoc.bhdevsites.com`.
2. Confirm login page loads without broken styling or localhost redirects.
3. Open `https://api-ptdoc.bhdevsites.com/health/live`.
4. Open `https://api-ptdoc.bhdevsites.com/health/ready` once.
5. Inspect browser network origins while loading and logging in.

Expected results:

- Web loads over HTTPS.
- API liveness and readiness are healthy.
- No requests target `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- No certificate, mixed-content, or expired-session errors appear.

Failure conditions:

- Web/API unavailable.
- Unstyled shell, mixed-content warnings, or wrong API origin.
- Seeded users cannot reach login.

Priority: Critical.

### Suite E2E-01: Authentication, Roles, And Sessions

Objective: Verify login, logout, session persistence, and role isolation.

Features covered:

- PIN login.
- Invalid credential handling.
- Protected route redirects.
- Session refresh.
- Patient role restrictions.

Preconditions:

- Seeded beta accounts exist.

Steps:

1. Submit login with blank username and PIN.
2. Submit login with malformed PIN.
3. Submit login with invalid credentials.
4. Login as `january.beta`.
5. Refresh dashboard.
6. Open `/dashboard`, `/appointments`, `/patients`, `/notes`, `/settings`.
7. Logout.
8. Directly open `/dashboard`, `/appointments`, `/notes`.
9. Login as `patient.beta`.
10. Attempt clinician/admin routes directly.

Expected results:

- Inline validation appears before authentication attempt for blank/malformed input.
- Invalid credentials show the established invalid-credential text.
- Admin session persists on refresh.
- Logout returns to login and protected routes redirect.
- Patient sees only patient-appropriate surfaces and cannot access clinician routes.

Failure conditions:

- Redirect loops.
- Stale nav/header after logout.
- Patient role sees Patients, Appointments, Notes, or Settings.
- Validation is missing or misleading.

Regression considerations:

- `/dashboard` alias and auth-aware fallback must remain protected.
- Unknown protected-looking routes must not render the authenticated shell to unauthenticated users.

Priority: Critical.

### Suite E2E-02: Dashboard Workflows

Objective: Verify dashboard tiles, alerts, recent activity, and plan summaries.

Preconditions:

- Login as Admin and PT.
- Seeded notes, intake items, and authorization/referral items should exist where possible.

Steps:

1. Open dashboard as `january.beta`.
2. Record all overview tile labels and counts.
3. Click each tile and confirm destination.
4. Return to dashboard after each tile.
5. Expand/collapse alert groups.
6. Verify Notes and Authorization groups when actionable data exists.
7. Click one alert item from each group.
8. Inspect Recent Activity and Recently Edited Plan of Care.
9. Repeat key dashboard checks as `dani.beta`.

Expected results:

- Tiles route to actionable queues.
- Notes Due routes to the appointment notes-due queue.
- Alert groups are correctly named and collapsible.
- Authorization group appears when seeded actionable authorization/referral data exists.
- Recent POC cards show patient, note type, date/context, and actionable view button.

Failure conditions:

- Tile does nothing or routes to a generic page.
- Authorization alert data exists but no Authorization group appears.
- Alerts open wrong patient/note/workflow.
- Counts and destination lists disagree materially.

Priority: High.

### Suite E2E-03: Appointment Scheduling, Details, And Copay

Objective: Verify appointment schedule usability, details, type edits, note entry, and payment gates.

Preconditions:

- Login as Admin or PT.
- Use seeded appointments and a safe copay fixture if available.
- Use sandbox payment config only; never use real card data.

Steps:

1. Open `/appointments`.
2. Verify Today view date, total counts, clinician grouping, status chips, and note/intake indicators.
3. Open a Scheduled appointment.
4. Inspect patient, MRN, date/time, duration, type, status, intake status, notes, care-team, billing, and document readiness.
5. If editable, change appointment type to a safe alternate, save, verify, then restore.
6. Open Week View.
7. Switch between Clinician and Day grouping.
8. Inspect multiple clinicians at same time slot for readability.
9. Open a no-copay appointment and verify `Record Copay` is hidden or disabled with a reason.
10. Open a copay-required appointment.
11. Click `Record Copay`.
12. Verify amount, patient, appointment time, and payment-required copy.
13. Click `Check In` for copay-required appointment.
14. Confirm unpaid check-in is blocked by payment modal or server-side error.
15. If approved sandbox tokenization is available, complete one sandbox payment with approved test card data only; otherwise stop before card entry.

Expected results:

- Today and Week views load quickly and remain readable.
- Appointment detail modal traps focus, closes with Escape/backdrop/Close, and does not clip footer.
- Type edit is reversible and reflected in schedule.
- Copay-required appointment cannot be checked in unpaid.
- Payment config exposes only client-safe fields.
- No card number or raw token is stored or displayed by PTDoc.

Failure conditions:

- Week View does not switch or labels become unreadable.
- Check In changes status before payment when copay is due.
- Payment action appears active while config is placeholder/missing.
- Authorize.net sandbox modal does not open despite real sandbox client config.
- Real payment data is required.

Priority: Critical.

### Suite E2E-04: Patient Directory And Add Patient

Objective: Verify patient search, filtering, add patient, and add-plus-intake handoff.

Preconditions:

- Login as Admin.
- Use fake `.test` data.

Steps:

1. Open `/patients`.
2. Search by each seeded MRN and patient name.
3. Clear search and verify list returns.
4. Open Add Patient.
5. Attempt save with missing required fields.
6. Enter fake patient demographics, primary insurance, secondary insurance, PCP, referring provider, authorization/referral, and notes where available.
7. Save as Add Patient.
8. Search for the new fake patient.
9. Repeat with a second fake patient using `Add Patient + Send Intake`.
10. Verify Send Intake opens preselected to the new patient.
11. Copy link/QR if available; do not send external SMS/email.

Expected results:

- Search supports names, MRNs, and email where applicable.
- Required field validation is inline and specific.
- Cursor focus remains stable while typing.
- Add Patient closes after success and created patient appears/searches.
- Add Patient + Send Intake preselects the created patient and does not require duplicate search.

Failure conditions:

- Modal unreadable or background bleeds through.
- Cursor jumps back to first name.
- Created patient missing from search/list.
- External send occurs without confirmation.

Priority: Critical.

### Suite E2E-05: Patient Chart, Insurance, Documents, Communications

Objective: Verify patient chart tabs and newly first-class chart workflows.

Preconditions:

- Login as Admin and PT.
- Use seeded patients first; use fake patient for write tests.

Steps:

1. Open each seeded patient profile.
2. Visit Timeline, Notes, Documents, Communications, and Insurance & Authorization.
3. Add or edit fake cost-sharing values, visit limits, primary/secondary payer details, and authorization/referral data.
4. Add two authorization/referral history records with non-overlapping dates.
5. Attempt overlapping authorization dates.
6. Save and refresh.
7. Upload a safe small `.pdf` or `.txt` document with document type and notes.
8. Attempt invalid file type and oversized file if test fixture exists.
9. Add communication log entry with channel, direction, contact, summary, and details.
10. Refresh and verify document/log persistence.
11. Login as Patient and attempt patient chart URLs.

Expected results:

- Chart navigation remains stable after refresh/direct links.
- Insurance/auth save persists and invalid values block save.
- Overlapping authorization dates are rejected.
- Documents upload/list with type, file name, notes, created date, and no unsafe filename behavior.
- Communications list persists and clears stale success/error messages correctly.
- Patient role cannot access clinician chart storage.

Failure conditions:

- Tabs are dead, hidden, or reload duplicate/stale patient data.
- Invalid dates/numbers save silently.
- Upload accepts unsafe file types or oversized files.
- Cross-patient document or communication data appears.

Priority: Critical.

### Suite E2E-06: Patient Intake End To End

Objective: Verify send/open/complete/submit intake and clinician consumption.

Preconditions:

- Admin can generate a safe intake link.
- Use fake patient or seeded patient explicitly approved for intake testing.

Steps:

1. Create or select fake patient.
2. Open Send Intake.
3. Generate link and QR; do not send external email/SMS.
4. Open intake link in separate browser context.
5. Confirm progress indicator and next/back behavior.
6. Submit with missing required fields once.
7. Complete demographics, address, sex at birth, body region, pain severity, functional limitations, prior/current function, insurance, secondary insurance where available, liability/adjuster fields, authorized contacts, PCP/referring provider, surgeries, outcome measures, and legal acknowledgement.
8. Use keyboard to select a body-map region.
9. Submit successfully.
10. Confirm success message appears at submit area.
11. Login as PT and start Evaluation for the patient.
12. Verify intake data seeds the Subjective/Objective areas where intended.

Expected results:

- Required validation is inline and focusable.
- Pain severity requires explicit interaction; `0` is valid only when selected intentionally.
- Body map works with mouse and keyboard.
- Liability payer type triggers adjuster fields.
- Submitted intake confirmation is visible without scrolling to top.
- Clinician workflow clearly indicates intake-sourced data.

Failure conditions:

- Intake link blocked or expired without recovery.
- Required validation appears only at top or not at all.
- Body map inaccessible by keyboard.
- Submitted data missing from clinician evaluation.
- Real external send required.

Priority: Critical.

### Suite E2E-07: Evaluation Note Workspace

Objective: Verify Evaluation SOAP workflow, autosave, validation, AI, and PDF/export readiness.

Preconditions:

- Login as PT.
- Use fake patient or seeded patient approved for note-write tests.

Steps:

1. Start Evaluation note from patient chart.
2. Confirm tabs: Subjective, Objective, Assessment, Plan, Review.
3. Enter body part, side/location, chief complaint, symptoms, frequency, imaging details, prior/current function, and limitations.
4. Add ROM/MMT metrics, special tests, posture, outcome measure, and comments.
5. Verify normal/previous/current value behavior.
6. Generate AI assessment/prognosis if enabled.
7. Add Plan fields, CPT/modifier settings if present, and follow-up/discharge planning.
8. Wait 10-20 seconds for autosave.
9. Save draft.
10. Refresh and verify persistence.
11. Open Review.
12. Export/preview PDF if supported and safe.

Expected results:

- Tabs load without blank screens or unhandled-error banner.
- Data persists through tab navigation, autosave, manual save, refresh, and Review.
- AI output references selected body part and returns localized errors if disabled/rate-limited.
- PDF/review content matches entered structured data.

Failure conditions:

- Autosave loses data.
- Review blocked without clear validation summary.
- AI uses wrong body part or writes stale template text.
- PDF omits key structured content.

Priority: Critical.

### Suite E2E-08: Daily, Progress, Discharge, And Dry Needling Notes

Objective: Verify note-type-specific behavior and client feedback alignment.

Preconditions:

- Login as PT.
- Use safe writable draft fixtures.

Steps:

1. Start Daily Treatment note from appointment and patient chart.
2. Verify Subjective mirrors patient daily questions where available.
3. Verify Interventions tab, Treatment Focus 1/2, row-level CPT, assistance, cueing, response, and HEP linkage.
4. Verify Assessment contains only intended Additional Notes fields.
5. Verify Plan is labeled `Plan for next visit`.
6. Save, refresh, Review, and export.
7. Start Progress note and verify carried-forward activities, pain control, prior/current objective values, and progress questionnaire behavior.
8. Start Discharge note and verify discharge-specific subjective, reason list, objective carry-forward, plan/review alignment, and non-billable discharge templates if available.
9. Start Dry Needling note and verify dropdowns, non-billable indication, save/review/export behavior.

Expected results:

- Each note type has its own appropriate fields and labels.
- Interventions own CPT/HEP where client requested.
- Carry-forward data is visible but editable where appropriate.
- Dry Needling is clearly non-billable if that requirement is implemented.
- No unhandled-error banner appears.

Failure conditions:

- Daily/Progress/Discharge reuse inappropriate fields.
- Interventions/CPT/HEP fail to persist.
- Non-billable workflows are missing where claimed.
- PDF/review omits daily/progress/discharge details.

Priority: Critical.

### Suite E2E-09: Global Notes And Patient Notes

Objective: Verify notes listing, pagination, filters, opening, editing, and consistency.

Preconditions:

- Login as Admin and PT.

Steps:

1. Open `/notes`.
2. Verify first render is bounded around 50 notes.
3. Click Load More until no more records or a safe limit is reached.
4. Use status, type, date, patient search, and sort controls where available.
5. Open a draft note.
6. Compare global note editor/open behavior with patient-specific Notes tab.
7. Save draft changes only in a safe fixture.

Expected results:

- Large lists do not render hundreds of records at once.
- Filters reset pagination.
- Open/edit controls match patient-specific notes.
- Validation and save feedback are consistent.

Failure conditions:

- Load More repeats or skips records.
- Filters/search do not affect result set.
- Global note edit lacks patient-note validations.

Priority: High.

### Suite E2E-10: Payments And Authorize.net

Objective: Verify beta payment integration without real card storage or accidental live charges.

Preconditions:

- Beta owner confirms payment environment is sandbox or explicitly disabled.
- A safe copay-required appointment exists.
- Sandbox test card use is approved before tokenization.

Steps:

1. Login as Admin or PT.
2. Open a no-copay appointment.
3. Verify payment action is hidden/disabled with a clear reason.
4. Open copay-required appointment.
5. Verify `Copay due $X.XX`, `Record Copay`, and payment summary.
6. Confirm unpaid `Check In` opens payment-required modal or returns a handled error.
7. Inspect network/config behavior through user-visible outcomes only; do not expose secrets.
8. If sandbox credentials are active and approved, open hosted Authorize.net form and submit approved sandbox card.
9. Verify success status, appointment check-in if requested, payment transaction id/status, and audit-safe user feedback.
10. Attempt declined sandbox card if approved.

Expected results:

- Client-safe configuration never exposes transaction key.
- Placeholder/missing config disables or clearly blocks payment.
- Successful sandbox payment records transaction and prevents duplicate charge.
- Declines and validation errors are visible and do not check in the appointment.

Failure conditions:

- Active Pay button with placeholder credentials.
- Check-in bypasses required copay.
- Card data is stored or displayed in PTDoc.
- Duplicate payment possible after success.

Priority: Critical.

### Suite E2E-11: Responsive, Dark Mode, And Accessibility

Objective: Verify usable workflows across viewport/theme/keyboard conditions.

Preconditions:

- Test at minimum `1280x720`, `1440x900`, and mobile-ish `430x932`.

Steps:

1. Test Dashboard, Appointments Week View, Patients, Add Patient modal, Patient chart, Intake, Notes editor, and Payment modal at `1280x720`.
2. Repeat key flows in dark mode.
3. Tab through header, navigation, first form, modal, and note workspace.
4. Use Escape/backdrop where modals allow dismiss.
5. Verify focus rings and visible labels.
6. Spot-check mobile/tablet layout and horizontal overflow.

Expected results:

- Critical controls remain reachable.
- Modal focus does not escape.
- Dark-mode contrast is readable.
- No horizontal document overflow except intended internal schedule scrolling.
- Space/Enter work for keyboard-operable controls.

Failure conditions:

- Text overlaps or clips in buttons/modals/cards.
- Focus is invisible.
- Mobile traps user in horizontal scroll.
- Dark mode makes labels unreadable.

Priority: High.

### Suite E2E-12: Settings, Progress Tracking, And Admin Areas

Objective: Verify exploratory but beta-visible admin and progress surfaces.

Preconditions:

- Login as Admin and PT.

Steps:

1. Open Settings as Admin.
2. Confirm settings categories are visible or clearly unavailable.
3. Attempt Settings as PT/Patient to verify access boundaries.
4. Open Progress Tracking.
5. Confirm data source, empty state, or beta limitation message.
6. Verify no dead buttons or broken routes.

Expected results:

- Admin can reach expected beta settings.
- Non-admin users see appropriate boundaries.
- Progress Tracking is either usable or explicitly marked exploratory/empty.

Failure conditions:

- Settings page inaccessible to Admin without explanation.
- Patient can access admin settings.
- Progress Tracking shows misleading or broken state.

Priority: Medium.

## Regression Test Plan

Run these after any beta deploy or hotfix:

1. Login/logout/protected-route smoke for all roles.
2. Dashboard tile routing and alert expansion.
3. Appointments Today/Week and one appointment detail modal.
4. Add Patient modal open, validation, and cancel.
5. Patient chart tabs direct-link refresh.
6. Documents and Communications tab load states.
7. Intake link open and first-step validation.
8. Evaluation draft save/refresh.
9. Daily note Interventions/Plan/Review navigation.
10. Global Notes bounded pagination.
11. Copay-required appointment unpaid check-in block.
12. Dark-mode smoke on dashboard, appointments, and note workspace.
13. Patient role direct-route denial.

## Release Readiness Checklist

The beta is ready for broader release only when all Critical items pass:

- [ ] Web and API beta health pass.
- [ ] All seeded roles can login with current out-of-band PIN.
- [ ] Patient role cannot access clinician/admin surfaces.
- [ ] Admin can complete dashboard, patient search, patient chart, and intake entry workflows.
- [ ] PT can open appointment, create/edit note draft, save, refresh, review, and export.
- [ ] PTA boundaries are clear and not bypassable.
- [ ] Intake can be completed with fake patient data and appears in clinician workflow.
- [ ] Documents and Communications persist on patient chart.
- [ ] Insurance/auth invalid data is blocked and valid fake data persists.
- [ ] Copay-required check-in is blocked until sandbox payment success or clearly disabled.
- [ ] No visible unhandled-error banner appears in core flows.
- [ ] No critical layout issue at `1280x720`.
- [ ] Dark mode is readable on dashboard, appointments, and notes.
- [ ] All known limitations are documented and separated from regressions.

## Recommended Testing Order

1. Environment Gate.
2. Authentication and roles.
3. Dashboard.
4. Appointments and copay gates.
5. Patient directory and patient chart.
6. Intake create/send/complete/clinician seed.
7. Evaluation note save/review/export.
8. Daily/Progress/Discharge/Dry Needling notes.
9. Global Notes.
10. Payments with approved sandbox only.
11. Settings/Progress Tracking.
12. Responsive, dark mode, keyboard/accessibility pass.
13. Regression sweep and release readiness scoring.

## Risks And Areas Requiring Additional Validation

- Beta PIN and seeded data can change outside the repo; always confirm with the beta owner before testing.
- Authorize.net should not be fully exercised unless sandbox credentials and safe fixtures are explicitly confirmed.
- AI generation may be disabled or rate-limited in beta; record environment state separately from product bugs.
- Prototype screenshot-dependent comments cannot be fully validated from text-only docs.
- Non-billable discharge and dry-needling billing defaults may need product confirmation.
- Week View may pass functionally while still failing client readability expectations at high clinician density.
- Intake-to-evaluation mapping requires a safe submitted-intake fixture to verify completely.
- Review/PDF export must be checked with realistic structured note content, not empty drafts.
- Mobile/tablet behavior may differ from desktop browser emulation; at least one real Apple/iPad pass remains valuable because prior feedback mentioned iPad sync and dark-mode readability.

## Bug Severity Guide

| Severity | Definition | Examples |
| --- | --- | --- |
| Critical | Blocks beta, data integrity, privacy, auth, payment safety, or note persistence | Patient sees clinician data; draft save loses content; payment bypasses copay gate |
| High | Blocks a major role workflow or creates serious user confusion | Add Patient cannot save; Week View unreadable; intake cannot submit |
| Medium | Non-blocking but frequent friction or partial client-comment mismatch | Sort missing, dense layout, unclear alert category |
| Low | Polish, copy, minor visual consistency | Label wording, spacing, non-critical icon alignment |

## Final Report Template

Use this shape after executing the plan:

```text
Environment:
Date/time:
Tester:
Browser/device:
Beta Web/API build or deployment identifier:
Accounts used:

Summary:
- Overall readiness:
- Critical blockers:
- High-priority issues:
- Unable-to-verify areas:

Feature readiness:
- Auth:
- Dashboard:
- Appointments:
- Patients:
- Intake:
- Notes:
- Payments:
- Responsive/accessibility:

Findings:
1. Title
   Route:
   Role:
   Steps:
   Expected:
   Actual:
   Severity:
   Evidence:
   Source anchor:

Release recommendation:
- Proceed / Proceed with limitations / Hold beta
```
